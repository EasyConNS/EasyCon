"""Verify runtime packaging, license retention and user replacement protection."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import build
import prepare


class RuntimeArchiveTests(unittest.TestCase):
    def make_archive(self, root):
        output = root / "staged"
        output.mkdir()
        (output / "easycon_opencv.dll").write_bytes(b"bridge fixture")
        notice = output / "licenses/third-party/OPENCV_LICENSE"
        notice.parent.mkdir(parents=True)
        notice.write_bytes(b"license fixture")
        with patch.object(build, "ROOT", root), patch.object(build, "rid", return_value="win-x64"):
            build.archive(output, "official-release-fixture")
        return root / "archives/win-x64.zip"

    def extract(self, root):
        with patch.object(prepare, "ROOT", root), patch.object(prepare, "rid", return_value="win-x64"):
            with patch("sys.argv", ["prepare.py", "--rid", "win-x64"]):
                prepare.main()

    def test_archive_retains_notices_is_idempotent_and_preserves_replacements(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = self.make_archive(root)
            manifest = json.loads(archive.with_suffix(".json").read_text())
            self.assertIn("licenses/third-party/OPENCV_LICENSE", manifest["notices"])
            self.extract(root)
            self.extract(root)
            runtime = root / "runtimes/win-x64"
            self.assertEqual((runtime / "licenses/third-party/OPENCV_LICENSE").read_bytes(), b"license fixture")
            bridge = runtime / "easycon_opencv.dll"
            bridge.write_bytes(b"user replacement")
            with self.assertRaisesRegex(SystemExit, "Preserving user replacement"):
                self.extract(root)
            self.assertEqual(bridge.read_bytes(), b"user replacement")

    def test_invalid_archive_digest_is_rejected_before_extraction(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = self.make_archive(root)
            archive.write_bytes(b"invalid archive")
            with self.assertRaisesRegex(SystemExit, "checksum mismatch"):
                self.extract(root)
            self.assertFalse((root / "runtimes").exists())


if __name__ == "__main__":
    unittest.main()
