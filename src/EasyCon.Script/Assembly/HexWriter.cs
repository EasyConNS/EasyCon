using System.Text;

namespace EasyCon.Script.Assembly;

public static class HexWriter
{
    public static readonly byte[] EmptyAsm = [0xFF, 0xFF];
    public static string WriteHex(string hexStr, byte[] asmBytes, int size, int version)
    {
        if (asmBytes.Length > size)
            throw new AssembleException("烧录失败！字节超出限制");
        var list = new List<byte>(asmBytes);
        // load hex file
        var lines = hexStr.Split('\r', '\n');
        var hexFile = new List<IntelHex>();
        foreach (var line in lines)
        {
            try
            {
                var hex = IntelHex.Parse(line);
                if (hex != null)
                    hexFile.Add(hex);
            }
            catch (AssembleException ex)
            {
                throw new AssembleException("固件读取失败！" + ex.Message);
            }
        }
        // find data position
        int index = hexFile.FindIndex(hex =>
        {
            if (hex.Data.Length < 0x10)
                return false;
            for (int i = 0; i < hex.Data.Length; i++)
                if (i < 2 && hex.Data[i] != 0xFF || i > 2 && hex.Data[i] != 0)
                    return false;
            return true;
        });
        if (index == -1)
            throw new AssembleException("固件读取失败！未找到数据结构");
        int ver = hexFile[index].Data[2];
        if (ver < version)
            throw new AssembleException("固件版本不符，请使用最新版的固件");
        // write data from asmBytes
        for (int i = 0; i < list.Count;)
        {
            int len = Math.Min(hexFile[index].DataSize, list.Count - i);
            byte[] data = hexFile[index].Data.ToArray();
            list.CopyTo(i, data, 0, len);
            hexFile[index].WriteData(data);
            i += len;
            index++;
        }
        // get string
        var str = new StringBuilder();
        foreach (var hex in hexFile)
            str.AppendLine(hex.ToString());
        return str.ToString();
    }
}

class IntelHex
{
    public byte DataSize { get; private set; }
    public ushort StartAddress { get; private set; }
    public byte RecordType { get; private set; }
    public byte[] Data { get; private set; }
    public byte Checksum { get; private set; }

    public static IntelHex Parse(string line)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;
            line = line.Trim();
            if (line[0] != ':')
                throw new AssembleException("需要以“:”作为起始");
            var bytes = new List<byte>();
            for (int i = 1; i < line.Length; i += 2)
                bytes.Add(Convert.ToByte(line.Substring(i, 2), 16));
            var hex = new IntelHex();
            int index = 0;
            hex.DataSize = bytes[index++];
            hex.StartAddress = (ushort)(bytes[index++] << 8);
            hex.StartAddress = (ushort)(hex.StartAddress | bytes[index++]);
            hex.RecordType = bytes[index++];
            hex.Data = bytes.GetRange(index, hex.DataSize).ToArray();
            index += hex.DataSize;
            hex.Checksum = bytes[index++];
            if (index != bytes.Count)
                throw new AssembleException("数据过多");
            return hex;
        }
        catch (IndexOutOfRangeException)
        {
            throw new AssembleException("数据过少");
        }
    }

    void FixChecksum()
    {
        Checksum = 0;
        Checksum -= DataSize;
        Checksum -= (byte)(StartAddress >> 8);
        Checksum -= (byte)(StartAddress & 0xFF);
        Checksum -= RecordType;
        foreach (var b in Data)
            Checksum -= b;
    }

    public void WriteData(byte[] data)
    {
        DataSize = (byte)data.Length;
        Data = data;
        FixChecksum();
    }

    public byte[] GetBytes()
    {
        var bytes = new List<byte>
        {
            DataSize,
            (byte)(StartAddress >> 8),
            (byte)(StartAddress & 0xFF),
            RecordType
        };
        bytes.AddRange(Data);
        bytes.Add(Checksum);
        return bytes.ToArray();
    }

    public string GetString()
    {
        return ":" + string.Join("", GetBytes().Select(b => b.ToString("X2")));
    }

    public override string ToString()
    {
        return GetString();
    }
}
