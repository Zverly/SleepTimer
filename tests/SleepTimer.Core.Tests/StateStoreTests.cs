using System.Text.Json;
using SleepTimer.Core;
using Xunit;

namespace SleepTimer.Core.Tests;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _portableDirectory = Path.Combine(
        @"E:\codex_project",
        ".tmp",
        "state-store-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SavePreferences_WritesPortableDataFile()
    {
        var store = new StateStore(_portableDirectory);
        var preferences = new TestPreferences("Sleep", 90);

        await store.SavePreferencesAsync(preferences);

        var path = Path.Combine(_portableDirectory, "data", "preferences.json");
        Assert.True(File.Exists(path));
        Assert.Equal(preferences, JsonSerializer.Deserialize<TestPreferences>(await File.ReadAllTextAsync(path)));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task SavePreferences_WhenSerializationFails_PreservesExistingFile()
    {
        var store = new StateStore(_portableDirectory);
        var original = new TestPreferences("Shutdown", 60);
        await store.SavePreferencesAsync(original);
        var invalid = new CyclicPreferences();
        invalid.Self = invalid;

        await Assert.ThrowsAsync<JsonException>(() => store.SavePreferencesAsync(invalid));

        Assert.Equal(original, await store.LoadPreferencesAsync(new TestPreferences("Default", 30)));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_portableDirectory, "data"), "*.tmp"));
    }

    [Fact]
    public async Task SaveTask_WritesPortableDataFile()
    {
        var store = new StateStore(_portableDirectory);
        var task = new TestTask("SleepTimer.Action", new DateTime(2026, 8, 27, 23, 30, 0));

        await store.SaveTaskAsync(task);

        var path = Path.Combine(_portableDirectory, "data", "task.json");
        Assert.True(File.Exists(path));
        Assert.Equal(task, await store.LoadTaskAsync(new TestTask("Default", DateTime.MinValue)));
    }

    [Fact]
    public async Task LoadPreferences_WhenJsonIsCorrupt_BacksUpFileAndReturnsDefault()
    {
        var dataDirectory = Path.Combine(_portableDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "preferences.json");
        await File.WriteAllTextAsync(path, "{ not-json");
        var store = new StateStore(_portableDirectory);
        var fallback = new TestPreferences("Shutdown", 60);

        var loaded = await store.LoadPreferencesAsync(fallback);

        Assert.Equal(fallback, loaded);
        Assert.False(File.Exists(path));
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(path + ".bak"));
    }

    [Fact]
    public async Task LoadTask_WhenFileDoesNotExist_ReturnsDefault()
    {
        var store = new StateStore(_portableDirectory);
        var fallback = new TestTask("Default", DateTime.MinValue);

        var loaded = await store.LoadTaskAsync(fallback);

        Assert.Equal(fallback, loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_portableDirectory))
            Directory.Delete(_portableDirectory, recursive: true);
    }

    private sealed record TestPreferences(string Action, int PresetMinutes);

    private sealed record TestTask(string Name, DateTime TargetTime);

    private sealed class CyclicPreferences
    {
        public CyclicPreferences? Self { get; set; }
    }
}
