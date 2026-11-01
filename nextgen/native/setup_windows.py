#!/usr/bin/env python3
"""Prepare an SHA-256 verified official Windows OpenCV SDK; optionally build our bridge."""
import argparse
import hashlib
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parent
VERSION = "5.0.0"
RELEASE = f"https://github.com/opencv/opencv/releases/download/{VERSION}"
ASSETS = {
    "x64": (f"opencv-{VERSION}-windows.exe",
            "9c6c1fcea58acdf06edba13148b2246e00c2658143fa51e61ecd370db8c39f63"),
    "arm64": (f"opencv-{VERSION}-windows-arm64.exe",
              "087bd5cc1f2d6d44f71ec941babbc49e0426fb81c4ecd59ab7721890c852b8a6"),
}


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def download(cache, arch):
    name, digest = ASSETS[arch]
    path = cache / name
    if not path.exists():
        temporary = path.with_suffix(".download")
        try:
            print(f"Downloading official SDK: {RELEASE}/{name}", flush=True)
            with urllib.request.urlopen(f"{RELEASE}/{name}", timeout=60) as source:
                with temporary.open("wb") as target:
                    shutil.copyfileobj(source, target)
            if sha256(temporary) != digest:
                raise SystemExit("Official OpenCV SDK checksum mismatch")
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)
    if sha256(path) != digest:
        raise SystemExit(f"Official OpenCV SDK checksum mismatch: {path}; remove the invalid cache file")
    return path


def sdk_paths(sdk, arch):
    config = sdk / "opencv" / "build"
    if not (config / "OpenCVConfig.cmake").is_file():
        raise SystemExit(f"Missing official SDK CMake configuration: {config}")
    # Discover the compiler directory instead of assuming vc16/vc17 in future SDKs.
    bins = {p.parent for p in config.rglob("opencv_world*.dll")
            if not p.stem.endswith("d") and arch in [s.lower() for s in p.parts]}
    if len(bins) != 1:
        raise SystemExit(f"Expected one {arch} release DLL directory in {config}, found {len(bins)}")
    return config, bins.pop()


def prepare(cache, arch, extractor):
    cache.mkdir(parents=True, exist_ok=True)
    asset = download(cache, arch)
    sdk = cache / f"{VERSION}-windows-{arch}"
    # Preserve an existing SDK, including user modifications; always revalidate the downloaded asset.
    if sdk.exists():
        return sdk_paths(sdk, arch)
    with tempfile.TemporaryDirectory(prefix="opencv-sdk-", dir=cache) as temporary:
        staged = Path(temporary)
        subprocess.run([extractor, "x", str(asset), f"-o{staged}", "-y"], check=True)
        sdk_paths(staged, arch)
        staged.rename(sdk)
    return sdk_paths(sdk, arch)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--arch", choices=ASSETS, default="x64")
    parser.add_argument("--cache", type=Path, default=ROOT.parent / ".deps" / "opencv")
    parser.add_argument("--build", action="store_true", help="build and archive our bridge on the matching Windows host")
    args = parser.parse_args()
    host_arch = {"amd64": "x64", "x86_64": "x64", "arm64": "arm64", "aarch64": "arm64"}.get(platform.machine().lower())
    if args.build and (platform.system() != "Windows" or host_arch != args.arch):
        parser.error("--build requires a Windows host matching --arch, with MSVC and CMake")
    extractor = next((path for name in ("7z", "7zz") if (path := shutil.which(name))), None)
    if not extractor:
        parser.error("7-Zip (7z or 7zz) is required to extract the official SDK")
    config, libraries = prepare(args.cache.resolve(), args.arch, extractor)
    print(f"OpenCV_DIR={config}\nLibrary directory={libraries}")
    if args.build:
        subprocess.run([sys.executable, str(ROOT / "build.py"), "--opencv-dir", str(config),
                        "--library-dir", str(libraries), "--license-file", str(config / "LICENSE"),
                        "--notices-dir", str(config / "etc" / "licenses"),
                        "--archive", "--source",
                        f"{RELEASE}/{ASSETS[args.arch][0]}; sha256:{ASSETS[args.arch][1]}"], check=True)


if __name__ == "__main__":
    main()
