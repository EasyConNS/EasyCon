#!/usr/bin/env python3
"""Package a host, native runtime and optional workflow project (including models); never downloads."""
import argparse
from pathlib import Path
import shutil

from build import ROOT, rid

BRIDGES = {
    "win-x64": "easycon_opencv.dll", "win-arm64": "easycon_opencv.dll",
    "osx-x64": "libeasycon_opencv.dylib", "osx-arm64": "libeasycon_opencv.dylib",
    "linux-x64": "libeasycon_opencv.so", "linux-arm64": "libeasycon_opencv.so",
}


def package(executable, runtime, output, target, project=None):
    if target not in BRIDGES:
        raise ValueError("unsupported runtime identifier")
    if not executable.is_file():
        raise ValueError(f"Executable not found: {executable}")
    if not (runtime / BRIDGES[target]).is_file():
        raise ValueError(f"Prepared native bridge not found: {runtime / BRIDGES[target]}")
    if output.exists():
        raise ValueError(f"Output already exists; preserving existing files: {output}")
    resolved = output.resolve()
    if resolved.is_relative_to(runtime.resolve()):
        raise ValueError("Output must be outside the native runtime directory")
    if project is not None:
        if not (project / "workflow.json").is_file():
            raise ValueError(f"Workflow project not found: {project / 'workflow.json'}")
        if resolved.is_relative_to(project.resolve()):
            raise ValueError("Output must be outside the workflow project directory")
    output.mkdir(parents=True)
    try:
        shutil.copy2(executable, output / executable.name)
        shutil.copytree(runtime, output / "native" / target)
        if project is not None:
            shutil.copytree(project, output / "project")
    except BaseException:
        shutil.rmtree(output)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", required=True, type=Path)
    parser.add_argument("--runtime", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--project", type=Path, help="Copy a complete workflow project, including models/, into the package")
    parser.add_argument("--rid", default=rid(), choices=BRIDGES)
    args = parser.parse_args()
    runtime = args.runtime or ROOT / "runtimes" / args.rid
    try:
        package(args.executable, runtime, args.output, args.rid, args.project)
    except (ValueError, OSError) as error:
        parser.exit(1, f"{error}\n")
    print(f"Packaged executable, native runtime and selected project: {args.output}")


if __name__ == "__main__":
    main()
