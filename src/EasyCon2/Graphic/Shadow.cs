using EasyDevice;
using System.IO;
using System.Security.Cryptography;

namespace EasyCon2.Graphic;

public class Shadow
{
    const string ShadowPath = @"Data\Shadows.bin";
    const string CapturedPath = ImageRes.ImagePath + @"CapturedShadows\";
    public const int X = 220;
    public const int Y = 232;
    public const int PW = 8;
    public const int PH = 8;
    public const int PX = 64;
    public const int PY = 64;
    public const int W = PW * PX;
    public const int H = PH * PY;
    public const int BytesLength = PX * PY / 8;
    public const int MD5Length = 16;

    static readonly Dictionary<string, List<Shadow>> _dict = new Dictionary<string, List<Shadow>>();
    static readonly Dictionary<string, Bitmap> _captured = new Dictionary<string, Bitmap>();
    static readonly HashSet<int> _genderdiff = new HashSet<int>();

    public readonly int ID;
    public readonly char Variance;
    public readonly string Hash;
    public readonly byte[] HashBytes;
    public readonly Bitmap Image;

    static Shadow()
    {
        Load();
        LoadCaptured();
    }

    public Shadow(Bitmap image)
    {
        // temporary
        ID = -1;
        Variance = '\0';
        HashBytes = GetHashBytes(image);
        Hash = BytesToString(HashBytes);
        Image = image;
        if (!_dict.ContainsKey(Hash))
            SaveCaptured(image, Hash);
    }

    public Shadow(int id, char v, byte[] bytes, Bitmap image = null)
    {
        // loaded
        ID = id;
        Variance = v;
        HashBytes = bytes;
        Hash = BytesToString(HashBytes);
        Image = image;
    }

    public Shadow(int id, char v, Bitmap image)
    {
        // new
        ID = id;
        Variance = v;
        HashBytes = GetHashBytes(image);
        Hash = BytesToString(HashBytes);
        Image = image;
    }

    byte[] GetHashBytes(Bitmap image)
    {
        int[,] data = new int[PX, PY];
        for (int x = 0; x < PX; x++)
            for (int y = 0; y < PY; y++)
                data[x, y] = image.GetPixel(x * PW, y * PH).Compare(Color.FromArgb(0, 0, 0)) > 0.95 ? 1 : 0;
        return MD5.HashData(DataToBytes(data));
    }

    string BytesToString(byte[] bytes)
    {
        return string.Join(string.Empty, bytes.Select(b => b.ToString("X2")));
    }

    public static byte[] DataToBytes(int[,] data)
    {
        List<byte> bytes = new List<byte>(BytesLength);
        byte b = 0;
        int c = 0;
        for (int i = 0; i < data.GetLength(0); i++)
            for (int j = 0; j < data.GetLength(1); j++)
            {
                b = (byte)(b | data[i, j] << c);
                c++;
                if (c >= 8)
                {
                    bytes.Add(b);
                    b = 0;
                    c = 0;
                }
            }
        return bytes.ToArray();
    }

    public static int[,] BytesToData(byte[] bytes)
    {
        int[,] data = new int[PX, PY];
        int c = 0;
        int index = 0;
        for (int i = 0; i < data.GetLength(0); i++)
            for (int j = 0; j < data.GetLength(1); j++)
            {
                data[i, j] = bytes[index] & 1;
                bytes[index] = (byte)(bytes[index] >> 1);
                c++;
                if (c >= 8)
                {
                    index++;
                    c = 0;
                }
            }
        return data;
    }

    static void AddToDict(Shadow shadow, bool overwrite = false)
    {
        if (!_dict.ContainsKey(shadow.Hash))
            _dict[shadow.Hash] = new List<Shadow>();
        if (overwrite)
        {
            foreach (var pair in _dict.ToArray())
                pair.Value.RemoveAll(u => u.ID == shadow.ID && u.Variance == shadow.Variance);
            _dict[shadow.Hash].Clear();
        }
        _dict[shadow.Hash].Add(shadow);
        if (shadow.Variance == 'f')
            _genderdiff.Add(shadow.ID);
    }

    static string GetCapturedPath(string hash)
    {
        return CapturedPath + hash + ImageRes.ImageExtension;
    }

    public static void LoadCaptured()
    {
        DirectoryInfo dir = new DirectoryInfo(CapturedPath);
        if (!dir.Exists)
            return;
        foreach (var fi in dir.GetFiles())
            if (fi.Extension == ImageRes.ImageExtension)
            {
                var hash = fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
                if (_dict.ContainsKey(hash))
                    fi.Delete();
                else
                    _captured[hash] = System.Drawing.Image.FromFile(fi.FullName) as Bitmap;
            }
    }

    static void SaveCaptured(Bitmap image, string hash)
    {
        _captured[hash] = image;
        var filename = GetCapturedPath(hash);
        if (!File.Exists(filename))
        {
            Directory.CreateDirectory(CapturedPath);
            image.Save(filename);
        }
    }

    public static void Load()
    {
        if (!File.Exists(ShadowPath))
            return;
        using (var fs = new FileStream(ShadowPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using (BinaryReader reader = new BinaryReader(fs))
            {
                try
                {
                    while (true)
                    {
                        int id = reader.ReadInt32();
                        char v = reader.ReadChar();
                        byte[] bytes = reader.ReadBytes(MD5Length);
                        AddToDict(new Shadow(id, v, bytes));
                    }
                }
                catch
                { }
            }
        }
    }

    public static void Save()
    {
        using (var fs = new FileStream(ShadowPath, FileMode.Create))
        {
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                foreach (var shadows in _dict.Values)
                    foreach (var shadow in shadows)
                    {
                        writer.Write(shadow.ID);
                        writer.Write(shadow.Variance);
                        writer.Write(shadow.HashBytes);
                    }
            }
        }
    }

    public static Shadow Save(int id, char v, Shadow shadow, bool overwrite)
    {
        shadow = new Shadow(id, v, shadow.HashBytes, shadow.Image);
        AddToDict(shadow, overwrite);
        Save();
        _captured.Remove(shadow.Hash);
        return shadow;
    }

    public static Shadow[] Find(string hash)
    {
        if (_dict.ContainsKey(hash))
            return _dict[hash].ToArray();
        return [];
    }

    public static Shadow[] Find(Shadow shadow)
    {
        return Find(shadow.Hash);
    }

    public static bool NameExist(Shadow shadow)
    {
        foreach (var pair in _dict)
            foreach (var s in pair.Value)
                if (s.ID == shadow.ID && s.Variance == shadow.Variance)
                    return true;
        return false;
    }

    public static Shadow[] GetSavedShadows()
    {
        List<Shadow> list = new List<Shadow>();
        foreach (var val in _dict.Values)
            list.AddRange(val);
        return [.. list];
    }

    public static KeyValuePair<string, Bitmap>[] GetCapturedShadows()
    {
        return _captured.ToArray();
    }
}
