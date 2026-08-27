$previewPath = 'E:\codex_project\assets\generated\sleep-timer-icon-reference-preview-v2.png'
$appDirectory = 'E:\codex_project\src\SleepTimer.App\Assets'
$appPngPath = Join-Path $appDirectory 'sleep-timer-icon-v3.png'
$appIcoPath = Join-Path $appDirectory 'sleep-timer-icon-v3.ico'
Copy-Item $previewPath $appPngPath -Force
$pngBytes = [System.IO.File]::ReadAllBytes($appPngPath)
$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([uint16]1)
$writer.Write([uint16]32)
$writer.Write([uint32]$pngBytes.Length)
$writer.Write([uint32]22)
$writer.Write($pngBytes)
$writer.Flush()
[System.IO.File]::WriteAllBytes($appIcoPath, $stream.ToArray())
$writer.Dispose()
$stream.Dispose()
