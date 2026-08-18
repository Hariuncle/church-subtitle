[CmdletBinding()]
param(
    [string]$ToolRoot = (Join-Path $env:LOCALAPPDATA 'church-subtitle-tools')
)

$ErrorActionPreference = 'Stop'
$dotnetRoot = Join-Path $ToolRoot 'dotnet'
$mediaRoot = Join-Path $ToolRoot 'media'
$installer = Join-Path $ToolRoot 'dotnet-install.ps1'
$ffmpegArchive = Join-Path $ToolRoot 'ffmpeg.zip'
$denoArchive = Join-Path $ToolRoot 'deno.zip'

New-Item -ItemType Directory -Force -Path $ToolRoot, $mediaRoot | Out-Null

if (-not (Test-Path -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe'))) {
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Channel 8.0 -Architecture x64 -InstallDir $dotnetRoot -NoPath
}

if (-not (Test-Path -LiteralPath (Join-Path $mediaRoot 'yt-dlp.exe'))) {
    Invoke-WebRequest -UseBasicParsing `
        'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' `
        -OutFile (Join-Path $mediaRoot 'yt-dlp.exe')
}

if (-not (Get-ChildItem -LiteralPath $mediaRoot -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue)) {
    Invoke-WebRequest -UseBasicParsing `
        'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip' `
        -OutFile $ffmpegArchive
    Expand-Archive -LiteralPath $ffmpegArchive -DestinationPath $mediaRoot -Force
}

if (-not (Test-Path -LiteralPath (Join-Path $mediaRoot 'deno.exe'))) {
    Invoke-WebRequest -UseBasicParsing `
        'https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip' `
        -OutFile $denoArchive
    Expand-Archive -LiteralPath $denoArchive -DestinationPath $mediaRoot -Force
}

$dotnet = Join-Path $dotnetRoot 'dotnet.exe'
$ytDlp = Join-Path $mediaRoot 'yt-dlp.exe'
$deno = Join-Path $mediaRoot 'deno.exe'
$ffmpeg = Get-ChildItem -LiteralPath $mediaRoot -Filter ffmpeg.exe -Recurse |
    Select-Object -First 1 -ExpandProperty FullName

& $dotnet --version
& $ytDlp --version
& $deno --version | Select-Object -First 1
& $ffmpeg -version | Select-Object -First 1
Write-Host "Tools installed at: $ToolRoot"
