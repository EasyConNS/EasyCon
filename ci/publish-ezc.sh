projname="EasyCon2.CLI"

detect_os() {
    case "$(uname -s)" in
        Linux*) echo "linux" ;;
        Darwin*) echo "osx" ;;
        *) echo "linux" ;;
    esac
}

detect_arch() {
    case "$(uname -m)" in
        arm64|aarch64) echo "arm64" ;;
        x86_64|amd64) echo "x64" ;;
        *) echo "x64" ;;
    esac
}

detect_sdk() {
    if dotnet --list-sdks | grep -q "^10\\.0"; then
        echo "net10.0"
    elif dotnet --list-sdks | grep -q "^9\\.0"; then
        echo "net9.0"
    elif dotnet --list-sdks | grep -q "^8\\.0"; then
        echo "net8.0"
    else
        echo "net10.0"
    fi
}

os=$(detect_os)
arch=$(detect_arch)
runtime="${os}-${arch}"
net_sdk=$(detect_sdk)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_DIR="$SCRIPT_DIR/../src"
DIST_DIR="$SCRIPT_DIR/../dist"

cd "$SRC_DIR"

rm -fr publish
cd ${projname}
rm -fr bin
rm -fr obj

dotnet publish ${projname}.csproj -c Release -r ${runtime} -f ${net_sdk} -p:PublishSingleFile=true --self-contained -o ../publish

cd ../publish
mv ${projname} ezcon
cd ..

mkdir -p "$DIST_DIR"
rsync -a --remove-source-files ./publish/ "$DIST_DIR/publish/"
rm -rf ./publish