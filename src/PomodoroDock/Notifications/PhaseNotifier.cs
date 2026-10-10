using System;
using PomodoroDock.Core;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace PomodoroDock;

/// <summary>
/// Windows toasts for phase ends, via the WinRT <see cref="ToastNotificationManager"/> with the package's own
/// identity (no AUMID, no activator, no click action). The end of a running phase is handed to Windows as a
/// scheduled toast, so it still appears if this process is idle or gone. Never throws into the caller; after
/// repeated failures toasts stay off for the life of the process.
/// </summary>
internal sealed partial class PhaseNotifier
{
    private const string ToastGroup = "PomodoroDock";
    private const string ScheduledId = "phase";
    private const int MaxFailures = 3;
    private static readonly TimeSpan MinLead = TimeSpan.FromSeconds(5);

    // Toast APIs are not Command Palette host calls, so holding this lock around them is safe.
    private readonly object _toastGate = new();
    private int _failures;
    private bool _disabled;
    private bool _loggedSetting;

    /// <summary>
    /// Makes the scheduled toasts match <paramref name="snapshot"/>: at most one, for the running phase's end.
    /// Returns true when a toast for that end is in place.
    /// </summary>
    public bool Reschedule(EngineSnapshot snapshot, bool enabled)
    {
        lock (_toastGate)
        {
            if (_disabled)
            {
                return false;
            }

            var now = DateTimeOffset.Now;
            DateTimeOffset? desired = snapshot.Status == RunStatus.Running && enabled
                && snapshot.State.EndsAt is { } ends && ends > now + MinLead
                ? ends
                : null;

            try
            {
                var notifier = ToastNotificationManager.CreateToastNotifier();
                if (notifier.Setting != NotificationSetting.Enabled && !_loggedSetting)
                {
                    _loggedSetting = true;
                    DiagLog.WriteLine($"Toast notifier setting is {notifier.Setting}; toasts may not appear.");
                }

                var present = false;
                foreach (var scheduled in notifier.GetScheduledToastNotifications())
                {
                    if (scheduled.Group != ToastGroup)
                    {
                        continue;
                    }

                    if (desired is { } d && !present && scheduled.DeliveryTime == d)
                    {
                        present = true;
                    }
                    else if (scheduled.DeliveryTime > now)
                    {
                        // Due ones are about to be shown by Windows; leave them alone.
                        notifier.RemoveFromSchedule(scheduled);
                    }
                }

                if (desired is { } at && !present)
                {
                    var (title, body) = ScheduledText(snapshot);
                    notifier.AddToSchedule(new ScheduledToastNotification(BuildXml(title, body), at)
                    {
                        Id = ScheduledId,
                        Group = ToastGroup,
                    });
                    present = true;
                }

                _failures = 0;
                return present;
            }
            catch (ArgumentException ex)
            {
                // The delivery time slipped into the past between the check and the call: not a toast failure.
                DiagLog.WriteLine($"Scheduling toast skipped: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                RecordFailure("Scheduling toast failed", ex);
                return false;
            }
        }
    }

    /// <summary>Shows a toast right now for a phase that ended without a scheduled one.</summary>
    public void ShowNow(PhaseCompleted completed, PomodoroOptions options, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        lock (_toastGate)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                var (title, body) = Formatting.Notice(completed.Finished, completed.Next, completed.NextAutoStarted, options);
                var toast = new ToastNotification(BuildXml(title, body)) { Tag = "phase-done", Group = ToastGroup };
                ToastNotificationManager.CreateToastNotifier().Show(toast);
                _failures = 0;
            }
            catch (Exception ex)
            {
                RecordFailure("Showing toast failed", ex);
            }
        }
    }

    private static (string Title, string Body) ScheduledText(EngineSnapshot s) => Formatting.ScheduledNotice(s.Phase);

    private static XmlDocument BuildXml(string title, string body)
    {
        var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
        var textNodes = xml.GetElementsByTagName("text");

        // DOM text nodes escape markup characters themselves.
        textNodes.Item(0).AppendChild(xml.CreateTextNode(title));
        textNodes.Item(1).AppendChild(xml.CreateTextNode(body));
        return xml;
    }

    private void RecordFailure(string what, Exception ex)
    {
        _failures++;
        if (_failures >= MaxFailures)
        {
            _disabled = true;
            DiagLog.WriteLine($"{what} {_failures} times in a row, disabling toasts for this session: {ex}");
        }
    }
}
