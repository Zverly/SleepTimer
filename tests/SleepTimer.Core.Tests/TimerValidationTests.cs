using SleepTimer.Core;
using Xunit;

namespace SleepTimer.Core.Tests;

public class TimerValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TimerPreset_RejectsMissingName(string? name)
    {
        Assert.Throws<ArgumentException>(() => new TimerPreset(name!, TimeSpan.FromMinutes(30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TimerPreset_RejectsNonPositiveDuration(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimerPreset("Preset", TimeSpan.FromMinutes(minutes)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Duration_RejectsNonPositiveValue(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimerCalculator.CalculateDurationTarget(DateTime.Now, TimeSpan.FromMinutes(minutes)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void SpecificTime_RejectsValuesOutsideDay(int hours)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimerCalculator.CalculateSpecificTarget(DateTime.Now, TimeSpan.FromHours(hours)));
    }

    [Fact]
    public void SpecificTime_EqualToNow_RollsToNextDay()
    {
        var now = new DateTime(2026, 8, 26, 22, 10, 0);

        Assert.Equal(new DateTime(2026, 8, 27, 22, 10, 0),
            TimerCalculator.CalculateSpecificTarget(now, now.TimeOfDay));
    }

    [Fact]
    public void CalculateTarget_RejectsInvalidMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimerCalculator.CalculateTarget(DateTime.Now, (TimerMode)999, TimeSpan.FromMinutes(30)));
    }
}
