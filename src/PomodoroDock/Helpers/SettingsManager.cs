using System;
using System.Globalization;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PomodoroDock.Core;

namespace PomodoroDock;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private static readonly string _namespace = "PomodoroDock";

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private readonly ChoiceSetSetting _focusMinutes = new(
        Namespaced(nameof(FocusMinutes)),
        "Focus",
        "Length of a focus session",
        Minutes(15, 20, 25, 30, 35, 40, 45, 50, 55, 60));

    private readonly ChoiceSetSetting _shortBreakMinutes = new(
        Namespaced(nameof(ShortBreakMinutes)),
        "Short break",
        "Length of a short break",
        Minutes(3, 5, 10, 15));

    private readonly ChoiceSetSetting _longBreakMinutes = new(
        Namespaced(nameof(LongBreakMinutes)),
        "Long break",
        "Length of a long break",
        Minutes(10, 15, 20, 25, 30));

    private readonly ChoiceSetSetting _longBreakEvery = new(
        Namespaced(nameof(LongBreakEvery)),
        "Long break every",
        "Focus sessions before a long break",
        [
            new ChoiceSetSetting.Choice("2 pomodoros", "2"),
            new ChoiceSetSetting.Choice("3 pomodoros", "3"),
            new ChoiceSetSetting.Choice("4 pomodoros", "4"),
            new ChoiceSetSetting.Choice("5 pomodoros", "5"),
            new ChoiceSetSetting.Choice("6 pomodoros", "6"),
        ]);

    private readonly ToggleSetting _autoStartBreaks = new(
        Namespaced(nameof(AutoStartBreaks)),
        "Auto-start breaks",
        "Start the break as soon as a focus session ends",
        true);

    private readonly ToggleSetting _autoStartFocus = new(
        Namespaced(nameof(AutoStartFocus)),
        "Auto-start focus",
        "Start the next focus session as soon as a break ends",
        false);

    private readonly ToggleSetting _notifications = new(
        Namespaced(nameof(Notifications)),
        "Notifications",
        "Show a Windows notification when a phase ends",
        true);

    public int FocusMinutes => Parse(_focusMinutes.Value, 25);

    public int ShortBreakMinutes => Parse(_shortBreakMinutes.Value, 5);

    public int LongBreakMinutes => Parse(_longBreakMinutes.Value, 15);

    public int LongBreakEvery => Parse(_longBreakEvery.Value, 4);

    public bool AutoStartBreaks => _autoStartBreaks.Value;

    public bool AutoStartFocus => _autoStartFocus.Value;

    public bool Notifications => _notifications.Value;

    /// <summary>The current settings as engine options.</summary>
    public PomodoroOptions Options => new(
        FocusMinutes, ShortBreakMinutes, LongBreakMinutes, LongBreakEvery, AutoStartBreaks, AutoStartFocus);

    internal static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("PomodoroDock");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        // A choice list defaults to its first entry; these are the intended defaults. Set before
        // LoadSettings so a saved value still wins.
        _focusMinutes.Value = "25";
        _shortBreakMinutes.Value = "5";
        _longBreakMinutes.Value = "15";
        _longBreakEvery.Value = "4";

        Settings.Add(_focusMinutes);
        Settings.Add(_shortBreakMinutes);
        Settings.Add(_longBreakMinutes);
        Settings.Add(_longBreakEvery);
        Settings.Add(_autoStartBreaks);
        Settings.Add(_autoStartFocus);
        Settings.Add(_notifications);

        // Load settings from file upon initialization
        LoadSettings();

        Settings.SettingsChanged += (_, _) =>
        {
            try
            {
                SaveSettings();
            }
            catch (Exception)
            {
                // Persisting failed (locked/readonly settings.json); in-memory values are still correct.
            }
        };
    }

    private static System.Collections.Generic.List<ChoiceSetSetting.Choice> Minutes(params int[] values)
    {
        var choices = new System.Collections.Generic.List<ChoiceSetSetting.Choice>(values.Length);
        foreach (var v in values)
        {
            var text = v.ToString(CultureInfo.InvariantCulture);
            choices.Add(new ChoiceSetSetting.Choice(text + " min", text));
        }

        return choices;
    }

    private static int Parse(string? raw, int fallback) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
}
