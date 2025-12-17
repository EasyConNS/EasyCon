using System.Drawing;
using System.Text;

namespace EasyCapture;

/// <summary>
/// 图像处理格式定义
/// </summary>
public class ImageLabelX
{
    /// <summary>
    /// 目标区域坐标和尺寸
    /// </summary>
    public Rectangle TargetRegion { get; set; }
    /// <summary>
    /// 搜索区域坐标和尺寸
    /// </summary>
    public Rectangle SearchRegion { get; set; }

    /// <summary>
    /// 图像数据（Base64编码）或OCR文本
    /// </summary>
    public string TargetData { get; set; } = string.Empty;

    /// <summary>
    /// 处理方式（根据数据类型自动选择）
    /// </summary>
    public SearchMethod ProcessingMethod { get; set; } = SearchMethod.SqDiffNormed;

    /// <summary>
    /// 可接受的相似度阈值 (0-1)
    /// </summary>
    public double SimilarityThreshold
    {
        get => _similarityThreshold;
        set => _similarityThreshold = Math.Clamp(value, 0.0, 1.0);
    }
    private double _similarityThreshold = 0.95;

    /// <summary>
    /// 检查当前处理方式是否适用于图像数据
    /// </summary>
    public bool IsImageProcessingMethod => (int)ProcessingMethod < 10;

    /// <summary>
    /// 检查当前处理方式是否适用于文本数据
    /// </summary>
    public bool IsTextProcessingMethod => (int)ProcessingMethod > 10;

    /// <summary>
    /// 验证标签合理性
    /// </summary>
    public bool Validate(out string errorMessage)
    {
        if (TargetRegion.Width <= 0 || TargetRegion.Height <= 0)
        {
            errorMessage = "目标区域尺寸无效";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TargetData))
        {
            errorMessage = "目标数据不能为空";
            return false;
        }

        // 检查处理方式与数据类型的匹配
        if (IsImageData && !IsImageProcessingMethod)
        {
            errorMessage = $"处理方式'{ProcessingMethod}'不适用于图像数据";
            return false;
        }

        if (!IsImageData && !IsTextProcessingMethod)
        {
            errorMessage = $"处理方式'{ProcessingMethod}'不适用于文本数据";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
    /// <summary>
    /// 二进制文件魔数，用于标识文件格式
    /// </summary>
    private const uint FILE_MAGIC = 0x494C464D; // "ILFM" in hex (Image Label Format)
    /// <summary>
    /// 文件格式版本
    /// </summary>
    private const ushort FILE_VERSION = 0x0100; // Version 1.0

    /// <summary>
    /// 保存到.ILX文件
    /// </summary>
    /// <param name="filePath">完整文件路径，自动添加.ILX后缀</param>
    public void SaveToFile(string filePath)
    {
        // 确保文件后缀为.ILX
        if (!filePath.EndsWith(".ILX", StringComparison.OrdinalIgnoreCase))
        {
            filePath += ".ILX";
        }
        if(!Validate(out var msg))
        {
            throw new ArgumentException(msg);
        }

        using (var fileStream = new FileStream(filePath, FileMode.Create))
        using (var writer = new BinaryWriter(fileStream, Encoding.UTF8))
        {
            // 写入文件头
            writer.Write(FILE_MAGIC);           // 4字节：魔数
            writer.Write(FILE_VERSION);         // 2字节：版本号
            writer.Write((byte)(IsImageData ? 1 : 0)); // 1字节：数据类型标识 (1=图像, 0=文本)
            writer.Write((byte)ProcessingMethod); // 1字节：处理方式
            writer.Write(SimilarityThreshold);  // 8字节：阈值
            
            // 写入区域信息
            TargetRegion.WriteToBinary(writer); // 16字节：x,y,width,height
            SearchRegion.WriteToBinary(writer); // 16字节：搜索区域
            
            // 写入数据长度和数据
            if (IsImageData)
            {
                // 对于图像数据，将Base64解码为原始字节
                byte[] imageBytes = Convert.FromBase64String(TargetData);
                writer.Write(imageBytes.Length); // 4字节：数据长度
                writer.Write(imageBytes);       // N字节：图像数据
            }
            else
            {
                // 对于文本数据，直接编码为UTF8
                byte[] textBytes = Encoding.UTF8.GetBytes(TargetData);
                writer.Write(textBytes.Length); // 4字节：数据长度
                writer.Write(textBytes);       // N字节：文本数据
            }
            
            // 写入文件结束标记（可选，用于验证文件完整性）
            writer.Write((byte)0xFF); // 1字节：结束符
        }
    }

    /// <summary>
    /// 从.ILX文件加载
    /// </summary>
    /// <param name="filePath">完整文件路径</param>
    public static ImageLabelX LoadFromFile(string filePath)
    {
        // 检查文件后缀
        if (!filePath.EndsWith(".ILX", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("仅支持.ILX格式文件", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("标签文件不存在", filePath);
        }

        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(fileStream, Encoding.UTF8))
        {
            // 读取并验证文件头
            uint magic = reader.ReadUInt32();
            if (magic != FILE_MAGIC)
            {
                throw new InvalidDataException("无效的文件格式：魔数不匹配");
            }

            ushort version = reader.ReadUInt16();
            if (version != FILE_VERSION)
            {
                throw new InvalidDataException($"不支持的版本号：{version >> 8}.{version & 0xFF}");
            }

            byte dataType = reader.ReadByte();    // 数据类型
            SearchMethod method = (SearchMethod)reader.ReadByte(); // 处理方式
            double threshold = reader.ReadDouble(); // 阈值
            
            // 读取区域信息
            var region = RectangleFact.ReadFromBinary(reader);
            var searchRegion = RectangleFact.ReadFromBinary(reader);
            
            // 读取数据长度和数据
            int dataLength = reader.ReadInt32();
            byte[] dataBytes = reader.ReadBytes(dataLength);
            
            // 验证数据长度
            if (dataBytes.Length != dataLength)
            {
                throw new EndOfStreamException("文件数据不完整");
            }
            
            // 验证结束标记
            if (fileStream.Position < fileStream.Length)
            {
                byte endMagic = reader.ReadByte();
                if (endMagic != 0xF1F)
                {
                    System.Diagnostics.Debug.WriteLine($"警告：文件结束标记不匹配，但继续处理...");
                }
            }

            // 根据数据类型创建字符串
            string targetData;
            if (dataType == 1)
            {
                // 图像数据：将字节编码为Base64
                targetData = Convert.ToBase64String(dataBytes);
            }
            else
            {
                // 文本数据：解码为UTF8字符串
                targetData = Encoding.UTF8.GetString(dataBytes);
            }

            // 创建并返回对象
            return new ImageLabelX
            {
                TargetRegion = region,
                SearchRegion = searchRegion,
                TargetData = targetData,
                ProcessingMethod = method,
                SimilarityThreshold = threshold
            };
        }
    }

    /// <summary>
    /// 检查是否为图像数据（Base64编码）
    /// </summary>
    public bool IsImageData => IsBase64String(TargetData);

    /// <summary>
    /// 高效检查字符串是否为有效的Base64格式
    /// </summary>
    private static bool IsBase64String(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Length % 4 != 0) return false;

        try
        {
            // 尝试转换验证
            _ = Convert.FromBase64String(s);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
/// <summary>
/// 表示图像处理的目标区域
/// </summary>
static class RectangleFact
{
    /// <summary>
    /// 将区域数据写入二进制流
    /// </summary>
    public static void WriteToBinary(this Rectangle rect, BinaryWriter writer)
    {
        writer.Write(rect.X);
        writer.Write(rect.Y);
        writer.Write(rect.Width);
        writer.Write(rect.Height);
    }

    /// <summary>
    /// 从二进制流读取区域数据
    /// </summary>
    public static Rectangle ReadFromBinary(BinaryReader reader)
    {
        return new Rectangle(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32()
        );
    }
}
