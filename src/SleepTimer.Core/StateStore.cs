using System.Collections.Concurrent;
using System.Text.Json;

namespace SleepTimer.Core;

public sealed class StateStore
{
    private const string PreferencesFileName = "preferences.json";
    private const string TaskFileName = "task.json";
    private const string HistoryFileName = "history.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _dataDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public StateStore(string portableDirectory, JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portableDirectory);
        _dataDirectory = Path.Combine(Path.GetFullPath(portableDirectory), "data");
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.General);
    }

    public Task SavePreferencesAsync<T>(T preferences, CancellationToken cancellationToken = default) =>
        SaveAsync(PreferencesFileName, preferences, cancellationToken);

    public Task<T> LoadPreferencesAsync<T>(T defaultValue, CancellationToken cancellationToken = default) =>
        LoadAsync(PreferencesFileName, defaultValue, cancellationToken);

    public Task SaveTaskAsync<T>(T task, CancellationToken cancellationToken = default) =>
        SaveAsync(TaskFileName, task, cancellationToken);

    public Task<T> LoadTaskAsync<T>(T defaultValue, CancellationToken cancellationToken = default) =>
        LoadAsync(TaskFileName, defaultValue, cancellationToken);

    public Task SaveHistoryAsync(
        IReadOnlyList<TimerHistoryEntry> history,
        CancellationToken cancellationToken = default) =>
        SaveAsync(HistoryFileName, history, cancellationToken);

    public async Task<IReadOnlyList<TimerHistoryEntry>> LoadHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        var history = await LoadAsync(
            HistoryFileName,
            new List<TimerHistoryEntry>(),
            cancellationToken);
        return history;
    }

    private async Task SaveAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);
        var destinationPath = Path.Combine(_dataDirectory, fileName);
        var fileLock = FileLocks.GetOrAdd(destinationPath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken);
        var temporaryPath = Path.Combine(_dataDirectory, $".{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destinationPath))
                File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            fileLock.Release();
        }
    }

    private async Task<T> LoadAsync<T>(string fileName, T defaultValue, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_dataDirectory, fileName);
        var fileLock = FileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(path))
                return defaultValue;

            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var value = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
                return value is null ? defaultValue : value;
            }
            catch (JsonException)
            {
                File.Move(path, path + ".bak", overwrite: true);
                return defaultValue;
            }
        }
        finally
        {
            fileLock.Release();
        }
    }
}
