using SleepTimer.Windows;
using Xunit;

namespace SleepTimer.Windows.Tests;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public void SetEnabled_WritesSilentCommandAndCanRemoveIt()
    {
        var store = new FakeStartupValueStore();
        var registration = new WindowsStartupRegistration(store);
        var applicationPath = @"E:\Sleep Timer\SleepTimer.App.exe";

        registration.SetEnabled(applicationPath, enabled: true, silent: true);

        Assert.Equal($"\"{applicationPath}\" --silent", store.Value);
        Assert.True(registration.IsEnabled(applicationPath));

        registration.SetEnabled(applicationPath, enabled: false, silent: false);

        Assert.Null(store.Value);
        Assert.False(registration.IsEnabled(applicationPath));
    }

    [Fact]
    public void IsEnabled_RejectsACommandForAnotherPath()
    {
        var store = new FakeStartupValueStore { Value = "\"E:\\Other\\SleepTimer.App.exe\" --silent" };
        var registration = new WindowsStartupRegistration(store);

        Assert.False(registration.IsEnabled(@"E:\SleepTimer.App.exe"));
    }

    private sealed class FakeStartupValueStore : IStartupValueStore
    {
        public string? Value { get; set; }

        public string? GetValue(string valueName) => Value;

        public void SetValue(string valueName, string value) => Value = value;

        public void DeleteValue(string valueName) => Value = null;
    }
}
