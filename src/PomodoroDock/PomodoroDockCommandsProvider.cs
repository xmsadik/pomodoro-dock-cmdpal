using System;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PomodoroDock.Core;

namespace PomodoroDock;

public partial class PomodoroDockCommandsProvider : CommandProvider
{
    private readonly PomodoroEngine _engine;
    private readonly SettingsManager _settingsManager = new();
    private readonly PhaseNotifier _notifier = new();
    private readonly TimerPage _page;
    private readonly PomodoroBand _band;
    private readonly CommandItem _topItem;
    private readonly ICommandItem[] _commands;
    private readonly WrappedDockItem _dockBand;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly object _scheduleGate = new(); // serializes snapshot -> toast schedule -> stamp; never held across host calls
    private string _signature = "";
    private string _topSubtitle = "";
    private long _seq;
    private bool _tickRunning;
    private bool _disposed;

    public PomodoroDockCommandsProvider()
    {
        DisplayName = "Pomodoro Dock";
        Id = "PomodoroDock";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _engine = new PomodoroEngine(
            TimeProvider.System,
            _settingsManager.Options,
            new FileStateStore(FileStateStore.DefaultPath()),
            new FileHistoryStore(FileHistoryStore.DefaultPath()));
        _page = new TimerPage(_engine, _settingsManager, Run);
        _band = new PomodoroBand(_page);

        _topItem = new CommandItem(_page)
        {
            Title = DisplayName,
            MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
        };
        _commands = [_topItem];

        // Command.Id must be non-empty or the host silently drops the band.
        _dockBand = new WrappedDockItem([_band], "PomodoroDock.dock.timer", "Pomodoro Dock");
        Settings = _settingsManager.Settings;

        // Startup order: load (engine ctor) -> advance -> notify -> schedule -> render -> start timer.
        _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            NotifyCompletions(_engine.Advance());
            Reschedule();
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Startup failed: {ex}");
        }

        Refresh(force: true);
        _settingsManager.Settings.SettingsChanged += OnSettingsChanged;
        ArmTimer(onlyIfIdle: false);
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_dockBand];

    /// <summary>Runs a command's engine operation, then notifies, reschedules and re-renders at once.</summary>
    private void Run(Func<PomodoroEngine, EngineResult> operation)
    {
        Handle(operation(_engine));
        ArmTimer(onlyIfIdle: true);
    }

    private void Handle(EngineResult result)
    {
        NotifyCompletions(result);
        Reschedule();
        Refresh(force: false);
    }

    private void OnSettingsChanged(object? sender, object? e)
    {
        try
        {
            Handle(_engine.UpdateOptions(_settingsManager.Options));
            ArmTimer(onlyIfIdle: true);
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Settings change failed: {ex}");
        }
    }

    /// <summary>
    /// Only the latest completion gets an immediate toast, and only when Windows was not already given one
    /// for it (a scheduled toast fires by itself, even while this process was idle or gone).
    /// </summary>
    private void NotifyCompletions(EngineResult result)
    {
        if (result.Completed.Count == 0)
        {
            return;
        }

        var latest = result.Completed[^1];
        if (!latest.ToastScheduled)
        {
            _notifier.ShowNow(latest, result.Snapshot.Options, _settingsManager.Notifications);
        }
    }

    /// <summary>Makes the single scheduled toast match the engine state and records the outcome in it.</summary>
    private void Reschedule()
    {
        lock (_scheduleGate)
        {
            var snapshot = _engine.Snapshot();
            var scheduled = _notifier.Reschedule(snapshot, _settingsManager.Notifications);
            _engine.SetNotificationScheduled(scheduled ? snapshot.State.EndsAt : null);
        }
    }

    private void OnTick(object? state)
    {
        lock (_gate)
        {
            _tickRunning = true;
        }

        try
        {
            var result = _engine.Advance();
            if (result.Completed.Count > 0)
            {
                NotifyCompletions(result);
                Reschedule();
            }

            Refresh(force: false);
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Timer tick failed: {ex}");
        }
        finally
        {
            // Non-reentrant: re-arm only after this tick finished.
            ArmTimer(onlyIfIdle: false, finishingTick: true);
        }
    }

    /// <summary>
    /// Schedules the next tick. A command that changed the cadence (start, pause) must not re-arm while a
    /// tick is in flight (<paramref name="onlyIfIdle"/>): that tick re-arms itself when it ends.
    /// </summary>
    private void ArmTimer(bool onlyIfIdle, bool finishingTick = false)
    {
        try
        {
            lock (_gate)
            {
                if (finishingTick)
                {
                    _tickRunning = false;
                }

                if (!_disposed && !(onlyIfIdle && _tickRunning))
                {
                    // Snapshot under _gate so a command that started the timer just before cannot be
                    // overwritten with a stale idle delay. The engine lock never takes _gate, so this nests safely.
                    _timer.Change(NextDelay(), Timeout.InfiniteTimeSpan);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>The one render path: band (Title every second, the rest only on change), top item, and the page when its content changed.</summary>
    private TimeSpan NextDelay()
    {
        try
        {
            return TickSchedule.NextDelay(_engine.Snapshot());
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Computing next tick failed: {ex}");
            return TickSchedule.IdleInterval;
        }
    }

    private void Refresh(bool force)
    {
        try
        {
            var snapshot = _engine.Snapshot();
            var days = _engine.GetLastDays(7);
            var pageSignature = TimerPage.Signature(snapshot, days);
            bool refreshPage;
            long seq;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                refreshPage = force || pageSignature != _signature;
                _signature = pageSignature;
                seq = ++_seq;
            }

            // Host property sets happen with no lock held: the host may answer PropChanged by calling back
            // into the extension on another thread, so holding a lock here can deadlock. A refresh that was
            // overtaken by a newer one skips applying its stale values.
            if (seq == Interlocked.Read(ref _seq))
            {
                _band.Render(snapshot);

                var subtitle = Formatting.Subtitle(snapshot);
                if (subtitle != _topSubtitle)
                {
                    try
                    {
                        _topItem.Subtitle = subtitle;
                        _topSubtitle = subtitle;
                    }
                    catch (Exception ex)
                    {
                        DiagLog.WriteLine($"Top item subtitle failed: {ex}");
                    }
                }
            }

            if (refreshPage)
            {
                _page.Refresh();
            }
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Refresh failed: {ex}");
        }
    }

    public override void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _timer.Dispose();
        _settingsManager.Settings.SettingsChanged -= OnSettingsChanged;
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
