[CmdletBinding()]
param(
    [string]$PcmPath = 'data\poc-source-15m\service-segment-24k-mono-s16le.pcm',
    [string]$KeywordsPath = 'config\keywords.txt',
    [switch]$ConfirmEstimatedCost
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmEstimatedCost) {
    throw 'The 15-minute low + medium run is approximately $0.51 at the current public price. Re-run with -ConfirmEstimatedCost.'
}

& (Join-Path $PSScriptRoot 'run-transcription.ps1') `
    -Delay low -PcmPath $PcmPath -KeywordsPath $KeywordsPath
& (Join-Path $PSScriptRoot 'run-transcription.ps1') `
    -Delay medium -PcmPath $PcmPath -KeywordsPath $KeywordsPath
