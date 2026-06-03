namespace EasyScript;

/// &lt;summary&gt;
/// OCR委托，用于从指定区域提取文本。
/// &lt;/summary&gt;
/// &lt;param name="x"&gt;区域左上角X坐标&lt;/param&gt;
/// &lt;param name="y"&gt;区域左上角Y坐标&lt;/param&gt;
/// &lt;param name="width"&gt;区域宽度&lt;/param&gt;
/// &lt;param name="height"&gt;区域高度&lt;/param&gt;
/// &lt;param name="lang"&gt;OCR语言&lt;/param&gt;
/// &lt;returns&gt;识别到的文本&lt;/returns&gt;
public delegate string OcrDelegate(int x, int y, int width, int height, string lang);

/// &lt;summary&gt;
/// 帧委托，用于获取指定区域的图像数据。
/// &lt;/summary&gt;
/// &lt;param name="x"&gt;区域左上角X坐标&lt;/param&gt;
/// &lt;param name="y"&gt;区域左上角Y坐标&lt;/param&gt;
/// &lt;param name="width"&gt;区域宽度&lt;/param&gt;
/// &lt;param name="height"&gt;区域高度&lt;/param&gt;
/// &lt;returns&gt;Base64编码的PNG图像数据&lt;/returns&gt;
public delegate string FrameDelegate(int x, int y, int width, int height);

/// &lt;summary&gt;
/// ROI委托，用于从给定图像中裁剪指定区域。
/// &lt;/summary&gt;
/// &lt;param name="base64"&gt;Base64编码的原始图像数据&lt;/param&gt;
/// &lt;param name="x"&gt;区域左上角X坐标&lt;/param&gt;
/// &lt;param name="y"&gt;区域左上角Y坐标&lt;/param&gt;
/// &lt;param name="width"&gt;区域宽度&lt;/param&gt;
/// &lt;param name="height"&gt;区域高度&lt;/param&gt;
/// &lt;returns&gt;Base64编码的裁剪后图像数据&lt;/returns&gt;
public delegate string RoiDelegate(string base64, int x, int y, int width, int height);

/// &lt;summary&gt;
/// 标签匹配委托，用于检查标签是否存在。
/// &lt;/summary&gt;
/// &lt;param name="labelName"&gt;标签名称&lt;/param&gt;
/// &lt;returns&gt;匹配到的标签索引，未找到返回-1&lt;/returns&gt;
public delegate int LabelMatchDelegate(string labelName);