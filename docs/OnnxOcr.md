# OpenCV ONNX OCR 后端

EasyCon 的 ONNX OCR 后端使用随 EzCv 分发的 OpenCV 5 DNN 模块，不需要额外安装 ONNX Runtime NuGet 包。后端当前支持：

- PaddleOCR 风格的 CTC 文字识别模型；
- 可选的 PaddleOCR DB 文字检测模型；
- CPU、OpenCL、Vulkan 和 CUDA 目标选择；
- 通过 JSON 清单配置输入尺寸、归一化、通道顺序、CTC 空白类别和字符词典。

ONNX 是模型容器格式，不同模型的输入和输出约定并不相同。通用配置解决模型路径和预处理差异；模型仍须输出 `[T, C]`、`[1, T, C]`，或尾部维度均为 1 的 `[1, T, C, ...]` CTC 分数张量。

## ECS 使用方式

先用四参数 `OCR_INIT` 初始化模型，再使用原有 `OCR` 读取选区：

```ecs
$ready = OCR_INIT("sample", __APP__ + "/Models/sample", "ONNX", "sample_ocr.json")
IF not $ready
    PRINT "ONNX OCR 初始化失败"
    RETURN
ENDIF

$text = OCR(100, 80, 420, 64, "sample")
$confidence = OCR_CONF()
```

`engineMode` 可使用 `ONNX`、`ONNX:CPU`、`ONNX:OPENCL`、`ONNX:VULKAN` 或 `ONNX:CUDA`。设备后端是否可用取决于随程序分发的 OpenCV 构建和运行机器。

第四个参数在 ONNX 模式下表示模型清单文件。相对路径从 `dataPath` 开始解析；传入 `SINGLE_LINE` 等非 JSON 值时，后端会自动查找 `<lang>_ocr.json`。显式初始化的模型会保留在缓存中，后续 `OCR` 不会把它替换回 Tesseract。

## 模型清单

```json
{
  "recognitionModel": "rec.onnx",
  "characterDictionary": "dict.txt",
  "detectionModel": "det.onnx",
  "recognition": {
    "minScore": 0.5,
    "imageHeight": 48,
    "minImageWidth": 8,
    "maxImageWidth": 2048,
    "widthMultiple": 1,
    "scale": 0.00392156862745098,
    "mean": [0, 0, 0],
    "swapRedBlue": true,
    "paddingValue": 0,
    "blankIndex": 0,
    "appendSpaceClass": true
  },
  "detection": {
    "maxSideLen": 960,
    "boxThreshold": 0.3,
    "mergeBoxes": true,
    "inputMultiple": 32,
    "scale": 0.00392156862745098,
    "mean": [123.675, 116.28, 103.53],
    "standardDeviation": [0.229, 0.224, 0.225],
    "swapRedBlue": true,
    "paddingValue": 0
  }
}
```

OpenCV 的归一化公式为 `(pixel - mean) * scale`。例如 `[0, 1]` 输入使用 `scale = 1/255` 和 `mean = [0, 0, 0]`；`[-1, 1]` 输入使用 `scale = 1/127.5` 和 `mean = [127.5, 127.5, 127.5]`。

检测器还会在应用 `scale` 和 `mean` 后逐通道除以 `standardDeviation`。清单中的默认检测参数对应常见 PaddleOCR DB 模型的 `(pixel/255 - mean) / std` 预处理。

词典采用 UTF-8，每行是一个类别对应的文本，可以是单个字符或多个 Unicode 字符。CTC 空白类别不写入词典；其索引由 `blankIndex` 指定。如果输出类别数比“词典 + 空白类别”多 1，且 `appendSpaceClass` 为 `true`，最后一个类别按空格解码。

## 无清单的约定

不提供 JSON 清单时，识别器依次尝试：

1. `<lang>_rec.onnx` 与 `<lang>_dict.txt`；
2. `PP-OCRv5_mobile_rec.onnx` 与 `ppocr_keys_v5.txt`。

端到端检测默认查找 `PP-OCRv5_mobile_det.onnx`。无清单模式使用 PaddleOCR 常见的 32 像素高度、8 像素宽度对齐和 `[-1, 1]` 归一化。

## 宿主 API

宿主可以直接构造 `OnnxRecognizer`、`OnnxDetector`、`OnnxOcrEngine`，也可以让 `OcrEngineCache` 根据 `engineMode` 选择后端。传入自定义 `IOcrEngineFactory` 时仍保持原有固定工厂行为。
