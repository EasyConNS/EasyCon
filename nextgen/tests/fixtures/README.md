# OCR fixture

`hello.png` is a fixed 350 × 90 RGB image of “HELLO 123” on white. It was generated once using OpenCV Hershey Simplex (scale 1.5, thickness 2, antialiased, baseline at 12,63). Tests read the committed PNG and do not load OpenCV. The fixture is a smoke input, not a recognition accuracy benchmark.

The following RGB PNGs were generated once with Pillow 12.0.0 and the macOS Arial Unicode font at 40 px; tests do not require Pillow or the font. White background, 20 px horizontal padding, 80 px height, text top at y=15. The font itself is not distributed.

| File | Size | Expected text | RGB text color |
|---|---|---|---|
| `ocr-english.png` | 301 × 80 | `EasyCon 2026` | 0,0,0 |
| `ocr-chinese.png` | 266 × 80 | `你好世界123` | 0,0,0 |
| `ocr-mixed.png` | 490 × 80 | `自动化测试 EasyCon 123` | 20,70,130 |

Corpus assertions ignore whitespace only; case, characters and digits must match exactly. Each backend also checks ROI coordinates, confidence, nested crops, invalid ROI, cancellation/reuse and blank→text state isolation. Game screenshot tests load externally supplied files using `EASYCON_OCR_GAME_ASSETS`; see [real OCR acceptance](../../vision/ACCEPTANCE.md).
