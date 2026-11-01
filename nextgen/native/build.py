#!/usr/bin/env python3
"""Build only our C ABI bridge against a supplied native OpenCV SDK; never build OpenCV."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parent


def rid():
    machine = platform.machine().lower()
    arch = {"arm64": "arm64", "aarch64": "arm64", "amd64": "x64", "x86_64": "x64"}.get(machine)
    system = {"Windows": "win", "Darwin": "osx", "Linux": "linux"}.get(platform.system())
    if not arch or not system:
        raise SystemExit(f"Unsupported platform: {platform.system()} {machine}")
    return f"{system}-{arch}"


def run(*args):
    subprocess.run([str(arg) for arg in args], check=True)


def dependencies(path):
    text = subprocess.check_output(["otool", "-L", str(path)], text=True)
    return [line.strip().split(" (compatibility", 1)[0] for line in text.splitlines()[1:]]


def relocate_macos(output):
    # Relocate every copied dependency, not just our bridge, to remove SDK-machine paths.
    libraries = sorted(output.glob("*.dylib"))
    aliases = {p.name: p.resolve().name for p in libraries}
    for library in (p for p in libraries if not p.is_symlink()):
        library.chmod(0o755)
        changes = []
        for dependency in dependencies(library):
            target = output / aliases.get(Path(dependency).name, Path(dependency).name)
            if target.exists():
                changes.extend(["-change", dependency, f"@loader_path/{target.name}"])
        run("install_name_tool", "-id", f"@rpath/{library.name}", *changes, library)
        # Remove SDK-specific rpaths; all packaged non-system dependencies are siblings.
        text = subprocess.check_output(["otool", "-l", str(library)], text=True).splitlines()
        for i, line in enumerate(text):
            if line.strip() == "cmd LC_RPATH":
                old = text[i + 2].strip().split(" (offset", 1)[0].removeprefix("path ")
                if old not in ("@loader_path", "@executable_path"):
                    run("install_name_tool", "-delete_rpath", old, library)
        run("codesign", "--force", "--sign", "-", library)
    for alias in libraries:
        if alias.is_symlink():
            alias.unlink()


def relocate_linux(output):
    # DT_RUNPATH is not transitive: each non-system dependency needs its own sibling lookup.
    for library in output.iterdir():
        if library.is_file() and not library.is_symlink() and ".so" in library.name:
            run("patchelf", "--set-rpath", "$ORIGIN", library)


def archive(output, source):
    archive_dir = ROOT / "archives"
    archive_dir.mkdir(exist_ok=True)
    path = archive_dir / f"{rid()}.zip"
    files = sorted(p for p in output.iterdir() if p.is_file())
    notices = sorted((output / "licenses").rglob("*")) if (output / "licenses").exists() else []
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        for file in files:
            bundle.write(file, file.name)
        for notice in notices:
            if notice.is_file():
                bundle.write(notice, "licenses/" + notice.relative_to(output / "licenses").as_posix())
    manifest = {
        "format_version": 1, "rid": rid(), "source": source,
        "archive_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "files": {file.name: hashlib.sha256(file.read_bytes()).hexdigest() for file in files},
        "notices": {"licenses/" + file.relative_to(output / "licenses").as_posix(): hashlib.sha256(file.read_bytes()).hexdigest()
                    for file in notices if file.is_file()},
    }
    path.with_suffix(".json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Archived {path} ({path.stat().st_size} bytes)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--opencv-dir", required=True, type=Path, help="directory containing OpenCVConfig.cmake")
    parser.add_argument("--library-dir", type=Path, help="native shared-library directory, especially Windows SDK bin")
    parser.add_argument("--output", type=Path, default=ROOT / "runtimes" / rid())
    parser.add_argument("--archive", action="store_true", help="save the prepared runtime as an in-project zip dependency")
    parser.add_argument("--source", help="required provenance description when creating an archive")
    parser.add_argument("--notices-dir", type=Path, help="SDK directory with third-party license notices")
    parser.add_argument("--license-file", type=Path, help="OpenCV SDK license")
    args = parser.parse_args()
    if args.archive and (not args.source or not args.notices_dir):
        parser.error("--archive requires --source and --notices-dir")
    if args.license_file and not args.license_file.is_file():
        parser.error(f"license file does not exist: {args.license_file}")
    if args.notices_dir and not args.notices_dir.is_dir():
        parser.error(f"notices directory does not exist: {args.notices_dir}")
    output = args.output.resolve()
    if output.exists() and any(output.iterdir()):
        parser.error(f"output is not empty: {output}; select a new directory to preserve existing runtimes")
    build = ROOT / ".build" / rid()
    run("cmake", "-S", ROOT / "bridge", "-B", build,
        f"-DOpenCV_DIR={args.opencv_dir.resolve()}", "-DCMAKE_BUILD_TYPE=Release",
        f"-DEASYCON_OPENCV_RUNTIME_DIR={args.library_dir.resolve() if args.library_dir else ''}",
        f"-DCMAKE_INSTALL_PREFIX={output}")
    run("cmake", "--build", build, "--config", "Release", "--parallel")
    run("cmake", "--install", build, "--config", "Release")
    if platform.system() == "Darwin":
        relocate_macos(output)
    elif platform.system() == "Linux":
        relocate_linux(output)
    if args.notices_dir:
        shutil.copytree(args.notices_dir, output / "licenses" / "third-party")
    if args.license_file:
        license_path = output / "licenses" / "opencv-LICENSE"
        license_path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(args.license_file, license_path)
    if args.archive:
        archive(output, args.source)
    print(f"Runtime ready: {output}")


if __name__ == "__main__":
    main()
