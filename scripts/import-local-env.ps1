[CmdletBinding()]
param(
    [string]$Path
)

if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $PSScriptRoot '..\.env'
}

if (-not [string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY) -or
    -not (Test-Path -LiteralPath $Path)) {
    return
}

foreach ($line in Get-Content -LiteralPath $Path) {
    if ($line -notmatch '^\s*OPENAI_API_KEY\s*=\s*(.*)\s*$') {
        continue
    }

    $value = $Matches[1].Trim()
    if ($value.Length -ge 2 -and
        (($value.StartsWith('"') -and $value.EndsWith('"')) -or
         ($value.StartsWith("'") -and $value.EndsWith("'")))) {
        $value = $value.Substring(1, $value.Length - 2)
    }

    if (-not [string]::IsNullOrWhiteSpace($value)) {
        $env:OPENAI_API_KEY = $value
    }

    return
}
