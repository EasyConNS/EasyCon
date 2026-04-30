# build-vsix.ps1 - 生成 VS Code 插件 .vsix 安装包（无需 npm）
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$pluginDir = Join-Path -Path $PSScriptRoot -ChildPath "../vscode-plugin"
$buildDir = Join-Path $pluginDir ".vsix-build"

# 读取 package.json 获取插件信息
$jsonContent = Get-Content (Join-Path $pluginDir "package.json") -Raw -Encoding UTF8
$pkg = $jsonContent | ConvertFrom-Json
$id = $pkg.name
$version = $pkg.version
$displayName = $pkg.displayName
$publisher = $pkg.publisher
$description = $pkg.description
$engine = $pkg.engines.vscode

$distDir = Join-Path -Path $PSScriptRoot -ChildPath "../dist"
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
$outFile = Join-Path $distDir "${id}-${version}.vsix"

Write-Host "正在打包插件: $id v$version ..."

# 清理并创建构建目录
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
New-Item -ItemType Directory -Path $buildDir | Out-Null
$extDir = Join-Path $buildDir "extension"
New-Item -ItemType Directory -Path $extDir | Out-Null

# 1. 创建 [Content_Types].xml
$ctXml = @'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension=".json" ContentType="application/json" />
  <Default Extension=".js" ContentType="application/javascript" />
  <Default Extension=".xml" ContentType="application/xml" />
  <Default Extension=".ico" ContentType="image/x-icon" />
  <Default Extension=".md" ContentType="text/markdown" />
  <Default Extension=".vsixmanifest" ContentType="text/xml" />
</Types>
'@
[IO.File]::WriteAllText((Join-Path $buildDir "[Content_Types].xml"), $ctXml, [Text.UTF8Encoding]::new($false))

# 2. 创建 extension.vsixmanifest
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011" xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
  <Metadata>
    <Identity Language="en-US" Id="$id" Version="$version" Publisher="$publisher" />
    <DisplayName>$displayName</DisplayName>
    <Description xml:space="preserve">$description</Description>
    <Tags>easycon,ecs</Tags>
    <Categories>Programming Languages</Categories>
    <GalleryFlags>Public</GalleryFlags>
    <Properties>
      <Property Id="Microsoft.VisualStudio.Code.Engine" Value="$engine" />
      <Property Id="Microsoft.VisualStudio.Code.ExtensionDependencies" Value="" />
      <Property Id="Microsoft.VisualStudio.Code.ExtensionPack" Value="" />
      <Property Id="Microsoft.VisualStudio.Code.ExtensionKind" Value="workspace" />
      <Property Id="Microsoft.VisualStudio.Code.LocalizedLanguages" Value="" />
    </Properties>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Code" />
  </Installation>
  <Dependencies />
  <Assets>
    <Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" />
  </Assets>
</PackageManifest>
"@
[IO.File]::WriteAllText((Join-Path $buildDir "extension.vsixmanifest"), $manifest, [Text.UTF8Encoding]::new($false))

# 3. 复制插件文件到 extension/ 目录
$filesToCopy = @("package.json", "language-configuration.json", "README.md")
foreach ($f in $filesToCopy) {
    $src = Join-Path $pluginDir $f
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $extDir $f) -Force
    }
}

# 复制 src/ 目录
$srcDir = Join-Path $pluginDir "src"
if (Test-Path $srcDir) {
    $destSrcDir = Join-Path $extDir "src"
    New-Item -ItemType Directory -Path $destSrcDir -Force | Out-Null
    Copy-Item (Join-Path $srcDir "extension.js") (Join-Path $destSrcDir "extension.js") -Force
}

# 复制子目录
foreach ($d in @("syntaxes", "samples", "images")) {
    $src = Join-Path $pluginDir $d
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $extDir $d) -Recurse -Force
    }
}

# 复制 node_modules（vscode-languageclient 等）
$nmSrc = Join-Path $pluginDir "node_modules"
if (Test-Path $nmSrc) {
    $destNm = Join-Path $extDir "node_modules"
    Copy-Item $nmSrc $destNm -Recurse -Force
}

# 4. 打包为 .vsix（ZIP 格式）
if (Test-Path $outFile) { Remove-Item $outFile -Force }

# Compress-Archive 只支持 .zip 扩展名，先输出 zip 再重命名
$tempZip = Join-Path $distDir "${id}-${version}.zip"
if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
$items = @(Get-ChildItem $buildDir | ForEach-Object { $_.FullName })
Compress-Archive -LiteralPath $items -DestinationPath $tempZip -CompressionLevel Optimal
Move-Item $tempZip $outFile -Force

# 5. 清理构建目录
Remove-Item $buildDir -Recurse -Force

Write-Host ""
Write-Host "打包完成: $outFile" -ForegroundColor Green
Write-Host "可通过 VS Code -> 扩展 -> ... -> 从 VSIX 安装 来安装此插件" -ForegroundColor Cyan
