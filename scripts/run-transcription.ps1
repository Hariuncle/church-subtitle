[CmdletBinding()]
param(
    [ValidateSet('low', 'medium')]
    [string]$Delay = 'low',
    [string]$PcmPath = 'data\poc-source-15m\service-segment-24k-mono-s16le.pcm',
    [string]$KeywordsPath = 'config\keywords.txt',
    [string]$OutputRoot = 'runs'
)

$ErrorActionPreference = 'Stop'

function Resolve-Dotnet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        $installedSdks = @(& $command.Source --list-sdks)
        if ($installedSdks.Count -gt 0) {
            return $command.Source
        }
    }

    $candidate = Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw 'The .NET 8 SDK was not found. Run scripts\install-tools.ps1 first.'
    }

    return $candidate
}

. (Join-Path $PSScriptRoot 'import-local-env.ps1')

if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    throw 'Set OPENAI_API_KEY in the project .env file or current process first.'
}

$dotnet = Resolve-Dotnet
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$output = Join-Path $OutputRoot "$timestamp-$Delay"

& $dotnet run --project 'src\ChurchSubtitle.Cli\ChurchSubtitle.Cli.csproj' -- `
    transcribe `
    --pcm $PcmPath `
    --delay $Delay `
    --output $output `
    --keywords $KeywordsPath
if ($LASTEXITCODE -ne 0) {
    throw "Transcription failed with exit code $LASTEXITCODE."
}

Write-Host "Run directory: $output"
