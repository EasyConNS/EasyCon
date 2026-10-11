# test/assets — 真机测试资产

测试用的真实模型 / 截图等大文件放这里，**不入库**（.gitignore 只保留本 README）。
资产缺失时，依赖它们的测试自动 Ignore 跳过，CI 不受影响；仓库代码不硬编码任何真实环境路径。

## 约定布局

    test/assets/
    └── ppocr/
        ├── PP-OCRv5_mobile_rec.onnx   # PP-OCRv5 mobile rec 模型（PaddleOCR 官方发布）
        └── ppocr2.png                 # 火红/叶绿英文状态页截图（1920x1080）

## 使用方

- `NetPpocrRealModelTests.PokemonStats_RecAndCompute`（examples/pokemon-stats.ecs 端到端，
  测试把模型 symlink 进临时 `__APP__/models/ppocr/` 满足脚本契约路径）
