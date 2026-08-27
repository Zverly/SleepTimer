namespace SleepTimer.Core;

public enum TimerMode
{
    Duration,
    SpecificTime
}

public sealed record TimerPreset
{
    public TimerPreset(string name, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name is required.", nameof(name));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Preset duration must be positive.");

        Name = name;
        Duration = duration;
    }

    public TimerPreset(string name, int durationMinutes)
        : this(name, TimeSpan.FromMinutes(durationMinutes))
    {
    }

    public string Name { get; }
    public TimeSpan Duration { get; }
}

public static class TimerPresets
{
    public static IReadOnlyList<TimerPreset> Default { get; } =
    [
        new TimerPreset("30 分钟", 30),
        new TimerPreset("60 分钟", 60),
        new TimerPreset("90 分钟", 90),
        new TimerPreset("120 分钟", 120)
    ];
}