"""Fetch or verify the pinned FRLG OCR resources."""
from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
REVISIONS = {
    "EasyConNS/EasyCon": "0718a15b484d7f2a70c43b5f0c3c670f07e2f23a",
    "PokemonAutomation/Arduino-Source": "a772133ebc497aed05439f464a6222d3d83e0c10",
    "PokemonAutomation/Packages": "e8cc29cdc9e9c16faf406a1d154d70ad687b375c",
    "PokemonAutomation/CommandLineTests": "46b892bd7f2106a1f34de11aa300b492aee06b83",
    "PaddlePaddle/PaddleOCR": "2661c7c0ef5c613e8f93c6e93b2e052399f0f854",
    "tesseract-ocr/tessdata": "ced78752cc61322fb554c280d13360b35b8684e4",
}
LOCK = ROOT / "docs/frlg-ocr-resources.lock.json"


def entries():
    yield "PaddlePaddle/PaddleOCR", "LICENSE", "docs/licenses/PaddleOCR-Apache-2.0.txt"
    yield "tesseract-ocr/tessdata", "LICENSE", "docs/licenses/TesseractData-Apache-2.0.txt"
    for family in ("Digits", "LevelDigits", "DialogDigits"):
        for digit in range(10):
            yield "PokemonAutomation/Packages", f"Resources/PokemonFRLG/{family}/{digit}.png", f"src/EasyCon.Capture/Ocr/Frlg/Resources/{family}/{digit}.png"
    yield "PokemonAutomation/Arduino-Source", "LICENSE", "docs/licenses/PokemonAutomation-MIT.txt"
    for name in ("nyash_jpn_45345.png", "tom_eng_60895.jpg"):
        yield "PokemonAutomation/CommandLineTests", f"PokemonFRLG/TrainerIdReader/{name}", f"test/EasyCon.Tests/TestData/Frlg/{name}"
    for source, name in (
        ("Pokemon/PokemonNameDisplay.json", "PokemonNameDisplay.json"),
        ("Pokemon/PokemonNameOCR/PokemonOCR-jpn.json", "PokemonOCR-jpn.json"),
        ("PokemonFRLG/NatureCheckerOCR.json", "NatureCheckerOCR.json"),
        ("OCR/CharacterReductions.json", "CharacterReductions.json"),
    ):
        yield "PokemonAutomation/Packages", f"Resources/{source}", f"src/EasyCon.Capture/Ocr/Frlg/Resources/Text/{name}"
    for source, name in (
        ("PaddleOCR/chinese/rec.onnx", "chinese/rec.onnx"),
        ("PaddleOCR/chinese/dict.txt", "chinese/dict.txt"),
        ("PaddleOCR/chinese/config.json", "chinese/config.json"),
        ("PaddleOCR/README.md", "PaddleOCR-README.md"),
        ("Tesseract/jpn.traineddata", "tessdata/jpn.traineddata"),
        ("Tesseract/README.md", "Tesseract-README.md"),
    ):
        yield "PokemonAutomation/Packages", f"Resources/{source}", f"models/frlg/{name}"
    for page, name in (("Page1", "bulbasaur_1_jpn"), ("Page1", "deoxys_1_jpn"), ("Page2", "deoxys_1_jpn")):
        for filename in (name + ".png", "_" + name + ".txt"):
            yield "PokemonAutomation/CommandLineTests", f"PokemonFRLG/StatsReader/{page}/{filename}", f"test/EasyCon.Tests/TestData/Frlg/{page}/{filename}"
    for name in ("eng_dragonair.jpg", "eng_chansey.jpg"):
        yield "PokemonAutomation/CommandLineTests", f"PokemonFRLG/WildEncounterReader/{name}", f"test/EasyCon.Tests/TestData/Frlg/Wild/{name}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="Verify existing files without network access")
    parser.add_argument("--models-only", action="store_true", help="Fetch only the optional runtime models")
    args = parser.parse_args()
    existing = json.loads(LOCK.read_text(encoding="utf-8")) if LOCK.exists() else None
    expected = {item["path"]: item["sha256"] for item in existing["files"]} if existing else {}

    def fetch(entry):
        repo, source, destination = entry
        url = f"https://raw.githubusercontent.com/{repo}/{REVISIONS[repo]}/{source}"
        target = ROOT / destination
        if args.verify:
            if destination not in expected:
                raise ValueError(f"Missing resource lock: {destination}")
            data = target.read_bytes()
        elif target.exists() and destination in expected:
            data = target.read_bytes()
        else:
            with urllib.request.urlopen(url, timeout=180) as response:
                data = response.read()
        digest = hashlib.sha256(data).hexdigest()
        if destination in expected and expected[destination] != digest:
            raise ValueError(f"Resource hash mismatch: {destination}")
        if not args.verify:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        return {"path": destination, "source": url, "sha256": digest, "bytes": len(data)}

    selected = [entry for entry in entries() if not args.models_only or entry[2].startswith("models/frlg/")]
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        files = list(pool.map(fetch, selected))
    if not args.verify and not args.models_only:
        LOCK.parent.mkdir(parents=True, exist_ok=True)
        LOCK.write_text(json.dumps({"revisions": REVISIONS, "files": files}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {len(files)} pinned resources.")


if __name__ == "__main__":
    main()
