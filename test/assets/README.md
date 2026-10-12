# test/assets — 真机测试资产

测试用的真实模型 / 截图等大文件放这里，**不入库**（.gitignore 只保留本 README）。
资产缺失时，依赖它们的测试自动 Ignore 跳过，CI 不受影响；仓库代码不硬编码任何真实环境路径。

## 约定布局

    test/assets/
    ├── ppocr/
    │   ├── PP-OCRv5_mobile_det.onnx   # PP-OCRv5 mobile det 模型（PaddleOCR 官方发布；det+rec 对拍用）
    │   ├── PP-OCRv5_mobile_rec.onnx   # PP-OCRv5 mobile rec 模型（PaddleOCR 官方发布）
    │   ├── ppocr_keys_v5.txt          # PP-OCRv5 官方字典（18383 行；NET_LABELS 标签表交叉验证）
    │   └── ppocr2.png                 # 火红/叶绿英文状态页截图（1920x1080）
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

## 使用方

- `NetPpocrRealModelTests.PokemonStats_RecAndCompute`（examples/pokemon-stats.ecs 端到端，
  测试把模型 symlink 进临时 `__APP__/models/ppocr/` 满足脚本契约路径）
- `NetPpocrRealModelTests.PpocrDictionary_Labels_CrossValidate_ScriptSideFold`
  （真实字典 × NET_LABELS/NET_LABEL 交叉验证：class→NET_LABEL(cls-1) 行号对齐，
  脚本侧自实现折叠与 CTC_FOLD 洞同答 19531）
- `PaddleOnnxOcrTests`（det+rec 全流程 vs Tesseract 对拍；向上探测 test/assets/ppocr）
- `NetResnetRealModelTests`（ResNet-18 真模型分类全链：FILE/rgbc → SCALE → NORM →
  RUNH → ARGMAX → LABELS/LABEL，模型 symlink 进临时 `__APP__/models/resnet/`）
- `NetYoloRealModelTests`（YOLO26n × OpenCV DNN 端到端检测 smoke + bus.jpg 真实检出/逆映射，
  模型 symlink 进临时 `__APP__/models/yolo26/`）
