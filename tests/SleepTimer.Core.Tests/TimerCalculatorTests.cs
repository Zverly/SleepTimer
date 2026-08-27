using SleepTimer.Core;
using Xunit;

namespace SleepTimer.Core.Tests;

public class TimerCalculatorTests
{
    [Fact]
    public void DefaultPresets_ContainsFourExpectedDurations()
    {
        var presets = TimerPresets.Default;

        Assert.Equal(4, presets.Count);
        Assert.Equal(new[] { 30, 60, 90, 120 }, presets.Select(p => (int)p.Duration.TotalMinutes));
    }

    [Fact]
    public void Duration_ReturnsTargetTime()
    {
        var now = new DateTime(2026, 8, 26, 22, 10, 0);

        Assert.Equal(now.AddMinutes(30), TimerCalculator.CalculateDurationTarget(now, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void NormalizeTargetToMinute_RoundsPartialMinuteUp()
    {
        var target = new DateTime(2026, 8, 26, 22, 10, 1, 500);

        Assert.Equal(
            new DateTime(2026, 8, 26, 22, 11, 0),
            TimerCalculator.NormalizeTargetToMinute(target));
    }

    [Fact]
    public void NormalizeTargetToMinute_LeavesWholeMinuteUnchanged()
    {
        var target = new DateTime(2026, 8, 26, 22, 10, 0);

        Assert.Equal(target, TimerCalculator.NormalizeTargetToMinute(target));
    }

    [Fact]
    public void SpecificTime_ThatHasPassed_RollsToNextDay()
    {
        var now = new DateTime(2026, 8, 26, 23, 30, 0);

        Assert.Equal(new DateTime(2026, 8, 27, 1, 15, 0),
            TimerCalculator.CalculateSpecificTarget(now, new TimeSpan(1, 15, 0)));
    }

    [Fact]
    public void SpecificTime_LaterToday_UsesToday()
    {
        var now = new DateTime(2026, 8, 26, 22, 10, 0);

        Assert.Equal(new DateTime(2026, 8, 26, 23, 15, 0),
            TimerCalculator.CalculateSpecificTarget(now, new TimeSpan(23, 15, 0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Duration_NonPositive_Throws(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimerCalculator.CalculateDurationTarget(DateTime.Now, TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void SpecificTime_EqualToNow_RollsToNextDay()
    {
        var now = new DateTime(2026, 8, 26, 23, 15, 0);
        Assert.Equal(now.AddDays(1), TimerCalculator.CalculateSpecificTarget(now, now.TimeOfDay));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void SpecificTime_OutsideDay_Throws(int hours) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimerCalculator.CalculateSpecificTarget(DateTime.Now, TimeSpan.FromHours(hours)));

    [Fact]
    public void CalculateTarget_InvalidMode_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimerCalculator.CalculateTarget(DateTime.Now, (TimerMode)999, TimeSpan.FromMinutes(30)));}
