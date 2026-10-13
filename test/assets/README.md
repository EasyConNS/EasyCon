# test/assets — 真机测试资产

测试用的真实模型 / 截图等大文件放这里，**不入库**（.gitignore 只保留本 README）。
资产缺失时，依赖它们的测试自动 Ignore 跳过，CI 不受影响；仓库代码不硬编码任何真实环境路径。

## 约定布局

    test/assets/
    ├── ppocr/
    │   ├── PP-OCRv6_medium_det.onnx   # PP-OCRv6 medium det（PaddlePaddle 官方 ONNX：
    │   │                              #   https://hf-mirror.com/PaddlePaddle/PP-OCRv6_medium_det_onnx/resolve/main/inference.onnx
    │   │                              #   原始仓库 https://huggingface.co/PaddlePaddle/PP-OCRv6_medium_det_onnx，约 62MB）
    │   ├── PP-OCRv6_medium_rec.onnx   # PP-OCRv6 medium rec（同上 /PP-OCRv6_medium_rec_onnx，约 76MB）
    │   ├── ppocrv6_dict.txt           # PP-OCRv6 官方字典（18708 行；数字 '0'-'9' 在表头，
    │   │                              #   class 33-42；PaddleOCR 仓库 ppocr/utils/dict/ppocrv6_dict.txt）
    │   ├── PP-OCRv5_mobile_det.onnx   # PP-OCRv5 mobile det（旧代际，回退/对拍用；官方发布转 ONNX）
    │   ├── PP-OCRv5_mobile_rec.onnx   # PP-OCRv5 mobile rec（旧代际）
    │   ├── ppocr_keys_v5.txt          # PP-OCRv5 官方字典（18383 行；数字 '0'-'9' 在表尾，
    │   │                              #   class 16163-16172；NET_LABELS 标签表交叉验证）
    │   └── ppocr2.png                 # 火红/叶绿英文状态页截图（1920x1080）
    │   └── ppocr.png / ppocr3.png / ppocr4.png   # 日文版画面 / 英文 INFO 页 / 日文能力页
    ├── resnet/
    │   ├── resnet18.onnx              # ResNet-18 ImageNet-1k（microsoft/resnet-18 的 transformers 导出；下载：
    │   │                              #   https://hf-mirror.com/onnx-community/resnet-18-ONNX/resolve/main/onnx/model.onnx，约 45MB）
    │   └── imagenet_classes.txt       # ImageNet 1000 类名（pytorch/hub；class id = 行号-1，无尾换行）
    └── yolo26/
        ├── yolo26n.onnx               # YOLO26n 端到端检测（COCO 预训练；下载：
        │                              #   https://hf-mirror.com/prithivMLmods/YOLO26-ONNX/resolve/main/yolo26n/yolo26n.onnx
        │                              #   原始仓库 https://huggingface.co/prithivMLmods/YOLO26-ONNX，约 9.9MB）
        └── bus.jpg                    # Ultralytics 经典街景测试图（810x1080，COCO 必中巴士+人；
                                       #   https://gh-proxy.com/https://github.com/ultralytics/yolov5/raw/master/data/images/bus.jpg）
                                       #   ResNet 分类测试亦复用本图（黄色大巴 → minibus/school bus）

## 部署约定名（OCR 服务面，代码不绑定模型代际）

`PaddleOnnxOcr` / `OnnxEngineFactory` 按**部署约定名**在模型目录内解析（模型与字典同代配对
由部署保证；脚本面 `NET_LOAD` 则用任意显式路径）：

    det.onnx            # 检测模型（OnnxEngineFactory detModelName 参数可覆写）
    <lang>_rec.onnx     # 识别模型（如 eng_rec.onnx / ch_rec.onnx）
    <lang>_dict.txt     # 字符集（与 rec 模型同代）

数字窗口下标随代际不同，由脚本以常量指定（std 不钉常数）：
PP-OCRv6 = 33（数字在表头）、PP-OCRv5 = 16163（数字在表尾）——见 `examples/pokemon-stats.ecs`。

## 使用方

- `NetPpocrRealModelTests.PokemonStats_RecAndCompute`（examples/pokemon-stats.ecs 端到端，v6；
  测试把模型 symlink 进临时 `__APP__/models/ppocr/` 满足脚本契约路径）
- `NetPpocrRealModelTests.PpocrDictionary_Labels_CrossValidate_ScriptSideFold`
  （真实字典 × NET_LABELS/NET_LABEL 交叉验证，v5/v6 两代各跑一遍：class→NET_LABEL(cls-1)
  行号对齐，脚本侧自实现折叠与 CTC_FOLD 洞同答 19531）
- `NetPpocrRealModelTests.SyntheticRender_DetRec_DigitsEndToEnd`（合成图 det+rec 全链，只需模型+字典）
- `PaddleOnnxOcrTests`（det+rec 全流程 vs Tesseract 对拍；向上探测 test/assets/ppocr，
  symlink 成部署约定名）
- `NetResnetRealModelTests`（ResNet-18 真模型分类全链：FILE/rgbc → SCALE → NORM →
  RUNH → ARGMAX → LABELS/LABEL，模型 symlink 进临时 `__APP__/models/resnet/`）
- `NetYoloRealModelTests`（YOLO26n × OpenCV DNN 端到端检测 smoke + bus.jpg 真实检出/逆映射，
  模型 symlink 进临时 `__APP__/models/yolo26/`）
