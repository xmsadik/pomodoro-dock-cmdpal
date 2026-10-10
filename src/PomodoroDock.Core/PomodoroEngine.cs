namespace PomodoroDock.Core;

/// <summary>One calendar day and its totals.</summary>
public readonly record struct DayEntry(DateOnly Date, DayStats Stats);

/// <summary>
/// The Pomodoro state machine. Thread-safe: one internal lock guards its own fields (and the store
/// writes, which are plain file IO); it never calls back into the caller while locked. Every public
/// operation first completes any phase that already ran out, so callers get those completions back
/// in <see cref="EngineResult.Completed"/> and can notify.
/// </summary>
public sealed class PomodoroEngine
{
    /// <summary>A phase that ended no more than this long ago may chain straight into an auto-started next phase.</summary>
    public static readonly TimeSpan ChainWindow = TimeSpan.FromSeconds(90);

    private const int RetentionDays = 90;

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly IStateStore _stateStore;
    private readonly IHistoryStore _historyStore;
    private readonly Dictionary<DateOnly, DayStats> _history;
    private PomodoroOptions _options;
    private TimerState _state;

    public PomodoroEngine(TimeProvider time, PomodoroOptions options, IStateStore stateStore, IHistoryStore historyStore)
    {
        _time = time;
        _options = options.Clamped();
        _stateStore = stateStore;
        _historyStore = historyStore;
        _state = Sanitize(stateStore.Load());
        _history = historyStore.Load();
    }

    /// <summary>The phase "Skip" moves to from <paramref name="state"/>.</summary>
    public static Phase NextAfterSkip(TimerState state, PomodoroOptions options) =>
        state.Phase == Phase.Focus ? NextBreak(state.CompletedInCycle, options) : Phase.Focus;

    /// <summary>What follows a finished focus session once <paramref name="completedInCycle"/> is counted.</summary>
    public static Phase NextBreak(int completedInCycle, PomodoroOptions options) =>
        completedInCycle >= options.LongBreakEvery ? Phase.LongBreak : Phase.ShortBreak;

    /// <summary>The phase that follows <paramref name="finished"/> when it runs to its end, and whether it would auto-start.</summary>
    public static (Phase Next, bool AutoStart) PredictNext(Phase finished, int completedInCycleBefore, PomodoroOptions options) =>
        finished == Phase.Focus
            ? (NextBreak(completedInCycleBefore + 1, options), options.AutoStartBreaks)
            : (Phase.Focus, options.AutoStartFocus);

    /// <summary>Current state without advancing it.</summary>
    public EngineSnapshot Snapshot()
    {
        lock (_gate)
        {
            return Build(_time.GetUtcNow());
        }
    }

    /// <summary>Completes a phase that ran out (tick and startup).</summary>
    public EngineResult Advance()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var completed = new List<PhaseCompleted>();
            var historyChanged = AdvanceCore(now, completed);
            if (completed.Count > 0)
            {
                Persist(now, historyChanged);
            }

            return new EngineResult(Build(now), completed);
        }
    }

    /// <summary>Idle starts, Running pauses, Paused resumes.</summary>
    public EngineResult StartOrToggle() => Mutate(now =>
    {
        var s = _state;
        switch (s.Status)
        {
            case RunStatus.Idle:
                _state = StartPhase(s.Phase, s.CompletedInCycle, now);
                break;
            case RunStatus.Running:
                _state = s with
                {
                    Status = RunStatus.Paused,
                    Remaining = Max0((s.EndsAt ?? now) - now),
                    EndsAt = null,
                    NotificationScheduledFor = null,
                };
                break;
            default:
                _state = s with
                {
                    Status = RunStatus.Running,
                    EndsAt = now + (s.Remaining ?? s.PhaseDuration ?? _options.DurationOf(s.Phase)),
                    Remaining = null,
                    NotificationScheduledFor = null,
                };
                break;
        }
    });

    /// <summary>Back to an idle first focus session of a fresh cycle.</summary>
    public EngineResult Reset() => Mutate(_ => _state = new TimerState());

    /// <summary>Leaves the current phase without crediting it.</summary>
    public EngineResult Skip() => Mutate(now =>
    {
        var s = _state;
        var next = NextAfterSkip(s, _options);
        var cycle = s.Phase == Phase.LongBreak ? 0 : s.CompletedInCycle;
        var auto = next == Phase.Focus ? _options.AutoStartFocus : _options.AutoStartBreaks;
        _state = auto
            ? StartPhase(next, cycle, now)
            : new TimerState { Phase = next, CompletedInCycle = cycle };
    });

    /// <summary>New options apply to future phases only; a running or paused phase keeps its timing.</summary>
    public EngineResult UpdateOptions(PomodoroOptions options) => Mutate(_ => _options = options.Clamped());

    /// <summary>
    /// Records that a Windows toast exists for <paramref name="endsAt"/> (or none, when null). Ignored if the
    /// running phase has moved on, so a slow scheduler cannot stamp a stale value.
    /// </summary>
    public void SetNotificationScheduled(DateTimeOffset? endsAt)
    {
        lock (_gate)
        {
            if (endsAt is not null && (_state.Status != RunStatus.Running || _state.EndsAt != endsAt))
            {
                return;
            }

            if (_state.NotificationScheduledFor == endsAt)
            {
                return;
            }

            _state = _state with { NotificationScheduledFor = endsAt };
            _stateStore.Save(_state);
        }
    }

    public DayStats GetToday()
    {
        lock (_gate)
        {
            return _history.GetValueOrDefault(DayOf(_time.GetUtcNow()));
        }
    }

    /// <summary>The last <paramref name="n"/> days, today first, including days with no activity.</summary>
    public IReadOnlyList<DayEntry> GetLastDays(int n)
    {
        lock (_gate)
        {
            var today = DayOf(_time.GetUtcNow());
            var days = new List<DayEntry>(n);
            for (var i = 0; i < n; i++)
            {
                var day = today.AddDays(-i);
                days.Add(new DayEntry(day, _history.GetValueOrDefault(day)));
            }

            return days;
        }
    }

    private static TimeSpan Max0(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    /// <summary>Repairs a loaded state whose fields disagree with its status.</summary>
    private static TimerState Sanitize(TimerState? loaded)
    {
        if (loaded is null)
        {
            return new TimerState();
        }

        var cycle = Math.Max(0, loaded.CompletedInCycle);
        return loaded.Status switch
        {
            RunStatus.Running when loaded.EndsAt is not null =>
                loaded with { Remaining = null, CompletedInCycle = cycle },
            RunStatus.Paused when loaded.Remaining is not null =>
                loaded with { EndsAt = null, NotificationScheduledFor = null, CompletedInCycle = cycle },
            _ => new TimerState { Phase = loaded.Phase, CompletedInCycle = cycle },
        };
    }

    /// <summary>Runs <paramref name="change"/> after completing anything overdue, persists, and returns the result.</summary>
    private EngineResult Mutate(Action<DateTimeOffset> change)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var completed = new List<PhaseCompleted>();
            var historyChanged = AdvanceCore(now, completed);
            change(now);
            Persist(now, historyChanged);
            return new EngineResult(Build(now), completed);
        }
    }

    private TimerState StartPhase(Phase phase, int completedInCycle, DateTimeOffset startAt)
    {
        var duration = _options.DurationOf(phase);
        return new TimerState
        {
            Phase = phase,
            Status = RunStatus.Running,
            EndsAt = startAt + duration,
            PhaseDuration = duration,
            CompletedInCycle = completedInCycle,
        };
    }

    /// <summary>
    /// Completes the running phase when its end has passed. Catch-up is deliberately shallow: only the
    /// phase that was running is credited; the next one chains on only if we were ticking (within
    /// <see cref="ChainWindow"/>), otherwise it waits idle. Returns true when history changed.
    /// </summary>
    private bool AdvanceCore(DateTimeOffset now, List<PhaseCompleted> completed)
    {
        var historyChanged = false;
        while (_state is { Status: RunStatus.Running, EndsAt: { } ends } s && now >= ends)
        {
            var finished = s.Phase;
            var day = DayOf(ends);
            var stats = _history.GetValueOrDefault(day);
            var cycle = s.CompletedInCycle;
            Phase next;
            switch (finished)
            {
                case Phase.Focus:
                    var credit = s.PhaseDuration ?? _options.DurationOf(Phase.Focus);
                    cycle++;
                    stats = stats with { Pomodoros = stats.Pomodoros + 1, FocusSeconds = stats.FocusSeconds + (long)credit.TotalSeconds };
                    next = NextBreak(cycle, _options);
                    break;
                case Phase.ShortBreak:
                    stats = stats with { ShortBreaks = stats.ShortBreaks + 1 };
                    next = Phase.Focus;
                    break;
                default:
                    stats = stats with { LongBreaks = stats.LongBreaks + 1 };
                    cycle = 0;
                    next = Phase.Focus;
                    break;
            }

            _history[day] = stats;
            historyChanged = true;

            var wantsAuto = next == Phase.Focus ? _options.AutoStartFocus : _options.AutoStartBreaks;
            var chain = wantsAuto && now - ends <= ChainWindow;
            _state = chain
                ? StartPhase(next, cycle, ends)
                : new TimerState { Phase = next, CompletedInCycle = cycle };
            completed.Add(new PhaseCompleted(finished, next, chain, ends, s.NotificationScheduledFor == ends));
        }

        return historyChanged;
    }

    private void Persist(DateTimeOffset now, bool historyChanged)
    {
        _stateStore.Save(_state);
        if (!historyChanged)
        {
            return;
        }

        var cutoff = DayOf(now).AddDays(-RetentionDays);
        foreach (var old in _history.Keys.Where(d => d < cutoff).ToList())
        {
            _history.Remove(old);
        }

        _historyStore.Save(_history);
    }

    private DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone).DateTime);

    private EngineSnapshot Build(DateTimeOffset now)
    {
        var remaining = _state.Status switch
        {
            RunStatus.Running => Max0((_state.EndsAt ?? now) - now),
            RunStatus.Paused => Max0(_state.Remaining ?? TimeSpan.Zero),
            _ => _options.DurationOf(_state.Phase),
        };
        var today = DayOf(now);
        return new EngineSnapshot(_state, _options, remaining, now, today, _history.GetValueOrDefault(today));
    }
}
