using Xunit;
using PomodoroDock.Core;

namespace PomodoroDock.Tests;

public class EngineTests
{
    private static readonly DateTime Start = new(2026, 10, 10, 10, 0, 0);
    private static readonly PomodoroOptions Manual = new(AutoStartBreaks: false, AutoStartFocus: false);
    private static readonly PomodoroOptions Both = new(AutoStartBreaks: true, AutoStartFocus: true);

    private sealed class Rig
    {
        public Rig(PomodoroOptions? options = null, DateTime? now = null)
        {
            Time = new FakeTimeProvider(now ?? Start);
            Options = options ?? new PomodoroOptions();
            Engine = new PomodoroEngine(Time, Options, State, History);
        }

        public FakeTimeProvider Time { get; }

        public PomodoroOptions Options { get; }

        public MemoryStateStore State { get; } = new();

        public MemoryHistoryStore History { get; } = new();

        public PomodoroEngine Engine { get; }

        public void Wait(TimeSpan span) => Time.LocalNow += span;

        public EngineResult Run(TimeSpan span)
        {
            Wait(span);
            return Engine.Advance();
        }
    }

    [Fact]
    public void Fresh_engine_is_idle_focus_with_full_duration()
    {
        var rig = new Rig();
        var s = rig.Engine.Snapshot();

        Assert.Equal(Phase.Focus, s.Phase);
        Assert.Equal(RunStatus.Idle, s.Status);
        Assert.Equal(TimeSpan.FromMinutes(25), s.Remaining);
        Assert.Equal(0, s.State.CompletedInCycle);
    }

    [Fact]
    public void Four_focus_sessions_lead_to_a_long_break_with_short_breaks_in_between()
    {
        var rig = new Rig(Manual);
        var finished = new List<(Phase, Phase)>();

        for (var i = 0; i < 4; i++)
        {
            rig.Engine.StartOrToggle();
            foreach (var c in rig.Run(TimeSpan.FromMinutes(25)).Completed)
            {
                finished.Add((c.Finished, c.Next));
            }

            if (i < 3)
            {
                Assert.Equal(Phase.ShortBreak, rig.Engine.Snapshot().Phase);
                rig.Engine.StartOrToggle();
                foreach (var c in rig.Run(TimeSpan.FromMinutes(5)).Completed)
                {
                    finished.Add((c.Finished, c.Next));
                }
            }
        }

        Assert.Equal(Phase.LongBreak, rig.Engine.Snapshot().Phase);
        Assert.Equal(RunStatus.Idle, rig.Engine.Snapshot().Status);
        Assert.Equal(4, rig.Engine.Snapshot().State.CompletedInCycle);
        Assert.Equal(
            [
                (Phase.Focus, Phase.ShortBreak), (Phase.ShortBreak, Phase.Focus),
                (Phase.Focus, Phase.ShortBreak), (Phase.ShortBreak, Phase.Focus),
                (Phase.Focus, Phase.ShortBreak), (Phase.ShortBreak, Phase.Focus),
                (Phase.Focus, Phase.LongBreak),
            ],
            finished);

        var today = rig.Engine.GetToday();
        Assert.Equal(4, today.Pomodoros);
        Assert.Equal(4 * 25 * 60, today.FocusSeconds);
        Assert.Equal(3, today.ShortBreaks);

        // The long break resets the cycle.
        rig.Engine.StartOrToggle();
        rig.Run(TimeSpan.FromMinutes(15));
        var after = rig.Engine.Snapshot();
        Assert.Equal(Phase.Focus, after.Phase);
        Assert.Equal(0, after.State.CompletedInCycle);
        Assert.Equal(1, rig.Engine.GetToday().LongBreaks);
    }

    [Fact]
    public void Default_options_auto_start_the_break_chained_at_the_previous_end()
    {
        var rig = new Rig();
        rig.Engine.StartOrToggle();
        var endOfFocus = rig.Engine.Snapshot().State.EndsAt!.Value;

        var result = rig.Run(TimeSpan.FromMinutes(25) + TimeSpan.FromSeconds(1));
        var s = result.Snapshot;

        Assert.Single(result.Completed);
        Assert.True(result.Completed[0].NextAutoStarted);
        Assert.Equal(Phase.ShortBreak, s.Phase);
        Assert.Equal(RunStatus.Running, s.Status);
        Assert.Equal(endOfFocus + TimeSpan.FromMinutes(5), s.State.EndsAt);
        Assert.Equal(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1), s.Remaining);
    }

    [Fact]
    public void Skipping_focus_is_not_counted_and_goes_to_the_break()
    {
        var rig = new Rig(Manual);
        rig.Engine.StartOrToggle();
        rig.Wait(TimeSpan.FromMinutes(10));

        var s = rig.Engine.Skip().Snapshot;

        Assert.Equal(Phase.ShortBreak, s.Phase);
        Assert.Equal(RunStatus.Idle, s.Status);
        Assert.Equal(0, s.State.CompletedInCycle);
        Assert.True(rig.Engine.GetToday().IsEmpty);
    }

    [Fact]
    public void Skipping_focus_before_the_fourth_pomodoro_still_leads_to_a_short_break()
    {
        var rig = new Rig(Manual);
        for (var i = 0; i < 3; i++)
        {
            rig.Engine.StartOrToggle();
            rig.Run(TimeSpan.FromMinutes(25));
            rig.Engine.Skip(); // skip the break
        }

        Assert.Equal(3, rig.Engine.Snapshot().State.CompletedInCycle);
        Assert.Equal(Phase.Focus, rig.Engine.Snapshot().Phase);

        // Three completed: a skipped focus still leads to a short break; the long one needs the fourth.
        Assert.Equal(Phase.ShortBreak, rig.Engine.Snapshot().SkipTarget);
        rig.Engine.StartOrToggle();
        rig.Run(TimeSpan.FromMinutes(25));
        Assert.Equal(Phase.LongBreak, rig.Engine.Snapshot().Phase);
    }

    [Fact]
    public void Skipping_the_long_break_resets_the_cycle()
    {
        var rig = new Rig(new PomodoroOptions(LongBreakEvery: 1, AutoStartBreaks: false));
        rig.Engine.StartOrToggle();
        rig.Run(TimeSpan.FromMinutes(25));
        Assert.Equal(Phase.LongBreak, rig.Engine.Snapshot().Phase);

        var s = rig.Engine.Skip().Snapshot;

        Assert.Equal(Phase.Focus, s.Phase);
        Assert.Equal(0, s.State.CompletedInCycle);
        Assert.Equal(0, rig.Engine.GetToday().LongBreaks);
    }

    [Fact]
    public void Skip_from_running_starts_the_next_phase_now_when_it_auto_starts()
    {
        var rig = new Rig();
        rig.Engine.StartOrToggle();
        rig.Wait(TimeSpan.FromMinutes(3));

        var s = rig.Engine.Skip().Snapshot;

        Assert.Equal(Phase.ShortBreak, s.Phase);
        Assert.Equal(RunStatus.Running, s.Status);
        Assert.Equal(TimeSpan.FromMinutes(5), s.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(5), s.State.PhaseDuration);
    }

    [Fact]
    public void Skip_works_from_paused_and_idle()
    {
        var rig = new Rig(Manual);
        Assert.Equal(Phase.ShortBreak, rig.Engine.Skip().Snapshot.Phase); // idle focus -> break

        rig.Engine.StartOrToggle();
        rig.Engine.StartOrToggle(); // paused
        Assert.Equal(RunStatus.Paused, rig.Engine.Snapshot().Status);
        var s = rig.Engine.Skip().Snapshot;
        Assert.Equal(Phase.Focus, s.Phase);
        Assert.Equal(RunStatus.Idle, s.Status);
        Assert.Null(s.State.Remaining);
    }

    [Fact]
    public void Pause_and_resume_preserve_the_remaining_time()
    {
        var rig = new Rig();
        rig.Engine.StartOrToggle();
        rig.Wait(TimeSpan.FromMinutes(10));

        var paused = rig.Engine.StartOrToggle().Snapshot;
        Assert.Equal(RunStatus.Paused, paused.Status);
        Assert.Equal(TimeSpan.FromMinutes(15), paused.Remaining);
        Assert.Null(paused.State.EndsAt);

        rig.Wait(TimeSpan.FromHours(2)); // paused time does not count
        Assert.Equal(TimeSpan.FromMinutes(15), rig.Engine.Advance().Snapshot.Remaining);

        var resumed = rig.Engine.StartOrToggle().Snapshot;
        Assert.Equal(RunStatus.Running, resumed.Status);
        Assert.Equal(TimeSpan.FromMinutes(15), resumed.Remaining);
        Assert.Equal(rig.Time.GetUtcNow() + TimeSpan.FromMinutes(15), resumed.State.EndsAt);
        Assert.Null(resumed.State.Remaining);
    }

    [Fact]
    public void Sleeping_past_the_end_credits_one_phase_and_waits_idle_even_with_both_auto_starts()
    {
        var rig = new Rig(Both);
        rig.Engine.StartOrToggle();

        var result = rig.Run(TimeSpan.FromHours(3));

        Assert.Single(result.Completed);
        Assert.False(result.Completed[0].NextAutoStarted);
        Assert.Equal(Phase.ShortBreak, result.Snapshot.Phase);
        Assert.Equal(RunStatus.Idle, result.Snapshot.Status);
        Assert.Null(result.Snapshot.State.EndsAt);
        Assert.Equal(1, rig.Engine.GetToday().Pomodoros);
        Assert.Equal(0, rig.Engine.GetToday().ShortBreaks);
    }

    [Fact]
    public void Chaining_continues_while_ticking_within_the_window()
    {
        var rig = new Rig(Both);
        rig.Engine.StartOrToggle();
        var t0 = rig.Time.GetUtcNow();

        rig.Run(TimeSpan.FromMinutes(25) + TimeSpan.FromSeconds(30));
        Assert.Equal(Phase.ShortBreak, rig.Engine.Snapshot().Phase);
        Assert.Equal(t0 + TimeSpan.FromMinutes(30), rig.Engine.Snapshot().State.EndsAt);

        rig.Run(TimeSpan.FromMinutes(5));
        var s = rig.Engine.Snapshot();
        Assert.Equal(Phase.Focus, s.Phase);
        Assert.Equal(RunStatus.Running, s.Status);
        Assert.Equal(t0 + TimeSpan.FromMinutes(55), s.State.EndsAt);
        Assert.Equal(1, rig.Engine.GetToday().ShortBreaks);
    }

    [Fact]
    public void Operations_complete_an_overdue_phase_first_and_return_the_completion()
    {
        var rig = new Rig(Manual);
        rig.Engine.StartOrToggle();
        rig.Wait(TimeSpan.FromMinutes(26));

        var result = rig.Engine.StartOrToggle(); // would otherwise "pause" a phase that already ended

        Assert.Single(result.Completed);
        Assert.Equal(Phase.Focus, result.Completed[0].Finished);
        Assert.Equal(1, rig.Engine.GetToday().Pomodoros);
        Assert.Equal(Phase.ShortBreak, result.Snapshot.Phase);
        Assert.Equal(RunStatus.Running, result.Snapshot.Status); // the toggle then started the break
    }

    [Fact]
    public void Completion_is_credited_to_the_local_day_of_the_end_instant()
    {
        var rig = new Rig(Manual, new DateTime(2026, 10, 10, 23, 30, 0));
        rig.Engine.StartOrToggle(); // ends 23:55 local on the 10th

        rig.Wait(TimeSpan.FromMinutes(60)); // we only notice at 00:30 on the 11th
        rig.Engine.Advance();

        var days = rig.Engine.GetLastDays(2);
        Assert.Equal(new DateOnly(2026, 10, 11), days[0].Date);
        Assert.True(days[0].Stats.IsEmpty);
        Assert.Equal(new DateOnly(2026, 10, 10), days[1].Date);
        Assert.Equal(1, days[1].Stats.Pomodoros);
    }

    [Fact]
    public void Completion_across_midnight_is_credited_to_the_day_it_ended_on()
    {
        var rig = new Rig(Manual, new DateTime(2026, 10, 10, 23, 50, 0));
        rig.Engine.StartOrToggle(); // ends 00:15 on the 11th

        rig.Run(TimeSpan.FromMinutes(26));

        var days = rig.Engine.GetLastDays(2);
        Assert.Equal(1, days[0].Stats.Pomodoros);
        Assert.True(days[1].Stats.IsEmpty);
    }

    [Fact]
    public void Changing_options_mid_phase_keeps_the_end_and_credits_the_duration_it_started_with()
    {
        var rig = new Rig(Manual);
        rig.Engine.StartOrToggle();
        var ends = rig.Engine.Snapshot().State.EndsAt;

        var changed = rig.Engine.UpdateOptions(Manual with { FocusMinutes = 50, ShortBreakMinutes = 10 }).Snapshot;
        Assert.Equal(ends, changed.State.EndsAt);
        Assert.Equal(TimeSpan.FromMinutes(25), changed.State.PhaseDuration);

        rig.Run(TimeSpan.FromMinutes(25));
        Assert.Equal(25 * 60, rig.Engine.GetToday().FocusSeconds);

        // The next idle phase shows the new break length, and a fresh focus the new focus length.
        Assert.Equal(TimeSpan.FromMinutes(10), rig.Engine.Snapshot().Remaining);
        Assert.Equal(TimeSpan.FromMinutes(50), rig.Engine.Reset().Snapshot.Remaining);
    }

    [Fact]
    public void Shrinking_the_long_break_interval_below_the_cycle_count_leads_to_a_long_break()
    {
        var rig = new Rig(Manual);
        rig.Engine.StartOrToggle();
        rig.Run(TimeSpan.FromMinutes(25)); // 1 done, short break pending
        rig.Engine.Skip();

        rig.Engine.UpdateOptions(Manual with { LongBreakEvery = 2 });
        rig.Engine.StartOrToggle();
        rig.Run(TimeSpan.FromMinutes(25));

        Assert.Equal(Phase.LongBreak, rig.Engine.Snapshot().Phase);
    }

    [Fact]
    public void Reset_returns_to_an_idle_first_focus_and_clears_timing()
    {
        var rig = new Rig();
        rig.Engine.StartOrToggle();
        rig.Engine.SetNotificationScheduled(rig.Engine.Snapshot().State.EndsAt);

        var s = rig.Engine.Reset().Snapshot;

        Assert.Equal(Phase.Focus, s.Phase);
        Assert.Equal(RunStatus.Idle, s.Status);
        Assert.Equal(0, s.State.CompletedInCycle);
        Assert.Null(s.State.EndsAt);
        Assert.Null(s.State.Remaining);
        Assert.Null(s.State.PhaseDuration);
        Assert.Null(s.State.NotificationScheduledFor);
    }

    [Fact]
    public void Completion_reports_whether_a_toast_was_scheduled_for_that_exact_instant()
    {
        var scheduled = new Rig(Manual);
        scheduled.Engine.StartOrToggle();
        scheduled.Engine.SetNotificationScheduled(scheduled.Engine.Snapshot().State.EndsAt);
        Assert.True(scheduled.Run(TimeSpan.FromMinutes(25)).Completed[0].ToastScheduled);

        var unscheduled = new Rig(Manual);
        unscheduled.Engine.StartOrToggle();
        Assert.False(unscheduled.Run(TimeSpan.FromMinutes(25)).Completed[0].ToastScheduled);
    }

    [Fact]
    public void A_stale_notification_stamp_is_ignored_and_pause_clears_it()
    {
        var rig = new Rig();
        rig.Engine.StartOrToggle();
        var ends = rig.Engine.Snapshot().State.EndsAt;

        rig.Engine.SetNotificationScheduled(ends!.Value + TimeSpan.FromMinutes(1));
        Assert.Null(rig.Engine.Snapshot().State.NotificationScheduledFor);

        rig.Engine.SetNotificationScheduled(ends);
        Assert.Equal(ends, rig.State.Saved!.NotificationScheduledFor);

        rig.Engine.StartOrToggle(); // pause
        Assert.Null(rig.Engine.Snapshot().State.NotificationScheduledFor);
    }

    [Fact]
    public void Every_mutation_saves_state_and_history_is_saved_on_change()
    {
        var rig = new Rig(Manual);
        rig.Engine.StartOrToggle();
        Assert.Equal(RunStatus.Running, rig.State.Saved!.Status);
        Assert.Empty(rig.History.Saved);

        rig.Run(TimeSpan.FromMinutes(25));
        Assert.Single(rig.History.Saved);
    }

    [Fact]
    public void Inconsistent_loaded_state_is_repaired()
    {
        var store = new MemoryStateStore
        {
            Saved = new TimerState { Phase = Phase.ShortBreak, Status = RunStatus.Running, EndsAt = null, CompletedInCycle = -3 },
        };
        var engine = new PomodoroEngine(new FakeTimeProvider(Start), new PomodoroOptions(), store, new MemoryHistoryStore());

        var s = engine.Snapshot();
        Assert.Equal(RunStatus.Idle, s.Status);
        Assert.Equal(Phase.ShortBreak, s.Phase);
        Assert.Equal(0, s.State.CompletedInCycle);
    }

    [Fact]
    public void A_running_phase_that_ended_while_the_process_was_gone_is_completed_at_startup()
    {
        var time = new FakeTimeProvider(Start);
        var state = new MemoryStateStore();
        var history = new MemoryHistoryStore();
        var first = new PomodoroEngine(time, Manual, state, history);
        first.StartOrToggle();

        time.LocalNow += TimeSpan.FromHours(5);
        var second = new PomodoroEngine(time, Manual, state, history);
        var result = second.Advance();

        Assert.Single(result.Completed);
        Assert.Equal(1, second.GetToday().Pomodoros);
        Assert.Equal(Phase.ShortBreak, result.Snapshot.Phase);
    }

    [Fact]
    public void Last_days_includes_zero_days_and_lists_today_first()
    {
        var rig = new Rig(Manual);
        rig.Engine.StartOrToggle();
        rig.Run(TimeSpan.FromMinutes(25));

        var days = rig.Engine.GetLastDays(7);

        Assert.Equal(7, days.Count);
        Assert.Equal(rig.Time.Today, days[0].Date);
        Assert.Equal(1, days[0].Stats.Pomodoros);
        for (var i = 1; i < 7; i++)
        {
            Assert.Equal(rig.Time.Today.AddDays(-i), days[i].Date);
            Assert.True(days[i].Stats.IsEmpty);
        }
    }

    [Fact]
    public void History_older_than_90_days_is_pruned_on_save()
    {
        var rig = new Rig(Manual);
        var today = rig.Time.Today;
        rig.History.Saved[today.AddDays(-91)] = new DayStats(1, 1500, 0, 0);
        rig.History.Saved[today.AddDays(-90)] = new DayStats(2, 3000, 0, 0);
        var engine = new PomodoroEngine(rig.Time, Manual, rig.State, rig.History);

        engine.StartOrToggle();
        rig.Wait(TimeSpan.FromMinutes(25));
        engine.Advance();

        Assert.DoesNotContain(today.AddDays(-91), rig.History.Saved.Keys);
        Assert.Contains(today.AddDays(-90), rig.History.Saved.Keys);
        Assert.Contains(today, rig.History.Saved.Keys);
    }
}
