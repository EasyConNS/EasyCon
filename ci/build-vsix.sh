#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
pluginDir="$SCRIPT_DIR/../vscode-plugin"
buildDir="$pluginDir/.vsix-build"

cd "$pluginDir"

read_json() {
    python3 -c "
import json,sys
d=json.load(open('$1'))
k='$2'.split('.')
for x in k: d=d[x]
print(d)
"
}

id=$(read_json package.json name)
version=$(read_json package.json version)
displayName=$(read_json package.json displayName)
publisher=$(read_json package.json publisher)
description=$(read_json package.json description)
engine=$(read_json package.json engines.vscode)
distDir="$SCRIPT_DIR/../dist"
mkdir -p "$distDir"
outFile="$distDir/${id}-${version}.vsix"

echo "正在打包插件: $id v$version ..."

rm -rf "$buildDir"
mkdir -p "$buildDir"
extDir="$buildDir/extension"
mkdir -p "$extDir"

cat > "$buildDir/[Content_Types].xml" << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension=".json" ContentType="application/json" />
  <Default Extension=".js" ContentType="application/javascript" />
  <Default Extension=".xml" ContentType="application/xml" />
  <Default Extension=".ico" ContentType="image/x-icon" />
  <Default Extension=".md" ContentType="text/markdown" />
  <Default Extension=".vsixmanifest" ContentType="text/xml" />
</Types>
EOF

cat > "$buildDir/extension.vsixmanifest" << EOF
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
EOF

for f in package.json language-configuration.json README.md; do
    if [ -f "$pluginDir/$f" ]; then
        cp "$pluginDir/$f" "$extDir/$f"
    fi
done

if [ -f "$pluginDir/src/extension.js" ]; then
    mkdir -p "$extDir/src"
    cp "$pluginDir/src/extension.js" "$extDir/src/extension.js"
fi

for d in syntaxes samples images; do
    if [ -d "$pluginDir/$d" ]; then
        cp -r "$pluginDir/$d" "$extDir/$d"
    fi
done

rm -f "$outFile"

cd "$buildDir"
zip -X -r "$outFile" "[Content_Types].xml" extension.vsixmanifest extension/

cd "$pluginDir"
rm -rf "$buildDir"

echo ""
echo "打包完成: $outFile" 
echo "可通过 VS Code -> 扩展 -> ... -> 从 VSIX 安装 来安装此插件"