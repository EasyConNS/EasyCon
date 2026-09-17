namespace EasyCon.Capture.Ocr;

/// <summary>
/// 文本检测框 — 图片中检测到的文字区域位置。
/// 对应 ocr-rs 的 TextBox。
/// </summary>
/// <param name="X">左上角 X 坐标</param>
/// <param name="Y">左上角 Y 坐标</param>
/// <param name="Width">宽度</param>
/// <param name="Height">高度</param>
public readonly record struct OcrTextBox(int X, int Y, int Width, int Height);

/// <summary>
/// 单区域识别结果 — 文本 + 置信度。
/// 对应 ocr-rs 的 RecognitionResult。
/// </summary>
/// <param name="Text">识别到的文本</param>
/// <param name="Confidence">置信度 (0.0 ~ 1.0)</param>
public readonly record struct OcrRecognizeResult(string Text, float Confidence);

/// <summary>
/// 端到端 OCR 结果 — 检测框 + 文本 + 置信度。
/// 对应 ocr-rs 的 OcrResult（DetModel 检测 + RecModel 识别后的综合结果）。
/// </summary>
/// <param name="Text">识别到的文本</param>
/// <param name="Confidence">置信度 (0.0 ~ 1.0)</param>
/// <param name="Box">检测框（仅检测引擎可用时为非 null）</param>
public readonly record struct OcrResult(string Text, float Confidence, OcrTextBox? Box);