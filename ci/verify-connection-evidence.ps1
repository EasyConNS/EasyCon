param(
    [Parameter(Mandatory)][string]$RenderDirectory,
    [Parameter(Mandatory)][string]$TrxPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (![IO.Path]::IsPathFullyQualified($RenderDirectory) -or ![IO.Path]::IsPathFullyQualified($TrxPath)) {
    throw 'M18 render directory and TRX path must both be absolute'
}
if (!(Test-Path -LiteralPath $RenderDirectory -PathType Container)) {
    throw "M18 render directory is missing: $RenderDirectory"
}
$pngFiles = @(Get-ChildItem -LiteralPath $RenderDirectory -File -Filter '*.png')
$jsonFiles = @(Get-ChildItem -LiteralPath $RenderDirectory -File -Filter '*.json')
if ($pngFiles.Count -ne 52 -or $jsonFiles.Count -ne 4) {
    throw "M18 evidence is incomplete: expected 52 PNG and 4 JSON; found $($pngFiles.Count) PNG and $($jsonFiles.Count) JSON"
}
$expectedTestNames = @()
foreach ($layout in @('classic', 'card')) {
    foreach ($width in @(1200, 800)) {
        $prefix = "$layout-$width"
        $jsonPath = Join-Path $RenderDirectory "$prefix.json"
        $records = @(ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($jsonPath)))
        if ($records.Count -ne 13) { throw "M18 JSON must contain 12 states and one hover record: $jsonPath" }
        foreach ($record in $records) {
            if ($record.layout -ne $layout -or $record.width -ne $width) {
                throw "M18 JSON has mismatched layout/width: $jsonPath"
            }
        }
        $states = @($records | Where-Object { $_.PSObject.Properties.Name -contains 'image' })
        $expectedImages = @(
            foreach ($nameIndex in 0..2) {
                foreach ($inputConnected in @($false, $true)) {
                    foreach ($captureConnected in @($false, $true)) {
                        "$prefix-name$nameIndex-input$inputConnected-capture$captureConnected.png"
                    }
                }
            }
        )
        if ($states.Count -ne 12 -or
            [string]::Join('|', @($states.image | Sort-Object)) -cne [string]::Join('|', @($expectedImages | Sort-Object))) {
            throw "M18 JSON does not reference all 12 distinct state images: $jsonPath"
        }
        $hover = @($records | Where-Object { $_.PSObject.Properties.Name -contains 'hover' })
        if ($hover.Count -ne 1 -or $hover[0].hover -ne 'passed' -or $hover[0].duplicateSelection -ne 'passed' -or
            [string]::IsNullOrWhiteSpace($hover[0].fullName)) {
            throw "M18 hover/duplicate-selection evidence is incomplete: $jsonPath"
        }
        foreach ($imageName in @($expectedImages) + @("$prefix-hover.png")) {
            $imagePath = Join-Path $RenderDirectory $imageName
            $bytes = [IO.File]::ReadAllBytes($imagePath)
            if ($bytes.Length -le 8 -or [Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A') {
                throw "M18 PNG is empty or invalid: $imagePath"
            }
        }
        $expectedTestNames += 'ConnectionPanels_RenderShortStatesAndStableWidths_AndExposeFullEndpointNames("{0}",{1})' -f $layout, $width
    }
}

[xml]$trx = [IO.File]::ReadAllText($TrxPath)
$summary = $trx.TestRun.ResultSummary
$counters = $summary.Counters
$results = @($trx.TestRun.Results.UnitTestResult)
if ($summary.outcome -notin @('Completed', 'Passed') -or [int]$counters.total -ne 4 -or
    [int]$counters.executed -ne 4 -or [int]$counters.passed -ne 4 -or [int]$counters.failed -ne 0 -or
    [int]$counters.notExecuted -ne 0 -or $results.Count -ne 4 -or
    @($results | Where-Object outcome -ne 'Passed').Count -ne 0 -or
    [string]::Join('|', @($results.testName | Sort-Object)) -cne [string]::Join('|', @($expectedTestNames | Sort-Object))) {
    throw "M18 TRX must report all four layout/width cases passed with a successful run outcome: $TrxPath"
}
Write-Host "M18 evidence verified: 52 PNG, 4 JSON, 4 passed tests; render directory: $RenderDirectory"