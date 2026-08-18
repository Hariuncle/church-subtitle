$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourceLauncher = Join-Path $repositoryRoot 'scripts\run-web.ps1'
$sourceLoader = Join-Path $repositoryRoot 'scripts\import-local-env.ps1'
$program = Join-Path $repositoryRoot 'src\ChurchSubtitle.Web\Program.cs'
$readme = Join-Path $repositoryRoot 'README.md'
$apiAndTestUiDocument = Join-Path $repositoryRoot 'docs\api-and-test-ui.md'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid())
$fakeRepository = Join-Path $temporaryDirectory 'fake-repository'
$fakeScripts = Join-Path $fakeRepository 'scripts'
$fakeBin = Join-Path $fakeRepository 'fake-bin'
$fakeLocalAppData = Join-Path $fakeRepository 'local-app-data'
$fakeLauncher = Join-Path $fakeScripts 'run-web.ps1'
$fakeLoader = Join-Path $fakeScripts 'import-local-env.ps1'
$fakeProbe = Join-Path $fakeScripts 'probe-launcher.ps1'
$fakeEnv = Join-Path $fakeRepository '.env'
$fakePcm = Join-Path $fakeRepository 'data\poc-source-full\service-full-24k-mono-s16le.pcm'
$fakeProject = Join-Path $fakeRepository 'src\ChurchSubtitle.Web\ChurchSubtitle.Web.csproj'
$fakeDotnet = Join-Path $fakeBin 'dotnet.cmd'
$sentinelKey = 'hermetic-sentinel-key-must-never-appear'
$missingEnv = Join-Path $fakeRepository 'missing.env'
$expectedPcmLength = [long]70368000
$powerShellExe = (Get-Command powershell.exe -ErrorAction Stop).Source
$capturedText = [System.Collections.Generic.List[string]]::new()

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Set-FileLength {
    param(
        [string]$Path,
        [long]$Length
    )

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $stream.SetLength($Length)
    }
    finally {
        $stream.Dispose()
    }
}

function Invoke-PowerShellFile {
    param(
        [string]$Path,
        [string[]]$Arguments
    )

    $argumentList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Path) + $Arguments
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $powerShellExe
    $startInfo.Arguments = ($argumentList | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    [void]$startInfo.EnvironmentVariables.Remove('OPENAI_API_KEY')
    $startInfo.EnvironmentVariables['PATH'] = $fakeBin
    $startInfo.EnvironmentVariables['LOCALAPPDATA'] = $fakeLocalAppData

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $standardOutput = $process.StandardOutput.ReadToEnd()
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $text = (@($standardOutput.TrimEnd(), $standardError.TrimEnd()) |
        Where-Object { $_.Length -gt 0 }) -join "`n"
    if ([string]::IsNullOrEmpty($text)) {
        $output = @()
    }
    else {
        $output = @($text -split "`r?`n")
    }

    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = @($output)
        Text = $text
    }
}

try {
    foreach ($directory in @(
        $fakeScripts,
        $fakeBin,
        $fakeLocalAppData,
        (Split-Path -Parent $fakePcm),
        (Split-Path -Parent $fakeProject)
    )) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Copy-Item -LiteralPath $sourceLauncher -Destination $fakeLauncher
    Copy-Item -LiteralPath $sourceLoader -Destination $fakeLoader
    [System.IO.File]::WriteAllText($fakeEnv, "OPENAI_API_KEY=$sentinelKey`r`n")
    Set-FileLength -Path $fakePcm -Length $expectedPcmLength
    [System.IO.File]::WriteAllText(
        $fakeProject,
        '<Project Sdk="Microsoft.NET.Sdk.Web"></Project>')
    [System.IO.File]::WriteAllText(
        $fakeDotnet,
        "@echo off`r`nif `"%1`"==`"--version`" (`r`n  echo 8.0.424`r`n  exit /b 0`r`n)`r`nexit /b 1`r`n")
    [System.IO.File]::WriteAllText(
        $fakeProbe,
        @'
param([string]$DotnetCandidatePath)
$result = & (Join-Path $PSScriptRoot 'run-web.ps1') `
    -ValidateOnly `
    -DotnetCandidatePaths @($DotnetCandidatePath)
$result | ConvertTo-Json -Compress
'@)

    $loaderRun = Invoke-PowerShellFile -Path $fakeLoader -Arguments @()
    $capturedText.Add($loaderRun.Text)
    Assert-True ($loaderRun.ExitCode -eq 0) 'The copied env loader failed with fake default inputs.'
    Assert-True ($loaderRun.Output.Count -eq 0) 'The copied env loader emitted configuration output.'

    $launcherRun = Invoke-PowerShellFile -Path $fakeLauncher -Arguments @('-ValidateOnly')
    $capturedText.Add($launcherRun.Text)
    Assert-True ($launcherRun.ExitCode -eq 0) 'The copied launcher failed with fake default inputs.'
    Assert-True ($launcherRun.Text.Contains('http://127.0.0.1:5287')) `
        'The copied launcher did not expose the fixed local URL.'

    $relativeDotnet = 'fake-bin\dotnet.cmd'
    $probeRun = Invoke-PowerShellFile -Path $fakeProbe `
        -Arguments @('-DotnetCandidatePath', $relativeDotnet)
    $capturedText.Add($probeRun.Text)
    Assert-True ($probeRun.ExitCode -eq 0) `
        'ValidateOnly rejected a relative fake dotnet candidate.'
    $result = $probeRun.Text | ConvertFrom-Json
    Assert-True ($result.Url -eq 'http://127.0.0.1:5287') 'ValidateOnly returned the wrong local URL.'
    Assert-True (
        $result.DotnetPath -eq [System.IO.Path]::GetFullPath($fakeDotnet)) `
        'A relative dotnet candidate was not normalized against the fake repository.'
    Assert-True (
        $result.ProjectPath -eq [System.IO.Path]::GetFullPath($fakeProject)) `
        'ValidateOnly did not return the exact fake web project path.'
    Assert-True (
        $result.PcmPath -eq [System.IO.Path]::GetFullPath($fakePcm)) `
        'ValidateOnly did not return the exact fake canonical full PCM path.'

    $missingKeyRun = Invoke-PowerShellFile -Path $fakeLauncher `
        -Arguments @(
            '-ValidateOnly',
            '-EnvPath', $missingEnv,
            '-DotnetCandidatePaths', $relativeDotnet)
    $capturedText.Add($missingKeyRun.Text)
    Assert-True ($missingKeyRun.ExitCode -ne 0) 'A missing fake key was not rejected.'
    Assert-True ($missingKeyRun.Text -match 'OPENAI_API_KEY') `
        'A missing fake key was not rejected safely.'

    $overrideRun = Invoke-PowerShellFile -Path $fakeLauncher `
        -Arguments @('-ValidateOnly', '-PcmPath', 'override.pcm')
    $capturedText.Add($overrideRun.Text)
    Assert-True ($overrideRun.ExitCode -ne 0) 'The copied launcher accepted an unsupported PCM override.'
    Assert-True ($overrideRun.Text -match 'PcmPath') `
        'The unsupported PCM override did not fail during parameter binding.'

    Set-FileLength -Path $fakePcm -Length ($expectedPcmLength - 1)
    $wrongLengthRun = Invoke-PowerShellFile -Path $fakeLauncher -Arguments @('-ValidateOnly')
    $capturedText.Add($wrongLengthRun.Text)
    Assert-True ($wrongLengthRun.ExitCode -ne 0) `
        'ValidateOnly accepted a canonical PCM with the wrong length.'
    Assert-True ($wrongLengthRun.Text -match 'PCM') `
        'The wrong-length canonical PCM was not rejected with a safe PCM error.'

    Remove-Item -LiteralPath $fakePcm
    $missingPcmRun = Invoke-PowerShellFile -Path $fakeLauncher -Arguments @('-ValidateOnly')
    $capturedText.Add($missingPcmRun.Text)
    Assert-True ($missingPcmRun.ExitCode -ne 0) 'ValidateOnly accepted a missing canonical PCM.'
    Assert-True ($missingPcmRun.Text -match 'PCM') `
        'The missing canonical PCM was not rejected with a safe PCM error.'

    $allCapturedText = $capturedText -join "`n"
    Assert-True ($allCapturedText -notmatch [regex]::Escape($sentinelKey)) `
        'The fake sentinel API key appeared in launcher or loader output/errors.'

    $launcherSource = Get-Content -LiteralPath $sourceLauncher -Raw
    Assert-True ($launcherSource -notmatch '\[string\]\$PcmPath') `
        'The canonical PCM path must not be user-overridable.'
    Assert-True (
        $launcherSource.Contains('data\poc-source-full\service-full-24k-mono-s16le.pcm')) `
        'The launcher does not name the canonical full PCM path.'

    $programSource = Get-Content -LiteralPath $program -Raw
    $programPatternOptions = (
        [System.Text.RegularExpressions.RegexOptions]::Singleline -bor
        [System.Text.RegularExpressions.RegexOptions]::IgnorePatternWhitespace)
    $validationPattern = @'
const\s+long\s+(?<lengthVariable>[A-Za-z_]\w*)\s*=\s*70_368_000\s*;\s*
var\s+(?<pathVariable>[A-Za-z_]\w*)\s*=\s*Path\.Combine\(\s*
repositoryRoot\s*,\s*"data"\s*,\s*"poc-source-full"\s*,\s*
"service-full-24k-mono-s16le\.pcm"\s*\)\s*;\s*
var\s+(?<fileVariable>[A-Za-z_]\w*)\s*=\s*new\s+FileInfo\(\s*\k<pathVariable>\s*\)\s*;\s*
if\s*\(\s*!\s*\k<fileVariable>\.Exists\s*\).*?
if\s*\(\s*\k<fileVariable>\.Length\s*!=\s*\k<lengthVariable>\s*\)
'@
    $validationMatch = [regex]::Match(
        $programSource,
        $validationPattern,
        $programPatternOptions)
    Assert-True ($validationMatch.Success) `
        'Program startup does not validate the exact canonical PCM path and 70,368,000-byte length.'
    Assert-True (
        $validationMatch.Index -lt $programSource.IndexOf('builder.WebHost.ConfigureKestrel')) `
        'Program validates the canonical PCM after Kestrel configuration.'

    $validatedPathVariable = [regex]::Escape($validationMatch.Groups['pathVariable'].Value)
    $routePattern = -join @(
        'app\.MapGet\("/test-assets/service\.pcm",\s*\(\)\s*=>\s*',
        'Results\.File\(\s*',
        $validatedPathVariable,
        '\s*,\s*contentType:\s*"application/octet-stream"\s*,\s*',
        'enableRangeProcessing:\s*true\s*\)\s*\)\s*;')
    Assert-True ([regex]::IsMatch($programSource, $routePattern, $programPatternOptions)) `
        'The range route does not serve the same canonical path validated during startup.'

    $readmeSource = Get-Content -LiteralPath $readme -Raw -Encoding utf8
    $webUiSectionMatch = [regex]::Match(
        $readmeSource,
        '(?ms)^## 4\..*?(?=^## 5\.)')
    Assert-True ($webUiSectionMatch.Success) `
        'The README does not contain the local web UI section.'
    $webUiReadmeSection = $webUiSectionMatch.Value
    $currentPlaybackText = -join @(
        [char]0xD604,
        [char]0xC7AC,
        [char]0x20,
        [char]0xC7AC,
        [char]0xC0DD,
        [char]0x20,
        [char]0xC704,
        [char]0xCE58)
    $twoLinesText = '2' + [char]0xC904

    Assert-True ($webUiReadmeSection.Contains($currentPlaybackText)) `
        'The README does not explicitly describe the current playback position.'
    Assert-True ($webUiReadmeSection.Contains($twoLinesText)) `
        'The README does not explicitly describe a two-line caption projection.'
    Assert-True ($webUiReadmeSection.Contains('HTTP Range')) `
        'The README does not explain that Start uses the current playback position.'
    Assert-True ($webUiReadmeSection.Contains('rolling projection')) `
        'The README does not explain the two-line rolling caption projection.'
    Assert-True ($webUiReadmeSection.Contains('docs/api-and-test-ui.md')) `
        'The README does not link the API-versus-test-UI contract document.'

    Assert-True (Test-Path -LiteralPath $apiAndTestUiDocument) `
        'The API-versus-test-UI contract document is missing.'
    $apiAndTestUiSource = Get-Content -LiteralPath $apiAndTestUiDocument -Raw -Encoding utf8
    $productionApiText = -join @(
        [char]0xC6B4,
        [char]0xC601,
        ' WebSocket API')
    $localTestUiText = -join @(
        [char]0xB85C,
        [char]0xCEEC,
        ' ',
        [char]0xD14C,
        [char]0xC2A4,
        [char]0xD2B8,
        ' UI')
    $noTwoLineLimitText = -join @(
        '2',
        [char]0xC904,
        ' ',
        [char]0xC81C,
        [char]0xD55C,
        ' ',
        [char]0xC5C6,
        [char]0xC74C)
    $topLeftText = -join @(
        [char]0xC67C,
        [char]0xCABD,
        ' ',
        [char]0xC704)
    $oneSpaceText = -join @(
        [char]0xACF5,
        [char]0xBC31,
        ' ',
        [char]0xD55C,
        ' ',
        [char]0xCE78)
    $topOneLineText = -join @(
        [char]0xB9E8,
        ' ',
        [char]0xC704,
        ' ',
        [char]0xD55C,
        ' ',
        [char]0xC904)
    $discreteCaptionUpdateText = -join @(
        [char]0xAC1C,
        [char]0xBCC4,
        ' CaptionUpdate')
    $staleTwoRowsText = -join @(
        [char]0xB450,
        ' ',
        [char]0xAC1C,
        [char]0xC758,
        ' ',
        [char]0xC548,
        [char]0xC815,
        [char]0xB41C,
        ' DOM ',
        [char]0xD589)
    foreach ($requiredBoundaryText in @(
        $productionApiText,
        $localTestUiText,
        'CaptionUpdate',
        $noTwoLineLimitText,
        $topLeftText,
        $oneSpaceText,
        $topOneLineText,
        $discreteCaptionUpdateText
    )) {
        Assert-True ($apiAndTestUiSource.Contains($requiredBoundaryText)) `
            "The API-versus-test-UI document is missing required boundary text: $requiredBoundaryText"
    }
    Assert-True (-not $apiAndTestUiSource.Contains($staleTwoRowsText)) `
        'The API-versus-test-UI document still describes two stable DOM rows.'
    foreach ($requiredFlowText in @(
        $topLeftText,
        $oneSpaceText,
        $topOneLineText,
        $discreteCaptionUpdateText
    )) {
        Assert-True ($webUiReadmeSection.Contains($requiredFlowText)) `
            "The README web UI section is missing inline-flow text: $requiredFlowText"
    }
    Assert-True (
        $readmeSource.Contains('data\poc-source-full\service-full-24k-mono-s16le.pcm')) `
        'The README does not name the canonical full PCM path.'
    foreach ($requiredProtocolText in @(
        '/ws/captions',
        '24 kHz',
        '16-bit',
        'mono',
        'OPENAI_API_KEY',
        'Ctrl+C'
    )) {
        Assert-True ($webUiReadmeSection.Contains($requiredProtocolText)) `
            "The README web UI section is missing required protocol or security text: $requiredProtocolText"
    }

    Assert-True ($webUiReadmeSection -notmatch '09:25|565\s*(?:seconds?|secs?|s|\uCD08)') `
        'The README still describes the removed fixed 09:25 UI start.'
    Assert-True (
        $webUiReadmeSection -notmatch '30\s*/\s*120\s*/\s*900' -and
        $webUiReadmeSection -notmatch '(?i)(?:30|120|900)\s*(?:seconds?|secs?|s|\uCD08)') `
        'The README still describes the removed UI duration selector.'

    foreach ($script in @($fakeLoader, $fakeLauncher)) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            $script,
            [ref]$tokens,
            [ref]$errors)
        Assert-True ($errors.Count -eq 0) "PowerShell parse error in $([System.IO.Path]::GetFileName($script))."
    }

    Write-Output 'PASS'
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
