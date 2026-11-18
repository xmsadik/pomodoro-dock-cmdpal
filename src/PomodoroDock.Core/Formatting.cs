using System.Globalization;

namespace PomodoroDock.Core;

/// <summary>Display text shared by the Dock band, the flyout and the toasts. Pure, so it is unit-tested.</summary>
public static class Formatting
{
    private const int ProgressCells = 10;

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>"mm:ss" with the seconds rounded up, so the clock reaches 00:00 exactly when the phase ends.</summary>
    public static string FormatClock(TimeSpan remaining)
    {
        var total = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }

    /// <summary>"0m", "45m", "1h 15m" (whole minutes, rounded down).</summary>
    public static string FormatDuration(long seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        return minutes < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}h {minutes % 60}m");
    }

    /// <summary>Band subtitle, e.g. "Focus · 2/4", "Paused · Short break", "Ready · Long break".</summary>
    public static string Subtitle(EngineSnapshot s)
    {
        var label = s.Phase switch
        {
            Phase.Focus => string.Create(
                CultureInfo.InvariantCulture,
                $"Focus · {Math.Min(s.State.CompletedInCycle + 1, s.Options.LongBreakEvery)}/{s.Options.LongBreakEvery}"),
            Phase.ShortBreak => "Short break",
            _ => "Long break",
        };
        return s.Status switch
        {
            RunStatus.Paused => "Paused · " + label,
            RunStatus.Idle => "Ready · " + label,
            _ => label,
        };
    }

    public static string PhaseName(Phase phase) => phase switch
    {
        Phase.Focus => "focus",
        Phase.ShortBreak => "short break",
        _ => "long break",
    };

    /// <summary>Title of the flyout's primary action for the current state.</summary>
    public static string PrimaryActionTitle(EngineSnapshot s) => s.Status switch
    {
        RunStatus.Running => "Pause",
        RunStatus.Paused => "Resume",
        _ => "Start " + PhaseName(s.Phase),
    };

    /// <summary>"3 pomodoros · 1h 15m focus" (the flyout row carries a vector icon, so no emoji here).</summary>
    public static string TodayTitle(DayStats d) =>
        string.Create(CultureInfo.InvariantCulture, $"{Count(d.Pomodoros, "pomodoro", "pomodoros")} · {FormatDuration(d.FocusSeconds)} focus");

    /// <summary>"2 short breaks · 1 long break", or an em dash when there were none.</summary>
    public static string BreaksLine(DayStats d) =>
        d.ShortBreaks == 0 && d.LongBreaks == 0
            ? "—"
            : Count(d.ShortBreaks, "short break", "short breaks") + " · " + Count(d.LongBreaks, "long break", "long breaks");

    /// <summary>"Thu 9 Oct · 6 🍅 · 2h 30m"; a day with no activity shows an em dash.</summary>
    public static string DayTitle(DateOnly date, DayStats d)
    {
        var day = date.ToString("ddd d MMM", Culture);
        return d.IsEmpty
            ? day + " · —"
            : string.Create(CultureInfo.InvariantCulture, $"{day} · {d.Pomodoros} 🍅 · {FormatDuration(d.FocusSeconds)}");
    }

    /// <summary>"23 pomodoros · 9h 35m focus"; the "Last 7 days" section header names the period.</summary>
    public static string WeekTitle(DayStats total) => TodayTitle(total);

    /// <summary>Display name of a phase with a capital letter: "Focus", "Short break", "Long break".</summary>
    public static string PhaseTitle(Phase phase) => Capitalize(PhaseName(phase));

    /// <summary>"Running", "Paused" or "Ready".</summary>
    public static string StatusName(RunStatus status) => status switch
    {
        RunStatus.Running => "Running",
        RunStatus.Paused => "Paused",
        _ => "Ready",
    };

    /// <summary>Share of the current phase already done, 0..1. Uses the length the phase started with.</summary>
    public static double Progress(EngineSnapshot s)
    {
        if (s.Status == RunStatus.Idle)
        {
            return 0;
        }

        var total = s.State.PhaseDuration ?? s.Options.DurationOf(s.Phase);
        return total <= TimeSpan.Zero ? 0 : Math.Clamp(1 - (s.Remaining / total), 0, 1);
    }

    /// <summary>
    /// Progress in whole tenths (0..10). The flyout repaints only when this changes, never every second
    /// (each host-visible set is costly, see host bug #50483).
    /// </summary>
    public static int ProgressStep(EngineSnapshot s) => (int)Math.Floor(Progress(s) * ProgressCells);

    /// <summary>"▰▰▰▰▱▱▱▱▱▱  40%" for a running or paused phase; an empty bar when idle.</summary>
    public static string ProgressLine(EngineSnapshot s)
    {
        var step = ProgressStep(s);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{new string('▰', step)}{new string('▱', ProgressCells - step)}  {step * 10}%");
    }

    /// <summary>
    /// The focus sessions of the cycle as dots: done "●", the one under way "◉" (focus only), still to go "○".
    /// E.g. "●●◉○" during the third focus of four.
    /// </summary>
    public static string CycleDots(EngineSnapshot s)
    {
        var every = Math.Max(1, s.Options.LongBreakEvery);
        var done = Math.Clamp(s.State.CompletedInCycle, 0, every);
        var current = s.Phase == Phase.Focus && done < every ? 1 : 0;
        return new string('●', done) + new string('◉', current) + new string('○', every - done - current);
    }

    /// <summary>Hint under the primary action: "Ends at 14:35", "12:30 left", or "25 min".</summary>
    public static string PrimaryHint(EngineSnapshot s, TimeZoneInfo zone) => s.Status switch
    {
        RunStatus.Running when s.State.EndsAt is { } end =>
            "Ends at " + TimeZoneInfo.ConvertTime(end, zone).ToString("HH:mm", CultureInfo.InvariantCulture),
        RunStatus.Paused => FormatClock(s.Remaining) + " left",
        _ => string.Create(CultureInfo.InvariantCulture, $"{s.Options.DurationOf(s.Phase).TotalMinutes:0} min"),
    };

    /// <summary>
    /// Focus time per day as a bar sparkline, oldest day first, e.g. "▁▃▅█▂▁▄". A day with no focus is "▁";
    /// any focus shows at least "▂" so it never looks like an empty day.
    /// </summary>
    public static string Sparkline(IReadOnlyList<DayStats> oldestFirst)
    {
        const string Bars = "▁▂▃▄▅▆▇█";
        long max = 0;
        foreach (var d in oldestFirst)
        {
            max = Math.Max(max, d.FocusSeconds);
        }

        var chars = new char[oldestFirst.Count];
        for (var i = 0; i < chars.Length; i++)
        {
            var v = oldestFirst[i].FocusSeconds;
            chars[i] = v <= 0 || max <= 0 ? Bars[0] : Bars[1 + (int)Math.Round((double)v / max * (Bars.Length - 2))];
        }

        return new string(chars);
    }

    /// <summary>Toast text for a phase that finished and the phase that follows it.</summary>
    public static (string Title, string Body) Notice(Phase finished, Phase next, bool nextAutoStarted, PomodoroOptions o)
    {
        if (finished == Phase.Focus)
        {
            var name = PhaseName(next);
            var body = nextAutoStarted
                ? string.Create(CultureInfo.InvariantCulture, $"{Capitalize(name)} ({o.DurationOf(next).TotalMinutes:0} min) started.")
                : $"Time for a {name} — start it from the Dock.";
            return ("Focus complete 🍅", body);
        }

        return ("Break over", nextAutoStarted ? "Next focus session started." : "Ready to focus? Start from the Dock.");
    }

    /// <summary>
    /// Toast text scheduled ahead of time. Settings may change or the PC may sleep before it fires, so it
    /// promises nothing about what follows.
    /// </summary>
    public static (string Title, string Body) ScheduledNotice(Phase finished) =>
        finished == Phase.Focus ? ("Focus complete 🍅", "Time for a break.") : ("Break over", "Time to focus.");

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private static string Count(int n, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{n} {(n == 1 ? one : many)}");
}
