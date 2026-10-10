using System.Globalization;

namespace PomodoroDock.Core;

/// <summary>Display text shared by the Dock band, the flyout and the toasts. Pure, so it is unit-tested.</summary>
public static class Formatting
{
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

    /// <summary>"3 pomodoros · 1h 15m focus".</summary>
    public static string TodayTitle(DayStats d) =>
        string.Create(CultureInfo.InvariantCulture, $"🍅 {Count(d.Pomodoros, "pomodoro", "pomodoros")} · {FormatDuration(d.FocusSeconds)} focus");

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

    /// <summary>"Last 7 days · 23 🍅 · 9h 35m".</summary>
    public static string WeekTitle(int days, DayStats total) =>
        string.Create(CultureInfo.InvariantCulture, $"Last {days} days · {total.Pomodoros} 🍅 · {FormatDuration(total.FocusSeconds)}");

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
