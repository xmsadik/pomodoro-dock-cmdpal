using Xunit;
using PomodoroDock.Core;

namespace PomodoroDock.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomodoroDock.Tests", Guid.NewGuid().ToString("N"));

    public StoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private PomodoroEngine Engine(FakeTimeProvider time, PomodoroOptions? options = null) => new(
        time,
        options ?? new PomodoroOptions(AutoStartBreaks: false),
        new FileStateStore(Path.Combine(_dir, "state.json")),
        new FileHistoryStore(Path.Combine(_dir, "history.json")));

    [Fact]
    public void State_and_history_round_trip_through_the_files()
    {
        var time = new FakeTimeProvider(new DateTime(2026, 10, 10, 10, 0, 0));
        var first = Engine(time);
        first.StartOrToggle();
        time.LocalNow += TimeSpan.FromMinutes(25);
        first.Advance();
        first.StartOrToggle(); // running short break
        time.LocalNow += TimeSpan.FromMinutes(2);
        first.StartOrToggle(); // paused
        var expected = first.Snapshot();

        var second = Engine(time);
        var actual = second.Snapshot();

        Assert.Equal(expected.State, actual.State);
        Assert.Equal(RunStatus.Paused, actual.Status);
        Assert.Equal(TimeSpan.FromMinutes(3), actual.Remaining);
        Assert.Equal(first.GetToday(), second.GetToday());
        Assert.Equal(1, second.GetToday().Pomodoros);
    }

    [Fact]
    public void History_file_uses_plain_date_strings()
    {
        var time = new FakeTimeProvider(new DateTime(2026, 10, 10, 10, 0, 0));
        var engine = Engine(time);
        engine.StartOrToggle();
        time.LocalNow += TimeSpan.FromMinutes(25);
        engine.Advance();

        var json = File.ReadAllText(Path.Combine(_dir, "history.json"));

        Assert.Contains("\"2026-10-10\"", json);
        Assert.False(File.Exists(Path.Combine(_dir, "history.json.tmp")));
    }

    [Fact]
    public void Corrupt_files_fall_back_to_defaults_without_throwing()
    {
        File.WriteAllText(Path.Combine(_dir, "state.json"), "{ not json");
        File.WriteAllText(Path.Combine(_dir, "history.json"), "[ 1, 2");

        var engine = Engine(new FakeTimeProvider(new DateTime(2026, 10, 10, 10, 0, 0)));
        var s = engine.Snapshot();

        Assert.Equal(Phase.Focus, s.Phase);
        Assert.Equal(RunStatus.Idle, s.Status);
        Assert.True(engine.GetToday().IsEmpty);
    }

    [Fact]
    public void Unknown_enum_text_and_bad_dates_are_tolerated()
    {
        File.WriteAllText(Path.Combine(_dir, "state.json"), "{\"Phase\":\"Nope\"}");
        File.WriteAllText(
            Path.Combine(_dir, "history.json"),
            "[{\"Date\":\"garbage\",\"Pomodoros\":3},{\"Date\":\"2026-10-10\",\"Pomodoros\":2,\"FocusSeconds\":3000}]");

        var engine = Engine(new FakeTimeProvider(new DateTime(2026, 10, 10, 10, 0, 0)));

        Assert.Equal(Phase.Focus, engine.Snapshot().Phase);
        Assert.Equal(new DayStats(2, 3000, 0, 0), engine.GetToday());
    }

    [Fact]
    public void Missing_files_mean_defaults()
    {
        Assert.Null(new FileStateStore(Path.Combine(_dir, "none.json")).Load());
        Assert.Empty(new FileHistoryStore(Path.Combine(_dir, "none2.json")).Load());
    }
}

public class FormattingTests
{
    [Theory]
    [InlineData(1500.0, "25:00")]
    [InlineData(1499.2, "25:00")]
    [InlineData(1499.0, "24:59")]
    [InlineData(0.4, "00:01")]
    [InlineData(0.0, "00:00")]
    [InlineData(-5.0, "00:00")]
    [InlineData(3600.0, "60:00")]
    [InlineData(65.0, "01:05")]
    public void FormatClock_rounds_seconds_up(double seconds, string expected) =>
        Assert.Equal(expected, Formatting.FormatClock(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0L, "0m")]
    [InlineData(59L, "0m")]
    [InlineData(2700L, "45m")]
    [InlineData(3540L, "59m")]
    [InlineData(4500L, "1h 15m")]
    [InlineData(7200L, "2h 0m")]
    public void FormatDuration_uses_whole_minutes(long seconds, string expected) =>
        Assert.Equal(expected, Formatting.FormatDuration(seconds));

    [Fact]
    public void Report_titles()
    {
        Assert.Equal("🍅 3 pomodoros · 1h 15m focus", Formatting.TodayTitle(new DayStats(3, 4500, 2, 1)));
        Assert.Equal("🍅 1 pomodoro · 25m focus", Formatting.TodayTitle(new DayStats(1, 1500, 0, 0)));
        Assert.Equal("2 short breaks · 1 long break", Formatting.BreaksLine(new DayStats(3, 4500, 2, 1)));
        Assert.Equal("—", Formatting.BreaksLine(DayStats.Empty));
        Assert.Equal("Thu 9 Oct · 6 🍅 · 2h 30m", Formatting.DayTitle(new DateOnly(2025, 10, 9), new DayStats(6, 9000, 0, 0)));
        Assert.Equal("Wed 8 Oct · —", Formatting.DayTitle(new DateOnly(2025, 10, 8), DayStats.Empty));
        Assert.Equal("Last 7 days · 23 🍅 · 9h 35m", Formatting.WeekTitle(7, new DayStats(23, 34500, 0, 0)));
    }

    [Fact]
    public void Subtitle_and_primary_action_follow_phase_and_status()
    {
        var o = new PomodoroOptions();
        EngineSnapshot Snap(TimerState s) => new(s, o, TimeSpan.FromMinutes(1), DateTimeOffset.UnixEpoch, new DateOnly(2026, 10, 10), DayStats.Empty);

        var idle = Snap(new TimerState { CompletedInCycle = 1 });
        Assert.Equal("Ready · Focus · 2/4", Formatting.Subtitle(idle));
        Assert.Equal("Start focus", Formatting.PrimaryActionTitle(idle));

        var running = Snap(new TimerState { Status = RunStatus.Running, CompletedInCycle = 9 });
        Assert.Equal("Focus · 4/4", Formatting.Subtitle(running));
        Assert.Equal("Pause", Formatting.PrimaryActionTitle(running));

        var paused = Snap(new TimerState { Phase = Phase.ShortBreak, Status = RunStatus.Paused });
        Assert.Equal("Paused · Short break", Formatting.Subtitle(paused));
        Assert.Equal("Resume", Formatting.PrimaryActionTitle(paused));

        var longIdle = Snap(new TimerState { Phase = Phase.LongBreak });
        Assert.Equal("Ready · Long break", Formatting.Subtitle(longIdle));
        Assert.Equal("Start long break", Formatting.PrimaryActionTitle(longIdle));
    }

    [Fact]
    public void Notice_texts()
    {
        var o = new PomodoroOptions();
        Assert.Equal(("Focus complete 🍅", "Short break (5 min) started."), Formatting.Notice(Phase.Focus, Phase.ShortBreak, true, o));
        Assert.Equal(("Focus complete 🍅", "Time for a long break — start it from the Dock."), Formatting.Notice(Phase.Focus, Phase.LongBreak, false, o));
        Assert.Equal(("Break over", "Next focus session started."), Formatting.Notice(Phase.ShortBreak, Phase.Focus, true, o));
        Assert.Equal(("Break over", "Ready to focus? Start from the Dock."), Formatting.Notice(Phase.LongBreak, Phase.Focus, false, o));
        Assert.Equal(("Focus complete 🍅", "Time for a break."), Formatting.ScheduledNotice(Phase.Focus));
        Assert.Equal(("Break over", "Time to focus."), Formatting.ScheduledNotice(Phase.ShortBreak));
    }

    [Fact]
    public void PredictNext_matches_the_engine_rules()
    {
        var o = new PomodoroOptions();
        Assert.Equal((Phase.ShortBreak, true), PomodoroEngine.PredictNext(Phase.Focus, 2, o));
        Assert.Equal((Phase.LongBreak, true), PomodoroEngine.PredictNext(Phase.Focus, 3, o));
        Assert.Equal((Phase.Focus, false), PomodoroEngine.PredictNext(Phase.ShortBreak, 1, o));
    }

    [Fact]
    public void Tick_delay_targets_the_next_displayed_second()
    {
        EngineSnapshot Snap(RunStatus status, double seconds) => new(
            new TimerState { Status = status }, new PomodoroOptions(), TimeSpan.FromSeconds(seconds),
            DateTimeOffset.UnixEpoch, new DateOnly(2026, 10, 10), DayStats.Empty);

        Assert.Equal(TickSchedule.IdleInterval, TickSchedule.NextDelay(Snap(RunStatus.Paused, 100)));
        Assert.Equal(TickSchedule.IdleInterval, TickSchedule.NextDelay(Snap(RunStatus.Idle, 100)));
        Assert.Equal(TimeSpan.FromMilliseconds(50), TickSchedule.NextDelay(Snap(RunStatus.Running, 0)));
        Assert.Equal(TimeSpan.FromSeconds(1), TickSchedule.NextDelay(Snap(RunStatus.Running, 100))); // whole second: full second
        Assert.Equal(TimeSpan.FromMilliseconds(520), TickSchedule.NextDelay(Snap(RunStatus.Running, 100.5)));
        Assert.Equal(TimeSpan.FromMilliseconds(50), TickSchedule.NextDelay(Snap(RunStatus.Running, 100.01)));
    }
}
