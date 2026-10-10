using System;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PomodoroDock.Core;

namespace PomodoroDock;

/// <summary>
/// The single Dock ListItem for the extension's lifetime (RaiseItemsChanged does not refresh a
/// WrappedDockItem), so only its properties are mutated, and only when the value changed. Once a second
/// that is the Title alone; Icon and Subtitle change only on a phase or status change.
/// </summary>
internal sealed partial class PomodoroBand : ListItem
{
    private string? _icon;
    private string? _title;
    private string? _subtitle;

    public PomodoroBand(TimerPage page)
        : base(page)
    {
    }

    public static string IconFor(EngineSnapshot s) => s.Status == RunStatus.Paused
        ? "⏸️"
        : s.Phase switch
        {
            Phase.ShortBreak => "☕",
            Phase.LongBreak => "🌴",
            _ => "🍅",
        };

    public void Render(EngineSnapshot snapshot)
    {
        var icon = IconFor(snapshot);
        var title = Formatting.FormatClock(snapshot.Remaining);
        var subtitle = Formatting.Subtitle(snapshot);

        // No lock: these setters call into the host (see PomodoroDockCommandsProvider.Refresh).
        if (icon != _icon)
        {
            try
            {
                Icon = new IconInfo(icon);
                _icon = icon;
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"PomodoroBand icon failed: {ex}");
            }
        }

        if (title != _title)
        {
            try
            {
                Title = title;
                _title = title;
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"PomodoroBand title failed: {ex}");
            }
        }

        if (subtitle != _subtitle)
        {
            try
            {
                Subtitle = subtitle;
                _subtitle = subtitle;
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"PomodoroBand subtitle failed: {ex}");
            }
        }
    }
}
