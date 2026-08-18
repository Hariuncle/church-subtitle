[CmdletBinding()]
param(
    [string]$EnvPath,
    [switch]$ValidateOnly,
    [Parameter(DontShow)]
    [string[]]$DotnetCandidatePaths
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$expectedPcmLength = [long]70368000
if ([string]::IsNullOrWhiteSpace($EnvPath)) {
    $EnvPath = Join-Path $repositoryRoot '.env'
}

function Test-CompatibleDotnet {
    param(
        [string]$Path,
        [string]$WorkingDirectory
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    Push-Location -LiteralPath $WorkingDirectory
    try {
        try {
            $versionOutput = @(& $Path --version 2>$null)
            $exitCode = $LASTEXITCODE
        }
        catch {
            return $false
        }

        return $exitCode -eq 0 -and
            $versionOutput.Count -gt 0 -and
            ([string]$versionOutput[0]).Trim() -match '^8\.'
    }
    finally {
        Pop-Location
    }
}

function Resolve-Dotnet {
    param(
        [string]$WorkingDirectory,
        [string[]]$CandidatePaths
    )

    if (-not $CandidatePaths) {
        $candidates = [System.Collections.Generic.List[string]]::new()
        $command = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($command) {
            $candidates.Add($command.Source)
        }

        $localCandidate = Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe'
        if (-not $candidates.Contains($localCandidate)) {
            $candidates.Add($localCandidate)
        }

        $CandidatePaths = $candidates.ToArray()
    }

    foreach ($candidate in $CandidatePaths) {
        $resolvedCandidate = if ([System.IO.Path]::IsPathRooted($candidate)) {
            [System.IO.Path]::GetFullPath($candidate)
        }
        else {
            [System.IO.Path]::GetFullPath((Join-Path $WorkingDirectory $candidate))
        }

        if (Test-CompatibleDotnet -Path $resolvedCandidate -WorkingDirectory $WorkingDirectory) {
            return $resolvedCandidate
        }
    }

    throw 'A compatible .NET 8 SDK was not found. Run scripts\install-tools.ps1 first.'
}

function Resolve-RepositoryPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

. (Join-Path $PSScriptRoot 'import-local-env.ps1') -Path $EnvPath

if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    throw 'Set OPENAI_API_KEY in the project .env file or current process first.'
}

$resolvedPcmPath = Resolve-RepositoryPath 'data\poc-source-full\service-full-24k-mono-s16le.pcm'
if (-not (Test-Path -LiteralPath $resolvedPcmPath -PathType Leaf)) {
    throw "The prepared PCM file was not found: $resolvedPcmPath"
}
$pcmFile = Get-Item -LiteralPath $resolvedPcmPath
if ([long]$pcmFile.Length -ne $expectedPcmLength) {
    throw "The prepared PCM file must be exactly 70,368,000 bytes: $resolvedPcmPath"
}

$dotnet = Resolve-Dotnet -WorkingDirectory $repositoryRoot -CandidatePaths $DotnetCandidatePaths
$projectPath = Resolve-RepositoryPath 'src\ChurchSubtitle.Web\ChurchSubtitle.Web.csproj'
$url = 'http://127.0.0.1:5287'

if ($ValidateOnly) {
    [pscustomobject]@{
        Url = $url
        DotnetPath = $dotnet
        ProjectPath = $projectPath
        PcmPath = $resolvedPcmPath
    }
    return
}

Write-Host "Local caption UI: $url"
Write-Host 'Press Ctrl+C in this window to stop the server.'

& $dotnet run --project $projectPath --no-launch-profile
if ($LASTEXITCODE -ne 0) {
    throw "Caption web server failed with exit code $LASTEXITCODE."
}
