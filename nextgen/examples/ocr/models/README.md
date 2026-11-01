# 随工作流发布的模型

此目录放工作流实际使用的模型。示例需要：

- `ppocr.onnx`：PP-OCRv6_medium_rec.onnx 的副本。
- `ppocr.txt`：对应的 ppocrv6_dict.txt 字典。
- `eng.traineddata`、`chi_sim.traineddata`：Tesseract 5 英文／简体中文数据。

这些文件与 workflow.json 一起交付，无需运行时下载或转换。模型文件由每套工作流选择，不作为运行基座的固定默认资产。完整项目可通过 native/package.py 的 --project 一并打包。
