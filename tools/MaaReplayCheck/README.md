# Maa P2 模板对拍

独立验收工具，不在产品解决方案和默认测试入口中。使用真实 Maa runtime，对递归发现的有效模板标签逐一比较旧 `ImgLabel.Search` 与 `MaaVisionService`；文字 OCR 由 `MaaOcrTests` 的模型用例验证。

```powershell
dotnet run --project tools/MaaReplayCheck -c Release -- <源包目录> <独立输出目录> <1920x1080截图目录>
```

输出 generation/转换报告、逐项 `replay.jsonl`、源 IL/ECS/Flow 的前后 SHA-256 和 summary。summary 额外记录同一原生资源实际通过自匹配装载验证的不同模板数，以及模板完整路径的最大长度；仅看到旧评分一致不能代替原生资源可用性证据。LegacyGui 全量调用实际 Maa 服务；LegacyCli 全量检查共享适配器，每种方法×每帧另调用一次实际 CLI profile 服务。返回非零代表对拍差异、执行错误或源文件发生变化；非法源标签另列在转换报告中，不假装参与通过的对拍。

只比较图像与算法结果，不推断每个标签的业务正负真值。有限截图不能代替每标签正例/近似负例、原游戏脚本闭环或硬件验收。源包、截图和模型不入库。
