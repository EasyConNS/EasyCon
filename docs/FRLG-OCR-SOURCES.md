# FRLG 场景 OCR

本实现把 FRLG 的固定界面字段作为“场景”识别，而不是让通用 OCR 猜测任意文字。脚本仍使用 EasyCon
已有的 `OCR(x, y, width, height, "lang")` 调用；当 `lang` 是下列 `FRLG_*` 场景键时，运行时自动路由到
FRLG 识别器。其他语言键继续走原 Tesseract 路径。

本提交没有增加独立 OCR 窗口。原有标签编辑器新增“FRLG 场景 OCR”搜索方法，可选择场景、应用当前截图
分辨率对应的默认区域、手动圈选、执行标签测试，并显示可复制的 `OCR(...)` 调用和识别结果。保存的 `.IL`
只记录场景与选区，不需要目标图片。

## 支持场景

| 场景键 | 内容 | 外部模型 |
| --- | --- | --- |
| `FRLG_JPN_TID` | 日版训练家卡 TID | 否 |
| `FRLG_EN_TID` | 英文训练家卡 TID | 否 |
| `FRLG_JPN_NAME` | 日版野生名称 | 是 |
| `FRLG_JPN_WILD_LEVEL` | 日版野生等级 | 是 |
| `FRLG_JPN_SUMMARY_NAME` | 日版摘要种族名称 | 是 |
| `FRLG_JPN_NATURE` | 日版性格 | 是 |
| `FRLG_JPN_LEVEL` | 日版摘要等级 | 否 |
| `FRLG_JPN_HP` | 日版最大 HP（支持当前/最大格式） | 否 |
| `FRLG_JPN_ATTACK` | 日版攻击 | 否 |
| `FRLG_JPN_DEFENSE` | 日版防御 | 否 |
| `FRLG_JPN_SP_ATTACK` | 日版特攻 | 否 |
| `FRLG_JPN_SP_DEFENSE` | 日版特防 | 否 |
| `FRLG_JPN_SPEED` | 日版速度 | 否 |

名称场景还可在脚本键后附加候选，例如
`FRLG_JPN_NAME:ミニリュウ|ハクリュー`。留空则使用完整日文种族词典；候选只缩小词典范围，不会绕过
OCR 或强制返回候选。

## 模型安装

TID、摘要等级、HP 与六维能力值使用随程序集嵌入的数字模板，不需要外部模型。名称、性格和野生等级需要
PaddleOCR 与 Tesseract 日文模型；由于两者合计约 120 MB，不直接提交到 Git：

```powershell
python tools/FrlgOcr/fetch_resources.py --models-only
```

脚本会按 `docs/frlg-ocr-resources.lock.json` 中的固定提交和 SHA-256 下载到 `models/frlg/`。构建时若该目录
存在，文件会复制到应用输出的 `models/frlg/`。也可把 `EASYCON_FRLG_MODELS` 指向同样目录结构的外部位置。

## 来源与许可

本 PR 目标基线为 `EasyConNS/EasyCon@0718a15b484d7f2a70c43b5f0c3c670f07e2f23a`；FRLG OCR 最初从
`EasyConNS/EasyCon@1aed001c0e2d3a32d211c39bec26546741626bd6` 开始移植。项目根目录 `LICENSE` 为 GPL-3.0。

数字读取与匹配算法移植自 PokémonAutomation：

- `PokemonAutomation/Arduino-Source@a772133ebc497aed05439f464a6222d3d83e0c10`
- `SerialPrograms/Source/PokemonFRLG/Inference/PokemonFRLG_DigitReader.cpp`
- `SerialPrograms/Source/PokemonFRLG/Inference/PokemonFRLG_TrainerIdReader.cpp`
- `SerialPrograms/Source/CommonTools/ImageMatch/ExactImageMatcher.cpp`
- `SerialPrograms/Source/PokemonFRLG/PokemonFRLG_Settings.cpp`

上述代码采用 MIT License，完整版权与许可证保存在 `docs/licenses/PokemonAutomation-MIT.txt`。

数字模板来自 `PokemonAutomation/Packages@e8cc29cdc9e9c16faf406a1d154d70ad687b375c` 的 `Resources/PokemonFRLG/{Digits,LevelDigits,DialogDigits}/0–9.png`。

公开回放图片来自 `PokemonAutomation/CommandLineTests@46b892bd7f2106a1f34de11aa300b492aee06b83` 的 `PokemonFRLG/TrainerIdReader/nyash_jpn_45345.png` 与 `tom_eng_60895.jpg`。

模板与截图保留原始字节，下载位置、大小和 SHA-256 逐项记录在 `frlg-ocr-resources.lock.json`。图像资源不据此另行宣称具有代码的 MIT 授权；相关游戏画面与商标仍归原权利人。用户本地历史截图仅作本地验证，不进入提交或公共测试包。

移植调整：按采集高度归一化字体尺寸；同一区域采用多阈值一致性；收紧 RMSD 与第二候选差距；严格检查五位数字及 16-bit TID 范围；不接受上游“跳过失败数字块后拼接”的结果。默认区域加少量边距，手动选区保持原样。

### 日文文字和摘要

同一 Arduino-Source 提交的 `PokemonFRLG_StatsReader.cpp`、`PokemonFRLG_WildEncounterReader.cpp`、`PokemonFRLG_OcrPreprocessing.cpp`、`ML_PaddleOCRPipeline.cpp` 提供场景坐标、文字预处理和 Paddle 输入/解码参考。C# 实现增加模型生命周期管理、匹配置信门槛、阈值一致性与 Tesseract 第二意见。野生等级是本项目新增场景，上游尚未启用；默认框只作起点，手动选区优先。

词典来自固定 Packages 提交的 `PokemonNameDisplay.json`、`PokemonNameOCR/PokemonOCR-jpn.json`、`PokemonFRLG/NatureCheckerOCR.json`、`OCR/CharacterReductions.json`。性格输出去掉「せいかく」后缀，名称输出日文标准名。

模型来自同一 Packages 提交的 `PaddleOCR/chinese/rec.onnx`（84,468,836 字节）、`dict.txt`、`config.json` 与 `Tesseract/jpn.traineddata`，全部纳入 SHA-256 清单。Paddle 实际使用 48 像素高度、RGB [0,1] NCHW 输入；config 中标称的 32 像素高度不适用。

Packages 说明 Paddle 模型源自 [monkt/paddleocr-onnx](https://huggingface.co/monkt/paddleocr-onnx)。核对模型卡提交 `7b02d0a30a07ba2b92ad1ff5a8941ae2c633de65` 标注 Apache-2.0。PaddleOCR 和 Tesseract 数据的 Apache-2.0 文本随包提供，分别固定于 `PaddlePaddle/PaddleOCR@2661c7c0ef5c613e8f93c6e93b2e052399f0f854`、`tesseract-ocr/tessdata@ced78752cc61322fb554c280d13360b35b8684e4`。ONNX Runtime 使用 NuGet 的 Microsoft.ML.OnnxRuntime 1.27.1（MIT）。这些不是本项目训练的模型。

新增公开样本仍来自固定 CommandLineTests 提交的 `StatsReader/Page1/{bulbasaur,deoxys}_1_jpn.png`、`StatsReader/Page2/deoxys_1_jpn.png` 及配套预期值；`WildEncounterReader/eng_dragonair.jpg`、`eng_chansey.jpg` 用于共享战斗数字字体检查。没有把用户本机缓存截图纳入提交。
