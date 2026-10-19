$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$verifier = Join-Path $PSScriptRoot 'verify-connection-evidence.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $tempRoot ('easycon-m18-evidence-' + [Guid]::NewGuid().ToString('N'))
$renderDirectory = Join-Path $fixtureRoot 'render'
$trxPath = Join-Path $fixtureRoot 'connection-m18.trx'

function Assert-Rejected([string]$Name, [string]$Directory = $renderDirectory, [string]$Results = $trxPath) {
    try { & $verifier -RenderDirectory $Directory -TrxPath $Results }
    catch { Write-Host "PASS rejects $Name"; return }
    throw "M18 evidence verifier incorrectly accepted: $Name"
}

try {
    [IO.Directory]::CreateDirectory($renderDirectory) | Out-Null
    $pixel = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=')
    $testResults = @()
    foreach ($layout in @('classic', 'card')) {
        foreach ($width in @(1200, 800)) {
            $prefix = "$layout-$width"
            $records = @(
                foreach ($nameIndex in 0..2) {
                    foreach ($inputConnected in @($false, $true)) {
                        foreach ($captureConnected in @($false, $true)) {
                            $image = "$prefix-name$nameIndex-input$inputConnected-capture$captureConnected.png"
                            [IO.File]::WriteAllBytes((Join-Path $renderDirectory $image), $pixel)
                            @{ layout = $layout; width = $width; image = $image }
                        }
                    }
                }
                @{ layout = $layout; width = $width; hover = 'passed'; duplicateSelection = 'passed'; fullName = 'USB Video #1' }
            )
            [IO.File]::WriteAllBytes((Join-Path $renderDirectory "$prefix-hover.png"), $pixel)
            $records | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $renderDirectory "$prefix.json") -Encoding utf8
            $testName = [Security.SecurityElement]::Escape(('ConnectionPanels_RenderShortStatesAndStableWidths_AndExposeFullEndpointNames("{0}",{1})' -f $layout, $width))
            $testResults += "<UnitTestResult testName=`"$testName`" outcome=`"Passed`" />"
        }
    }
    $trxText = '<TestRun><Results>' + ($testResults -join '') + '</Results><ResultSummary outcome="Completed"><Counters total="4" executed="4" passed="4" failed="0" notExecuted="0" /></ResultSummary></TestRun>'
    [IO.File]::WriteAllText($trxPath, $trxText)
    & $verifier -RenderDirectory $renderDirectory -TrxPath $trxPath
    Write-Host 'PASS complete evidence'

    Assert-Rejected 'relative render directory' -Directory 'render'
    Assert-Rejected 'relative TRX path' -Results 'connection-m18.trx'
    $emptyDirectory = Join-Path $fixtureRoot 'trx-only'
    [IO.Directory]::CreateDirectory($emptyDirectory) | Out-Null
    Assert-Rejected 'TRX without screenshots/JSON (F6)' -Directory $emptyDirectory

    $pngPath = Join-Path $renderDirectory 'classic-800-hover.png'
    [IO.File]::Move($pngPath, "$pngPath.missing")
    Assert-Rejected 'one missing PNG'
    [IO.File]::Move("$pngPath.missing", $pngPath)
    [IO.File]::WriteAllBytes($pngPath, [byte[]]::new(16))
    Assert-Rejected 'invalid PNG with correct file count'
    [IO.File]::WriteAllBytes($pngPath, $pixel)
    [IO.File]::Move($pngPath, (Join-Path $renderDirectory 'unexpected.png'))
    Assert-Rejected 'wrong PNG name with correct file count'
    [IO.File]::Move((Join-Path $renderDirectory 'unexpected.png'), $pngPath)

    $jsonPath = Join-Path $renderDirectory 'classic-800.json'
    $jsonText = [IO.File]::ReadAllText($jsonPath)
    [IO.File]::Move($jsonPath, "$jsonPath.missing")
    Assert-Rejected 'one missing JSON'
    [IO.File]::Move("$jsonPath.missing", $jsonPath)
    [IO.File]::WriteAllText($jsonPath, '[]')
    Assert-Rejected 'incomplete state/hover records'
    [IO.File]::WriteAllText($jsonPath, $jsonText.Replace('passed', 'failed'))
    Assert-Rejected 'failed hover/duplicate selection'
    [IO.File]::WriteAllText($jsonPath, $jsonText)

    [IO.File]::Move($trxPath, "$trxPath.missing")
    Assert-Rejected 'missing TRX'
    [IO.File]::Move("$trxPath.missing", $trxPath)
    [IO.File]::WriteAllText($trxPath, $trxText.Replace('outcome="Completed"', 'outcome="Failed"'))
    Assert-Rejected 'failed test run with four passed results'
    [IO.File]::WriteAllText($trxPath, $trxText.Replace('passed="4"', 'passed="3"'))
    Assert-Rejected 'fewer than four passed cases'
    [IO.File]::WriteAllText($trxPath, $trxText.Replace('classic&quot;,800', 'classic&quot;,801'))
    Assert-Rejected 'wrong layout/width test case'
    [IO.File]::WriteAllText($trxPath, $trxText)
    & $verifier -RenderDirectory $renderDirectory -TrxPath $trxPath
    Write-Host 'PASS evidence verifier regression: complete evidence accepted; 13 incomplete/invalid cases rejected'
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $tempPrefix = $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notlike 'easycon-m18-evidence-*') {
        throw "Refusing to clean an unexpected fixture path: $resolvedFixture"
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}