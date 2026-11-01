from pathlib import Path
import tempfile
import unittest

import package


class HostPackageTests(unittest.TestCase):
    def test_copies_executable_native_dependencies_and_licenses_and_preserves_replacements(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / "easycon-run.exe"
            executable.write_bytes(b"executable fixture")
            runtime = root / "runtime"
            (runtime / "licenses").mkdir(parents=True)
            (runtime / "easycon_opencv.dll").write_bytes(b"bridge fixture")
            (runtime / "opencv_world500.dll").write_bytes(b"opencv fixture")
            (runtime / "licenses/OpenCV.txt").write_bytes(b"notice fixture")
            output = root / "dist"
            package.package(executable, runtime, output, "win-x64")
            self.assertEqual((output / executable.name).read_bytes(), b"executable fixture")
            self.assertEqual((output / "native/win-x64/opencv_world500.dll").read_bytes(), b"opencv fixture")
            self.assertEqual((output / "native/win-x64/licenses/OpenCV.txt").read_bytes(), b"notice fixture")
            with self.assertRaisesRegex(ValueError, "preserving"):
                package.package(executable, runtime, output, "win-x64")

    def test_workflow_models_and_resources_are_copied_without_rewriting_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / "easycon-run"
            executable.write_bytes(b"fixture")
            runtime = root / "runtime"
            runtime.mkdir()
            (runtime / "libeasycon_opencv.so").write_bytes(b"bridge")
            project = root / "workflow"
            (project / "models").mkdir(parents=True)
            (project / "resources").mkdir()
            workflow = b'{"ppocr":{"text":{"model":"models/ppocr.onnx","dictionary":"models/ppocr.txt"}}}'
            (project / "workflow.json").write_bytes(workflow)
            for name in ["ppocr.onnx", "ppocr.txt", "eng.traineddata"]:
                (project / "models" / name).write_bytes(name.encode())
            (project / "resources/input.png").write_bytes(b"image")
            output = root / "dist"
            package.package(executable, runtime, output, "linux-x64", project)
            self.assertEqual((output / "project/workflow.json").read_bytes(), workflow)
            for name in ["ppocr.onnx", "ppocr.txt", "eng.traineddata"]:
                self.assertEqual((output / "project/models" / name).read_bytes(), name.encode())
            self.assertEqual((output / "project/resources/input.png").read_bytes(), b"image")
            with self.assertRaisesRegex(ValueError, "outside the workflow"):
                package.package(executable, runtime, project / "dist", "linux-x64", project)
            self.assertFalse((project / "dist").exists())

    def test_missing_runtime_and_recursive_output_fail_before_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / "easycon-run"
            executable.write_bytes(b"fixture")
            runtime = root / "runtime"
            runtime.mkdir()
            with self.assertRaisesRegex(ValueError, "not found"):
                package.package(executable, runtime, root / "dist", "linux-x64")
            self.assertFalse((root / "dist").exists())
            (runtime / "libeasycon_opencv.so").write_bytes(b"fixture")
            with self.assertRaisesRegex(ValueError, "outside"):
                package.package(executable, runtime, runtime / "dist", "linux-x64")


if __name__ == "__main__":
    unittest.main()
