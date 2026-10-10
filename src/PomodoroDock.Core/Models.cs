using System.Text.Json.Serialization;

namespace PomodoroDock.Core;

/// <summary>The kind of interval the timer is on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Phase>))]
public enum Phase
{
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>Whether the current phase is ticking.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunStatus>))]
public enum RunStatus
{
    Idle,
    Running,
    Paused,
}

/// <summary>User-tunable durations and automation. Values below 1 are clamped by the engine.</summary>
public sealed record PomodoroOptions(
    int FocusMinutes = 25,
    int ShortBreakMinutes = 5,
    int LongBreakMinutes = 15,
    int LongBreakEvery = 4,
    bool AutoStartBreaks = true,
    bool AutoStartFocus = false)
{
    /// <summary>Configured length of <paramref name="phase"/>.</summary>
    public TimeSpan DurationOf(Phase phase) => TimeSpan.FromMinutes(phase switch
    {
        Phase.Focus => FocusMinutes,
        Phase.ShortBreak => ShortBreakMinutes,
        _ => LongBreakMinutes,
    });

    /// <summary>Copy with every value forced into a sane range.</summary>
    internal PomodoroOptions Clamped() => this with
    {
        FocusMinutes = Math.Max(1, FocusMinutes),
        ShortBreakMinutes = Math.Max(1, ShortBreakMinutes),
        LongBreakMinutes = Math.Max(1, LongBreakMinutes),
        LongBreakEvery = Math.Max(1, LongBreakEvery),
    };
}

/// <summary>The persisted timer state. Immutable; the engine swaps it on every change.</summary>
public sealed record TimerState
{
    public Phase Phase { get; init; } = Phase.Focus;

    public RunStatus Status { get; init; } = RunStatus.Idle;

    /// <summary>Completion instant; set only while Running.</summary>
    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>Time left; set only while Paused.</summary>
    public TimeSpan? Remaining { get; init; }

    /// <summary>Focus sessions finished since the last long break (or reset).</summary>
    public int CompletedInCycle { get; init; }

    /// <summary>Length the running or paused phase was started with, so a settings change cannot mis-credit it.</summary>
    public TimeSpan? PhaseDuration { get; init; }

    /// <summary>The EndsAt for which a Windows scheduled toast is known to exist.</summary>
    public DateTimeOffset? NotificationScheduledFor { get; init; }
}

/// <summary>Per-day totals. Focus time is credited when a focus phase completes.</summary>
public readonly record struct DayStats(int Pomodoros, long FocusSeconds, int ShortBreaks, int LongBreaks)
{
    public static DayStats Empty => default;

    public bool IsEmpty => Pomodoros == 0 && FocusSeconds == 0 && ShortBreaks == 0 && LongBreaks == 0;

    public static DayStats operator +(DayStats a, DayStats b) => new(
        a.Pomodoros + b.Pomodoros, a.FocusSeconds + b.FocusSeconds, a.ShortBreaks + b.ShortBreaks, a.LongBreaks + b.LongBreaks);
}

/// <summary>A phase that ran to its end. <see cref="ToastScheduled"/> is true when Windows was already asked to show its toast.</summary>
public sealed record PhaseCompleted(Phase Finished, Phase Next, bool NextAutoStarted, DateTimeOffset At, bool ToastScheduled);

/// <summary>An immutable view of the engine at one instant.</summary>
public sealed record EngineSnapshot(
    TimerState State,
    PomodoroOptions Options,
    TimeSpan Remaining,
    DateTimeOffset Now,
    DateOnly Today,
    DayStats TodayStats)
{
    public Phase Phase => State.Phase;

    public RunStatus Status => State.Status;

    /// <summary>The phase "Skip" would move to.</summary>
    public Phase SkipTarget => PomodoroEngine.NextAfterSkip(State, Options);
}

/// <summary>Snapshot after an operation plus the phases that finished while performing it (caller notifies).</summary>
public sealed record EngineResult(EngineSnapshot Snapshot, IReadOnlyList<PhaseCompleted> Completed);
