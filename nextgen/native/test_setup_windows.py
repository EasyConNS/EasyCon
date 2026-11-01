"""Offline checks for official SDK preparation; no native toolchain or downloads needed."""
import hashlib
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import setup_windows as setup


class OfficialSdkTests(unittest.TestCase):
    def test_cached_asset_is_verified_and_corruption_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            asset = cache / "official.exe"
            asset.write_bytes(b"official binary")
            digest = hashlib.sha256(asset.read_bytes()).hexdigest()
            with patch.dict(setup.ASSETS, {"test": (asset.name, digest)}):
                self.assertEqual(setup.download(cache, "test"), asset)
                asset.write_bytes(b"corrupted binary")
                with self.assertRaisesRegex(SystemExit, "checksum mismatch"):
                    setup.download(cache, "test")

    def test_download_rejects_bad_digest_without_publishing_partial_asset(self):
        from io import BytesIO
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            with patch.dict(setup.ASSETS, {"test": ("official.exe", "0" * 64)}):
                with patch.object(setup.urllib.request, "urlopen", return_value=BytesIO(b"bad")):
                    with self.assertRaisesRegex(SystemExit, "checksum mismatch"):
                        setup.download(cache, "test")
            self.assertEqual(list(cache.iterdir()), [])

    def make_sdk(self, root):
        config = root / "opencv/build"
        config.mkdir(parents=True)
        (config / "OpenCVConfig.cmake").touch()
        libraries = config / "x64/vc17/bin"
        libraries.mkdir(parents=True)
        (libraries / "opencv_world500.dll").write_bytes(b"release")
        (libraries / "opencv_world500d.dll").touch()
        return config, libraries

    def test_discovers_compiler_directory_and_rejects_wrong_architecture_or_ambiguity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaisesRegex(SystemExit, "Missing official SDK"):
                setup.sdk_paths(root, "x64")
            expected = self.make_sdk(root)
            self.assertEqual(setup.sdk_paths(root, "x64"), expected)
            with self.assertRaisesRegex(SystemExit, "found 0"):
                setup.sdk_paths(root, "arm64")
            other = root / "opencv/build/x64/vc18/bin"
            other.mkdir(parents=True)
            (other / "opencv_world500.dll").touch()
            with self.assertRaisesRegex(SystemExit, "found 2"):
                setup.sdk_paths(root, "x64")

    def test_existing_sdk_is_preserved_without_extraction(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            sdk = cache / f"{setup.VERSION}-windows-x64"
            expected = self.make_sdk(sdk)
            with patch.object(setup, "download", return_value=cache / "official.exe") as download:
                with patch.object(setup.subprocess, "run") as extract:
                    self.assertEqual(setup.prepare(cache, "x64", "7z"), expected)
                    download.assert_called_once_with(cache, "x64")
                    extract.assert_not_called()
            self.assertEqual((expected[1] / "opencv_world500.dll").read_bytes(), b"release")

    def test_extracts_into_staging_then_publishes_complete_sdk(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            def extract(command, check):
                self.assertTrue(check)
                staged = Path(next(arg[2:] for arg in command if arg.startswith("-o")))
                self.make_sdk(staged)
            with patch.object(setup, "download", return_value=cache / "official.exe"):
                with patch.object(setup.subprocess, "run", side_effect=extract):
                    config, libraries = setup.prepare(cache, "x64", "7z")
            self.assertTrue((config / "OpenCVConfig.cmake").is_file())
            self.assertEqual((libraries / "opencv_world500.dll").read_bytes(), b"release")
            self.assertEqual([p.name for p in cache.iterdir()], [f"{setup.VERSION}-windows-x64"])


if __name__ == "__main__":
    unittest.main()
