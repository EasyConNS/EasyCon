namespace EasyCon.Core.Capabilities;

/// <summary>图像引用（D4：Base64 PNG 字符串抽象点；未来可扩展原生 image 句柄）。</summary>
public readonly struct ImageRef
{
    public string Base64 { get; }

    public ImageRef(string base64)
    {
        Base64 = base64;
    }

    public static ImageRef FromBase64(string base64) => new(base64);

    public bool IsEmpty => string.IsNullOrEmpty(Base64);
}