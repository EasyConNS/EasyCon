param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$models = Join-Path $repository '.ci-maa-ocr'
dotnet run --project (Join-Path $repository 'src/EasyCon2.CLI') -c $Configuration -- maa models install $models
if ($LASTEXITCODE -ne 0) { throw 'Maa OCR model installation failed' }
$profile = Join-Path $models 'ocr-profile.json'
$env:EASYCON_TEST_MAA_OCR_PROFILE = $profile
$env:EASYCON_MAA_REQUIRE_OCR_TESTS = '1'
if ($env:GITHUB_ENV) {
    "EASYCON_TEST_MAA_OCR_PROFILE=$profile" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
    'EASYCON_MAA_REQUIRE_OCR_TESTS=1' | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
}
