$ErrorActionPreference = 'Stop'
$loader = Join-Path $PSScriptRoot '..\..\scripts\import-local-env.ps1'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid())
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
$temporaryEnv = Join-Path $temporaryDirectory '.env'

try {
    [System.IO.File]::WriteAllText(
        $temporaryEnv,
        "# local key`nOPENAI_API_KEY=file-value`n")

    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    . $loader -Path $temporaryEnv
    if ($env:OPENAI_API_KEY -ne 'file-value') {
        throw 'File value was not loaded.'
    }

    $env:OPENAI_API_KEY = 'process-value'
    . $loader -Path $temporaryEnv
    if ($env:OPENAI_API_KEY -ne 'process-value') {
        throw 'Process value was overwritten.'
    }

    Write-Output 'PASS'
}
finally {
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
}
