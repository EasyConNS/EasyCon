#!/usr/bin/env python3
"""Extract and verify the checked-in runtime for this platform, without downloading anything."""
import argparse
import hashlib
import json
from pathlib import Path
import tempfile
import zipfile
from build import ROOT, rid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", default=rid())
    args = parser.parse_args()
    if args.rid not in ("win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64"):
        parser.error("unsupported runtime identifier")
    archive = ROOT / "archives" / f"{args.rid}.zip"
    if not archive.is_file() or not archive.with_suffix(".json").is_file():
        raise SystemExit(f"No bundled native runtime for {args.rid}; supply a native OpenCV >= 5 SDK and use native/build.py")
    manifest = json.loads(archive.with_suffix(".json").read_text())
    if manifest["format_version"] != 1 or manifest["rid"] != args.rid:
        raise SystemExit("Invalid runtime manifest")
    if hashlib.sha256(archive.read_bytes()).hexdigest() != manifest["archive_sha256"]:
        raise SystemExit("Runtime archive checksum mismatch")
    output = ROOT / "runtimes" / args.rid
    output.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as bundle:
        entries = {**manifest["files"], **manifest.get("notices", {})}
        if set(bundle.namelist()) != set(entries):
            raise SystemExit("Runtime archive file list mismatch")
        for name, digest in entries.items():
            if name.startswith("/") or ".." in Path(name).parts or "\\" in name or ":" in name:
                raise SystemExit("Invalid runtime filename")
            target = output / name
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.exists():
                if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
                    raise SystemExit(f"Preserving user replacement: {target}; choose a separate EASYCON_OPENCV_DIR")
                continue
            data = bundle.read(name)
            if hashlib.sha256(data).hexdigest() != digest:
                raise SystemExit(f"Runtime checksum mismatch: {name}")
            with tempfile.NamedTemporaryFile(dir=output, delete=False) as file:
                temporary = Path(file.name)
                file.write(data)
            temporary.chmod(0o755)
            temporary.replace(target)
    print(f"Runtime verified: {output}")


if __name__ == "__main__":
    main()
