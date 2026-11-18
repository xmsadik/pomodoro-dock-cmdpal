using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PomodoroDock.Core;

namespace PomodoroDock;

/// <summary>
/// Segoe Fluent Icons glyphs (vector, theme-aware, like the built-in Dock bands) and the phase accent colors.
/// </summary>
internal static class Glyphs
{
    public const string Focus = "";       // target
    public const string ShortBreak = "";  // cup
    public const string LongBreak = "";   // sun
    public const string Play = "";
    public const string Pause = "";
    public const string Skip = "";
    public const string Reset = "";
    public const string Today = "";       // report
    public const string Week = "";        // area chart
    public const string Settings = "";

    public static string ForPhase(Phase phase) => phase switch
    {
        Phase.ShortBreak => ShortBreak,
        Phase.LongBreak => LongBreak,
        _ => Focus,
    };

    /// <summary>Accent of a phase: tomato for focus, green for a short break, blue for a long break.</summary>
    public static OptionalColor PhaseColor(Phase phase) => phase switch
    {
        Phase.ShortBreak => ColorHelpers.FromRgb(0x2E, 0xA0, 0x43),
        Phase.LongBreak => ColorHelpers.FromRgb(0x3E, 0x63, 0xDD),
        _ => ColorHelpers.FromRgb(0xE5, 0x48, 0x4D),
    };

    public static OptionalColor White => ColorHelpers.FromRgb(0xFF, 0xFF, 0xFF);
}
