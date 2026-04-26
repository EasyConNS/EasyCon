# EasyCon.Capture 模块

## 模块概述

Capture模块实现UI无关的图像采集与识别功能，是EasyCon2的"眼睛"。基于OpenCvSharp封装，为上层提供视频帧获取、模板匹配、OCR文字识别和颜色检测等能力。

本模块可看作OpenCvSharp的轻量业务封装，核心关注点是将OpenCV能力映射到Switch自动化场景（如识别游戏画面状态、定位UI元素）。

## 核心功能

### 视频采集
- 通过USB采集卡（UVC协议）或虚拟摄像头获取Switch画面
- 支持多设备管理和自动格式适配
- Windows平台额外提供DirectShow设备枚举

### 模板匹配
- 基于OpenCV `MatchTemplate` 的子图搜索，支持多种匹配算法（SqDiff、CCorr、CCoeff及其归一化变体）
- 支持边缘检测辅助匹配（Sobel、Laplacian、Canny），提升抗干扰能力
- 匹配结果包含位置、置信度和边界框

### OCR文字识别
- 基于Tesseract引擎，支持多语言识别
- 支持白名单/黑名单过滤，提升特定场景识别精度

### 图像标签系统
- **ImgLabel (.IL)**: JSON格式的图像标签，记录搜索区域、目标图像（Base64编码）、匹配算法和阈值等参数
- **ImgLabelX (.ILX)**: 新一代标签格式，采用自定义二进制序列化，提升加载效率
- 标签支持按目录批量加载，自动去重

### 颜色检测
- HSV颜色空间的区域检测，用于判断特定颜色状态

## 关键类

- **ECCapture** — 采集设备管理入口，枚举摄像头和采集API
- **OpenCVCapture** — 视频帧获取，封装OpenCV VideoCapture
- **ECSearch** — 图像搜索门面，统一接口调用模板匹配、OCR、像素匹配等算法
- **ImgLabel** — 图像标签数据模型，存储搜索参数和目标图像
- **OCRDetect** — Tesseract OCR封装

## 依赖项

- OpenCvSharp4
- Tesseract（OCR引擎）
- SixLabors.ImageSharp（图像格式转换）

---

**版本**: 2.0
