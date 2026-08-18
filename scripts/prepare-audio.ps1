[CmdletBinding()]
param(
    [string]$Url = 'https://www.youtube.com/watch?v=YLsWQq04R10&t=1045s',
    [string]$Start = '00:09:25',
    [string]$Duration = '00:15:00',
    [string]$OutputDirectory = 'data\poc-source-15m',
    [switch]$ConfirmAuthorizedSource
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmAuthorizedSource) {
    throw 'Source authorization must be confirmed with -ConfirmAuthorizedSource.'
}

function Resolve-MediaTool([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $mediaRoot = Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\media'
    $candidate = Get-ChildItem -LiteralPath $mediaRoot -Filter "$Name.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $candidate) {
        throw "$Name was not found. Run scripts\install-tools.ps1 first."
    }

    return $candidate
}

$ytDlp = Resolve-MediaTool 'yt-dlp'
$ffmpeg = Resolve-MediaTool 'ffmpeg'
$ffprobe = Resolve-MediaTool 'ffprobe'
$deno = Resolve-MediaTool 'deno'
$absoluteOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $absoluteOutput | Out-Null

$existingOriginal = Get-ChildItem -LiteralPath $absoluteOutput -Filter 'original.*' -File -ErrorAction SilentlyContinue
if ($existingOriginal) {
    throw "Original media already exists and will not be overwritten: $($existingOriginal.FullName)"
}

$reservedOutputs = @(
    (Join-Path $absoluteOutput 'service-segment-48k-mono.flac'),
    (Join-Path $absoluteOutput 'service-segment-24k-mono-s16le.pcm'),
    (Join-Path $absoluteOutput 'source-metadata.json')
)
foreach ($reservedOutput in $reservedOutputs) {
    if (Test-Path -LiteralPath $reservedOutput) {
        throw "Output already exists and will not be overwritten: $reservedOutput"
    }
}

& $ytDlp --no-playlist --no-overwrites `
    --js-runtimes "deno:$deno" `
    --extractor-args 'youtube:player_client=web_embedded' `
    -f 'bestaudio/best' `
    -o (Join-Path $absoluteOutput 'original.%(ext)s') $Url
if ($LASTEXITCODE -ne 0) {
    throw "yt-dlp failed with exit code $LASTEXITCODE."
}

$original = Get-ChildItem -LiteralPath $absoluteOutput -Filter 'original.*' -File |
    Select-Object -First 1 -ExpandProperty FullName
$archiveFlac = Join-Path $absoluteOutput 'service-segment-48k-mono.flac'
$openAiPcm = Join-Path $absoluteOutput 'service-segment-24k-mono-s16le.pcm'
$metadataPath = Join-Path $absoluteOutput 'source-metadata.json'

& $ffmpeg -hide_banner -n -ss $Start -t $Duration -i $original `
    -vn -ac 1 -ar 48000 -c:a flac $archiveFlac
if ($LASTEXITCODE -ne 0) {
    throw "FLAC conversion failed with exit code $LASTEXITCODE."
}

& $ffmpeg -hide_banner -n -ss $Start -t $Duration -i $original `
    -vn -ac 1 -ar 24000 -c:a pcm_s16le -f s16le $openAiPcm
if ($LASTEXITCODE -ne 0) {
    throw "OpenAI PCM conversion failed with exit code $LASTEXITCODE."
}

$expectedSeconds = [TimeSpan]::Parse($Duration).TotalSeconds
$expectedPcmBytes = [long]($expectedSeconds * 24000 * 2)
$actualPcmBytes = (Get-Item -LiteralPath $openAiPcm).Length
if ($actualPcmBytes -lt ($expectedPcmBytes * 0.99)) {
    $actualDuration = $actualPcmBytes / 24000 / 2
    throw "PCM is shorter than requested: expected $expectedSeconds seconds, got $actualDuration seconds. Check the source duration and start time."
}

$archiveDuration = & $ffprobe -v error -show_entries format=duration `
    -of default=noprint_wrappers=1:nokey=1 $archiveFlac
if ($LASTEXITCODE -ne 0) {
    throw "FLAC duration validation failed with exit code $LASTEXITCODE."
}

$metadata = [ordered]@{
    sourceUrl = $Url
    start = $Start
    duration = $Duration
    authorizedSourceConfirmed = $true
    authorizedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$metadataJson = $metadata | ConvertTo-Json
[System.IO.File]::WriteAllText(
    $metadataPath,
    $metadataJson,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Original: $original"
Write-Host "Archive FLAC: $archiveFlac"
Write-Host "OpenAI PCM: $openAiPcm"
Write-Host "Metadata: $metadataPath"
Write-Host "Verified duration: $([math]::Round([double]$archiveDuration, 2)) seconds"
