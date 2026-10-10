using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PomodoroDock.Core;

/// <summary>Persists the timer state.</summary>
public interface IStateStore
{
    /// <summary>The stored state, or null when missing or unreadable.</summary>
    TimerState? Load();

    void Save(TimerState state);
}

/// <summary>Persists the per-day history.</summary>
public interface IHistoryStore
{
    /// <summary>The stored history; empty when missing or unreadable.</summary>
    Dictionary<DateOnly, DayStats> Load();

    void Save(IReadOnlyDictionary<DateOnly, DayStats> history);
}

/// <summary>One history row on disk; the date is a plain "yyyy-MM-dd" string.</summary>
public sealed class DayStatsDto
{
    public string Date { get; set; } = string.Empty;

    public int Pomodoros { get; set; }

    public long FocusSeconds { get; set; }

    public int ShortBreaks { get; set; }

    public int LongBreaks { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TimerState))]
[JsonSerializable(typeof(List<DayStatsDto>))]
internal sealed partial class PomodoroJsonContext : JsonSerializerContext
{
}

/// <summary>Shared file plumbing: atomic write (temp file, then move) and never-throwing reads.</summary>
internal static class JsonFile
{
    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PomodoroDock");

    public static T? Read<T>(string path, JsonTypeInfo<T> info)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(path), info);
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Reading {Path.GetFileName(path)} failed, using defaults: {ex.Message}");
            return null;
        }
    }

    public static void Write<T>(string path, T value, JsonTypeInfo<T> info)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, info));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Writing {Path.GetFileName(path)} failed: {ex.Message}");
        }
    }
}

/// <summary>%LOCALAPPDATA%\PomodoroDock\state.json.</summary>
public sealed class FileStateStore(string path) : IStateStore
{
    public static string DefaultPath() => Path.Combine(JsonFile.DefaultDirectory(), "state.json");

    public TimerState? Load() => JsonFile.Read(path, PomodoroJsonContext.Default.TimerState);

    public void Save(TimerState state) => JsonFile.Write(path, state, PomodoroJsonContext.Default.TimerState);
}

/// <summary>%LOCALAPPDATA%\PomodoroDock\history.json.</summary>
public sealed class FileHistoryStore(string path) : IHistoryStore
{
    public static string DefaultPath() => Path.Combine(JsonFile.DefaultDirectory(), "history.json");

    public Dictionary<DateOnly, DayStats> Load()
    {
        var result = new Dictionary<DateOnly, DayStats>();
        var rows = JsonFile.Read(path, PomodoroJsonContext.Default.ListDayStatsDto);
        if (rows is null)
        {
            return result;
        }

        foreach (var row in rows)
        {
            if (DateOnly.TryParseExact(row.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                result[day] = new DayStats(row.Pomodoros, row.FocusSeconds, row.ShortBreaks, row.LongBreaks);
            }
        }

        return result;
    }

    public void Save(IReadOnlyDictionary<DateOnly, DayStats> history)
    {
        var rows = new List<DayStatsDto>(history.Count);
        foreach (var (day, s) in history.OrderBy(p => p.Key))
        {
            rows.Add(new DayStatsDto
            {
                Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Pomodoros = s.Pomodoros,
                FocusSeconds = s.FocusSeconds,
                ShortBreaks = s.ShortBreaks,
                LongBreaks = s.LongBreaks,
            });
        }

        JsonFile.Write(path, rows, PomodoroJsonContext.Default.ListDayStatsDto);
    }
}

/// <summary>In-memory state store for tests.</summary>
public sealed class MemoryStateStore : IStateStore
{
    public TimerState? Saved { get; set; }

    public int SaveCount { get; private set; }

    public TimerState? Load() => Saved;

    public void Save(TimerState state)
    {
        Saved = state;
        SaveCount++;
    }
}

/// <summary>In-memory history store for tests.</summary>
public sealed class MemoryHistoryStore : IHistoryStore
{
    public Dictionary<DateOnly, DayStats> Saved { get; set; } = [];

    public Dictionary<DateOnly, DayStats> Load() => new(Saved);

    public void Save(IReadOnlyDictionary<DateOnly, DayStats> history) => Saved = new Dictionary<DateOnly, DayStats>(history);
}
