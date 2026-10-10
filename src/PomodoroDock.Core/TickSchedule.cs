namespace PomodoroDock.Core;

/// <summary>When the Dock timer should wake next.</summary>
public static class TickSchedule
{
    /// <summary>While idle or paused, only wake to roll the daily report over midnight.</summary>
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan Min = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Running: the moment the displayed (rounded-up) second changes, plus a small margin, within 50 ms to 1 s.
    /// A phase that already ran out is retried in 50 ms.
    /// </summary>
    public static TimeSpan NextDelay(EngineSnapshot s)
    {
        if (s.Status != RunStatus.Running)
        {
            return IdleInterval;
        }

        var seconds = s.Remaining.TotalSeconds;
        if (seconds <= 0)
        {
            return Min;
        }

        // The display shows ceil(seconds); it drops by one when remaining falls to the previous whole second.
        var untilChange = TimeSpan.FromSeconds(seconds - (Math.Ceiling(seconds) - 1)) + Margin;
        return untilChange < Min ? Min : untilChange > Max ? Max : untilChange;
    }
}
