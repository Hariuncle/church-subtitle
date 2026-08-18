[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunDirectory,
    [Parameter(Mandatory)]
    [string]$ReferencePath,
    [string]$KeywordsPath = 'config\keywords.txt'
)

$ErrorActionPreference = 'Stop'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if ($dotnet) {
    $installedSdks = @(& $dotnet --list-sdks)
}
if (-not $dotnet -or $installedSdks.Count -eq 0) {
    $dotnet = Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw 'The .NET 8 SDK was not found. Run scripts\install-tools.ps1 first.'
}

& $dotnet run --project 'src\ChurchSubtitle.Cli\ChurchSubtitle.Cli.csproj' -- `
    evaluate `
    --reference $ReferencePath `
    --hypothesis (Join-Path $RunDirectory 'final.txt') `
    --keywords $KeywordsPath `
    --runtime-metrics (Join-Path $RunDirectory 'runtime-metrics.json') `
    --output (Join-Path $RunDirectory 'evaluation.json')
if ($LASTEXITCODE -ne 0) {
    throw "Evaluation failed with exit code $LASTEXITCODE."
}
