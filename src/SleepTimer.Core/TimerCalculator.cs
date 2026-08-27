namespace SleepTimer.Core;

public static class TimerCalculator
{
    public static DateTime NormalizeTargetToMinute(DateTime target)
    {
        var remainder = target.Ticks % TimeSpan.TicksPerMinute;
        if (remainder == 0)
            return target;

        return target.AddTicks(-remainder).AddMinutes(1);
    }

    public static DateTime CalculateDurationTarget(DateTime now, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration cannot be negative.");

        return now.Add(duration);
    }

    public static DateTime CalculateSpecificTarget(DateTime now, TimeSpan timeOfDay)
    {
        if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(timeOfDay), "Time must be within a day.");

        var target = now.Date.Add(timeOfDay);
        return target > now ? target : target.AddDays(1);
    }

    public static DateTime CalculateTarget(DateTime now, TimerMode mode, TimeSpan value) =>
        mode switch
        {
            TimerMode.Duration => CalculateDurationTarget(now, value),
            TimerMode.SpecificTime => CalculateSpecificTarget(now, value),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}
