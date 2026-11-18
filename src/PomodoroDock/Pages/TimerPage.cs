using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PomodoroDock.Core;

namespace PomodoroDock;

/// <summary>Runs one engine operation and does the follow-up (notify, reschedule, re-render).</summary>
internal delegate void EngineAction(Func<PomodoroEngine, EngineResult> operation);

/// <summary>An engine operation exposed as a command. Never lets an exception escape.</summary>
internal sealed partial class TimerCommand : InvokableCommand
{
    private readonly EngineAction _run;
    private readonly Func<PomodoroEngine, EngineResult> _operation;

    public TimerCommand(string id, string name, EngineAction run, Func<PomodoroEngine, EngineResult> operation)
    {
        Id = id;
        Name = name;
        _run = run;
        _operation = operation;
    }

    public override ICommandResult Invoke()
    {
        try
        {
            _run(_operation);
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"{Id} failed: {ex}");
        }

        return CommandResult.KeepOpen();
    }
}

/// <summary>
/// The flyout opened from the Dock band. Top to bottom: a status row (phase, progress bar, status and cycle
/// tags), the controls, today's totals, a last-7-days summary with a sparkline, and Settings. All icons are
/// Segoe Fluent glyphs. The items are created once and their texts are mutated; <see cref="Refresh"/> is driven
/// by the provider's signature check (single refresh path), so a per-second tick never reaches this page: the
/// progress bar moves in tenths.
/// </summary>
internal sealed partial class TimerPage : ListPage
{
    private readonly PomodoroEngine _engine;
    private readonly TimerCommand _toggle;
    private readonly ListItem _status;
    private readonly ListItem _primary;
    private readonly ListItem _skip;
    private readonly ListItem _reset;
    private readonly ListItem _today;
    private readonly ListItem _week;
    private readonly IListItem[] _items;
    private string _statusTagsKey = "";

    public TimerPage(PomodoroEngine engine, SettingsManager settings, EngineAction run)
    {
        _engine = engine;
        Id = "PomodoroDock.timer";
        Name = "Open";
        Title = "Pomodoro";
        Icon = new IconInfo(Glyphs.Focus);

        _status = new ListItem(new NoOpCommand()) { Section = "Now" };
        _toggle = new TimerCommand("PomodoroDock.toggle", "Start", run, e => e.StartOrToggle());
        _primary = new ListItem(_toggle) { Section = "Now", Icon = new IconInfo(Glyphs.Play) };
        _skip = new ListItem(new TimerCommand("PomodoroDock.skip", "Skip", run, e => e.Skip()))
        {
            Section = "Now",
            Icon = new IconInfo(Glyphs.Skip),
        };
        _reset = new ListItem(new TimerCommand("PomodoroDock.reset", "Reset cycle", run, e => e.Reset()))
        {
            Title = "Reset cycle",
            Subtitle = "Back to the first focus session",
            Section = "Now",
            Icon = new IconInfo(Glyphs.Reset),
        };
        _today = new ListItem(new NoOpCommand()) { Section = "Today", Icon = new IconInfo(Glyphs.Today) };
        _week = new ListItem(new NoOpCommand()) { Section = "Last 7 days", Icon = new IconInfo(Glyphs.Week) };
        var settingsItem = new ListItem(settings.Settings.SettingsPage)
        {
            Title = "Settings",
            Subtitle = "Durations, auto-start, notifications",
            Section = "Settings",
            Icon = new IconInfo(Glyphs.Settings),
        };

        _items = [_status, _primary, _skip, _reset, _today, _week, settingsItem];
        Update();
    }

    /// <summary>Re-reads the engine, mutates changed texts and tells the host to re-fetch.</summary>
    public void Refresh()
    {
        try
        {
            Update();
            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"TimerPage refresh failed: {ex}");
        }
    }

    public override IListItem[] GetItems() => _items;

    /// <summary>Everything this page renders, as one string; the provider refreshes the page only when it changes.</summary>
    internal static string Signature(EngineSnapshot s, IReadOnlyList<DayEntry> days)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(s.Phase).Append('|').Append(s.Status).Append('|').Append(Formatting.CycleDots(s)).Append('|')
            .Append(Formatting.ProgressStep(s)).Append('|').Append(Formatting.PrimaryHint(s, TimeZoneInfo.Local)).Append('|')
            .Append(s.SkipTarget).Append('|').Append(s.Today.DayNumber).Append('|').Append(s.TodayStats);
        foreach (var d in days)
        {
            sb.Append('#').Append(d.Date.DayNumber).Append(d.Stats);
        }

        return sb.ToString();
    }

    private void Update()
    {
        var s = _engine.Snapshot();
        var days = _engine.GetLastDays(7);
        var total = DayStats.Empty;
        var oldestFirst = new DayStats[days.Count];
        for (var i = 0; i < days.Count; i++)
        {
            total += days[i].Stats;
            oldestFirst[days.Count - 1 - i] = days[i].Stats;
        }

        // Status row: what is on, how far along, and where in the cycle.
        SetText(_status, Formatting.PhaseTitle(s.Phase), Formatting.ProgressLine(s));
        SetIcon(_status, Glyphs.ForPhase(s.Phase));
        SetStatusTags(s);

        var primary = Formatting.PrimaryActionTitle(s);
        SetCommandName(_toggle, primary);
        SetText(_primary, primary, Formatting.PrimaryHint(s, TimeZoneInfo.Local));
        SetIcon(_primary, s.Status == RunStatus.Running ? Glyphs.Pause : Glyphs.Play);
        SetText(_skip, "Skip to " + Formatting.PhaseName(s.SkipTarget), "Leave this phase without counting it");
        SetText(_today, Formatting.TodayTitle(s.TodayStats), Formatting.BreaksLine(s.TodayStats));
        SetText(_week, Formatting.WeekTitle(total), Formatting.Sparkline(oldestFirst) + "   " + Formatting.BreaksLine(total));
    }

    /// <summary>Two tags on the status row: the run status (filled with the phase color while running) and the cycle dots.</summary>
    private void SetStatusTags(EngineSnapshot s)
    {
        var dots = Formatting.CycleDots(s);
        var key = $"{s.Phase}|{s.Status}|{dots}";
        if (key == _statusTagsKey)
        {
            return;
        }

        var accent = Glyphs.PhaseColor(s.Phase);
        var statusTag = s.Status switch
        {
            RunStatus.Running => new Tag(Formatting.StatusName(s.Status))
            {
                Background = accent,
                Foreground = Glyphs.White,
            },
            RunStatus.Paused => new Tag(Formatting.StatusName(s.Status)) { Foreground = accent },
            _ => new Tag(Formatting.StatusName(s.Status)),
        };

        try
        {
            _status.Tags = [statusTag, new Tag(dots) { ToolTip = "Focus sessions in this cycle" }];
            _statusTagsKey = key;
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"TimerPage tags failed: {ex}");
        }
    }

    // Each host-visible set is guarded and only done when the value changed (host bug #50483).
    private static void SetText(ListItem item, string title, string subtitle)
    {
        if (item.Title != title)
        {
            try
            {
                item.Title = title;
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"TimerPage title failed: {ex}");
            }
        }

        if (item.Subtitle != subtitle)
        {
            try
            {
                item.Subtitle = subtitle;
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"TimerPage subtitle failed: {ex}");
            }
        }
    }

    private static void SetIcon(ListItem item, string glyph)
    {
        if (item.Icon?.Dark?.Icon == glyph)
        {
            return;
        }

        try
        {
            item.Icon = new IconInfo(glyph);
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"TimerPage icon failed: {ex}");
        }
    }

    private static void SetCommandName(TimerCommand command, string name)
    {
        if (command.Name == name)
        {
            return;
        }

        try
        {
            command.Name = name;
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"TimerPage command name failed: {ex}");
        }
    }
}
