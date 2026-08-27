[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
$tempRoot = Join-Path $projectRoot '.tmp'
$publishDirectory = Join-Path $projectRoot 'artifacts\win-x64'
$zipPath = Join-Path $projectRoot 'artifacts\SleepTimer-win-x64.zip'
$projectRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\')
$projectPrefix = "$projectRoot\"

function Assert-InProjectRoot {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        $fullPath -ne $projectRoot) {
        throw "路径超出便携项目目录：$fullPath"
    }
}

function Assert-X64Pe {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "不是有效的 PE/DOS 文件：$Path"
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 6 -gt $bytes.Length -or
        $bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45 -or
        $bytes[$peOffset + 2] -ne 0 -or $bytes[$peOffset + 3] -ne 0) {
        throw "不是有效的 PE 文件：$Path"
    }

    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    if ($machine -ne 0x8664) {
        throw "发布程序不是 x64 PE（Machine=0x$('{0:X4}' -f $machine)）：$Path"
    }
}

function Get-RelativeFileMap {
    param([Parameter(Mandatory)][string]$Root)

    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $map = @{}
    Get-ChildItem -LiteralPath $rootPath -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($rootPath.Length).TrimStart('\')
        $map[$relative.ToLowerInvariant()] = [PSCustomObject]@{
            RelativePath = $relative
            Length = $_.Length
            Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
    return $map
}

Assert-InProjectRoot $tempRoot
Assert-InProjectRoot $publishDirectory
Assert-InProjectRoot $zipPath

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "E 盘项目 SDK 不存在：$dotnet"
}

$env:DOTNET_ROOT = Join-Path $projectRoot '.dotnet'
$env:DOTNET_CLI_HOME = Join-Path $tempRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $tempRoot 'nuget-packages'
$env:TEMP = Join-Path $tempRoot 'temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:TEMP | Out-Null
if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

& $dotnet test (Join-Path $projectRoot 'SleepTimer.sln') --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw '测试失败，已停止发布。' }

& $dotnet publish (Join-Path $projectRoot 'src\SleepTimer.App\SleepTimer.App.csproj') --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw '发布失败。' }

$publishedExe = Join-Path $publishDirectory 'SleepTimer.App.exe'
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "缺少 self-contained 单文件入口：$publishedExe"
}
Assert-X64Pe $publishedExe

Get-ChildItem -LiteralPath $publishDirectory -Recurse -Filter '*.pdb' -File | Remove-Item -Force

$forbiddenFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Extension -in '.pdb', '.trx', '.testlog', '.log' -or
    $_.FullName -match '[\\/]bin[\\/]' -or $_.FullName -match '[\\/]obj[\\/]'
}
if ($forbiddenFiles) {
    throw "发布目录包含禁止文件：$($forbiddenFiles.FullName -join ', ')"
}

$runtimeFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Name -in 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll'
}
if ($runtimeFiles) {
    throw "发布目录包含未打包运行时文件，self-contained 单文件审计失败：$($runtimeFiles.FullName -join ', ')"
}

$publishedFiles = Get-RelativeFileMap $publishDirectory
if (-not $publishedFiles.ContainsKey('sleeptimer.app.exe')) {
    throw '发布目录清单缺少应用入口。'
}

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

$zipFiles = @{}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $archive.Entries) {
        if ([String]::IsNullOrWhiteSpace($entry.Name)) { continue }
        $relative = $entry.FullName.Replace('/', '\')
        $destination = Join-Path $env:TEMP "SleepTimer-zip-audit-$([Guid]::NewGuid().ToString('N'))"
        try {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
            $zipFiles[$relative.ToLowerInvariant()] = [PSCustomObject]@{
                RelativePath = $relative
                Length = $entry.Length
                Hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            }
        } finally {
            if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
        }
    }
} finally {
    $archive.Dispose()
}

if ($publishedFiles.Count -ne $zipFiles.Count) {
    throw "ZIP 与发布目录文件数量不一致：$($publishedFiles.Count) vs $($zipFiles.Count)"
}
foreach ($key in $publishedFiles.Keys) {
    if (-not $zipFiles.ContainsKey($key) -or
        $publishedFiles[$key].Length -ne $zipFiles[$key].Length -or
        $publishedFiles[$key].Hash -ne $zipFiles[$key].Hash) {
        throw "ZIP 与发布目录内容不一致：$($publishedFiles[$key].RelativePath)"
    }
}

Write-Host "发布审计通过：x64 PE、self-contained 单文件、无调试/测试污染物、ZIP 内容一致。"
Write-Host "便携目录：$publishDirectory"
Write-Host "ZIP：$zipPath"
