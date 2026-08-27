using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace SleepTimer.Core;

public enum AppLogLevel
{
    Information,
    Warning,
    Error
}

public interface IAppLogger
{
    Task LogAsync(
        AppLogLevel level,
        string eventCode,
        string? errorCode = null,
        CancellationToken cancellationToken = default);
}

public sealed class AppLoggerOptions
{
    public long MaxFileBytes { get; init; } = 1024 * 1024;
    public int RetainedFileCount { get; init; } = 7;
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.Now;
}

public sealed class FileAppLogger : IAppLogger
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _logDirectory;
    private readonly AppLoggerOptions _options;

    public FileAppLogger(string portableDirectory, AppLoggerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portableDirectory);
        _options = options ?? new AppLoggerOptions();
        if (_options.MaxFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxFileBytes must be positive.");
        if (_options.RetainedFileCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RetainedFileCount must be positive.");
        ArgumentNullException.ThrowIfNull(_options.Clock);

        _logDirectory = Path.Combine(Path.GetFullPath(portableDirectory), "data", "logs");
    }

    public async Task LogAsync(
        AppLogLevel level,
        string eventCode,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(level))
            throw new ArgumentOutOfRangeException(nameof(level));
        EnsureSafeCode(eventCode, nameof(eventCode));
        if (errorCode is not null)
            EnsureSafeCode(errorCode, nameof(errorCode));

        var timestamp = _options.Clock();
        var filePath = Path.Combine(
            _logDirectory,
            $"app-{timestamp:yyyyMMdd}.log");
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp:O}|{level}|{eventCode}|{errorCode ?? "-"}{Environment.NewLine}");
        var bytes = Encoding.UTF8.GetBytes(line);
        var fileLock = FileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(_logDirectory);
            if (File.Exists(filePath) && new FileInfo(filePath).Length + bytes.Length > _options.MaxFileBytes)
                Rotate(filePath);

            await using var stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
            CleanupOldFiles();
        }
        finally
        {
            fileLock.Release();
        }
    }

    private void Rotate(string filePath)
    {
        for (var index = _options.RetainedFileCount - 1; index >= 1; index--)
        {
            var source = GetRotatedPath(filePath, index - 1);
            var destination = GetRotatedPath(filePath, index);
            if (File.Exists(source))
                File.Move(source, destination, overwrite: true);
        }
    }

    private static string GetRotatedPath(string filePath, int index) =>
        index == 0
            ? filePath
            : $"{Path.Combine(Path.GetDirectoryName(filePath)!, Path.GetFileNameWithoutExtension(filePath))}.{index}.log";

    private void CleanupOldFiles()
    {
        var files = Directory.GetFiles(_logDirectory, "app-*.log")
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ThenByDescending(path => path, StringComparer.Ordinal)
            .Skip(_options.RetainedFileCount)
            .ToArray();
        foreach (var file in files)
            File.Delete(file);
    }

    private static void EnsureSafeCode(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException("Codes may contain only letters, digits, '.', '-' and '_'.", parameterName);
        }
    }
}
