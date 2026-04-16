# EasyCon.Capture 模块

## 模块概述

Capture模块用来实现UI无关的图像处理功能，主要列表如下：

- 采集卡或虚拟摄像头的连接驱动（UVC），目前主要是由opencv库实现
- 视频画面的获取，目前主要靠opencv采集帧数据转成图像显示以达到监视器效果
- 画面内容的判断，可以根据模板匹配帧数据判断，目前的需求有子图像匹配（已实现）和OCR识别（待实现）

本模块目前高度依赖opencv库，可以看作是OpenCVSharp库的一个简单包装。

## 核心功能

### 1. 视频采集
- **采集卡支持**: 支持USB视频采集卡(UVC)
- **虚拟摄像头**: 支持虚拟摄像头设备
- **多设备管理**: 同时管理多个视频源
- **格式转换**: 自动格式转换和适配

### 2. 图像识别
- **模板匹配**: 高精度的子图像匹配
- **OCR识别**: 文字识别功能
- **颜色检测**: HSV颜色空间检测
- **图像预处理**: 噪声过滤、边缘增强等

### 3. 性能优化
- **硬件加速**: GPU加速支持
- **异步处理**: 异步图像采集
- **内存优化**: 图像缓存和复用

## 主要组件

### Capture 类
视频采集的核心类：

```csharp
public class Capture
{
    public int Width { get; }
    public int Height { get; }
    public int FPS { get; }
    
    public bool Open(string sourceName);
    public Mat CaptureFrame();
    public void Release();
    public bool IsConnected { get; }
}
```

### CVCapture 类
OpenCV采集卡实现：

```csharp
public class CVCapture : IDisposable
{
    public VideoCapture GetCapture();
    public Mat QueryFrame();
    public void SetCaptureProperty(PropId prop, double value);
    public double GetCaptureProperty(PropId prop);
}
```

### ImgLabel 类
图像标签管理系统：

```csharp
public class ImgLabel
{
    public string Name { get; set; }
    public string ImagePath { get; set; }
    public Mat Template { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    
    public void Load(string labelPath);
    public void Save(string labelPath);
    public bool Validate();
}
```

## 图像识别算法

### 模板匹配 (Template Matching)

```csharp
public class TemplateMatcher
{
    public MatchResult FindTemplate(Mat source, Mat template, double threshold = 0.8);
    public List<MatchResult> FindMultipleTemplates(Mat source, Mat template, double threshold = 0.8);
    public MatchResult FindTemplateWithRotation(Mat source, Mat template, double threshold, float maxRotation = 360f);
}

public struct MatchResult
{
    public bool Found;
    public Point Location;
    public double Confidence;
    public Rect BoundingBox;
}
```

#### 匹配方法
- **TM_SQDIFF**: 平方差匹配法
- **TM_SQDIFF_NORMED**: 归一化平方差匹配法
- **TM_CCORR**: 相关匹配法
- **TM_CCORR_NORMED**: 归一化相关匹配法
- **TM_CCOEFF**: 系数匹配法
- **TM_CCOEFF_NORMED**: 归一化系数匹配法

### OCR识别 (OCR Detection)

```csharp
public class OCRDetect
{
    public OCRDetect(string tessDataPath, string lang = "eng");
    public string RecognizeText(Mat image);
    public List<TextRegion> RecognizeTextWithRegions(Mat image);
    public void SetWhitelist(string characters);
    public void SetBlacklist(string characters);
}

public class TextRegion
{
    public Rect BoundingBox;
    public string Text;
    public double Confidence;
}
```

#### 支持的语言
- 英语 (eng)
- 简体中文 (chi_sim)
- 繁体中文 (chi_tra)
- 日语 (jpn)
- 韩语 (kor)

### 颜色检测 (Color Detection)

```csharp
public class HSVColor
{
    public static Mat DetectColor(Mat source, Scalar lowerBound, Scalar upperBound);
    public static List<Contour> FindContours(Mat binaryImage);
    public static Mat DrawContours(Mat source, List<Contour> contours);
}
```

### 图像搜索 (Image Search)

```csharp
public class Search
{
    public static MatchResult FindImage(Mat source, Mat template, double threshold = 0.9);
    public static List<MatchResult> FindAllImages(Mat source, Mat template, double threshold = 0.9);
    public static Mat PreprocessImage(Mat image, PreprocessOptions options);
}

public class PreprocessOptions
{
    public bool ConvertToGray { get; set; }
    public bool ApplyGaussianBlur { get; set; }
    public bool ApplyThreshold { get; set; }
    public double ThresholdValue { get; set; } = 127.0;
    public bool EnhanceContrast { get; set; }
}
```

## 使用示例

### 基本图像采集
```csharp
// 打开采集卡
var capture = new CVCapture();
if (capture.Open(0)) // 使用第一个摄像头
{
    // 采集一帧图像
    Mat frame = capture.QueryFrame();
    
    if (frame != null)
    {
        // 显示图像
        Cv2.ImShow("Capture", frame);
        Cv2.WaitKey(1);
    }
}
```

### 模板匹配示例
```csharp
// 加载源图像和模板
Mat source = Cv2.ImRead("source.png");
Mat template = Cv2.ImRead("template.png");

// 进行模板匹配
var result = Search.FindImage(source, template, 0.85);

if (result.Found)
{
    Console.WriteLine($"找到匹配图像，位置: {result.Location}, 置信度: {result.Confidence}");
    
    // 在源图像上绘制匹配结果
    Cv2.Rectangle(source, result.BoundingBox, Scalar.Red, 2);
    Cv2.ImShow("Result", source);
}
```

### OCR识别示例
```csharp
// 初始化OCR引擎
var ocr = new OCRDetect("./tessdata", "chi_sim+eng");

// 设置字符白名单（只识别数字和字母）
ocr.SetWhitelist("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

// 识别图像中的文字
Mat image = Cv2.ImRead("text_image.png");
string text = ocr.RecognizeText(image);

Console.WriteLine($"识别结果: {text}");
```

### 颜色检测示例
```csharp
// 加载图像
Mat image = Cv2.ImRead("color_image.png");

// 转换到HSV颜色空间
Mat hsv = new Mat();
Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);

// 检测红色区域（HSV范围）
Scalar lowerRed = new Scalar(0, 100, 100);
Scalar upperRed = new Scalar(10, 255, 255);

Mat redMask = HSVColor.DetectColor(hsv, lowerRed, upperRed);

// 查找轮廓
var contours = HSVColor.FindContours(redMask);
Console.WriteLine($"找到 {contours.Count} 个红色区域");
```

## 图像标签系统

### 标签文件格式 (.IL)
```json
{
  "name": "button_a",
  "imagePath": "./images/button_a.png",
  "threshold": 0.85,
  "searchMethod": "TM_CCOEFF_NORMED",
  "metadata": {
    "description": "A按钮",
    "priority": 1,
    "tags": ["ui", "button"]
  }
}
```

### 标签管理
```csharp
// 加载标签
var label = new ImgLabel();
label.Load("./labels/button_a.il");

// 进行识别
Mat screen = CaptureScreen();
var result = Search.FindImage(screen, label.Template, 0.85);

if (result.Found)
{
    Console.WriteLine($"找到 {label.Name}: {result.Location}");
}
```

## 性能优化

### 图像缓存
```csharp
public class ImageCache
{
    private readonly Dictionary<string, Mat> _cache = new();
    private readonly int _maxCacheSize = 100;
    
    public Mat GetOrLoad(string imagePath)
    {
        if (!_cache.ContainsKey(imagePath))
        {
            Mat image = Cv2.ImRead(imagePath);
            _cache[imagePath] = image;
            
            // 缓存满时清理最旧的图像
            if (_cache.Count > _maxCacheSize)
            {
                var oldest = _cache.First();
                oldest.Value.Dispose();
                _cache.Remove(oldest.Key);
            }
        }
        return _cache[imagePath];
    }
}
```

### 异步图像处理
```csharp
public class AsyncImageProcessor
{
    public Task<Mat> CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => 
        {
            return CaptureFrame();
        }, cancellationToken);
    }
    
    public Task<MatchResult> FindTemplateAsync(Mat source, Mat template, 
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => 
        {
            return FindTemplate(source, template);
        }, cancellationToken);
    }
}
```

## 配置选项

### 采集卡配置
```csharp
public class CaptureConfig
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int FPS { get; set; } = 30;
    public int Backend { get; set; } = 0; // 0=Auto, 1=DirectShow, 2=FFMPEG
}
```

### 匹配算法配置
```csharp
public class MatchingConfig
{
    public double DefaultThreshold { get; set; } = 0.85;
    public TemplateMatchingMethod Method { get; set; } = 
        TemplateMatchingMethod.CCoeffNormed;
    public bool EnableRotation { get; set; } = false;
    public float MaxRotation { get; set; } = 360f;
}
```

## 依赖项

- **OpenCV**: 4.x 版本
- **OpenCVSharp**: OpenCV的.NET封装
- **Tesseract**: OCR引擎
- **项目依赖**: EasyCon.Core

## 调试工具

### 图像查看器
```csharp
public class ImageViewer
{
    public void ShowImage(Mat image, string windowName = "Image");
    public void ShowMatchResult(Mat source, MatchResult result);
    public void SaveDebugImage(Mat image, string filename);
}
```

### 性能分析
```csharp
public class PerformanceProfiler
{
    public void StartTiming(string operation);
    public void StopTiming(string operation);
    public Dictionary<string, TimeSpan> GetResults();
}
```

## 未来发展

### 计划功能
- [ ] 深度学习目标检测
- [ ] 更精确的OCR识别
- [ ] 实时视频流处理
- [ ] 3D图像处理
- [ ] AR/VR支持

### 性能优化
- [ ] GPU加速普及
- [ ] 多线程处理优化
- [ ] 内存使用优化

---

**模块维护者**: EasyCon.Capture开发团队  
**最后更新**: 2026年4月16日  
**版本**: 1.0