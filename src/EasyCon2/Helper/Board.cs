using EasyScript.Assembly;
using EasyScript.Parsing;
using System.IO;
using System.Text.RegularExpressions;

namespace EasyCon2.Helper;

public class Board(string displayname, string corename, int datasize)
{
    public static readonly int Version = 0x45;

    public readonly string DisplayName = displayname;
    public readonly string CoreName = corename;
    public readonly int DataSize = datasize;

    public static Board[] SupportedBoards = [
        new ("Leonardo", "Leonardo", 924),
        new("Teensy 2.0", "Teensy2", 924),
        new("Teensy 2.0++", "Teensy2pp", 3996),
        new ("Beetle", "Beetle", 924),
        new ("Arduino UNO R3", "UNO", 412),
        ];

    public override string ToString()
    {
        return DisplayName;
    }
}

public static class BoardExtension
{
    public static string GenerateFirmware(this Board board, byte[] bytes)
    {
        var FirmwarePath = @"Firmware\";
        File.WriteAllBytes("temp.bin", bytes);
        var filename = GetFirmwareName(board.CoreName);
        if (filename == null)
        {
            throw new ParseException("未找到固件！请确认程序Firmware目录下是否有对应固件文件！", 0);
        }
        var hexStr = File.ReadAllText(FirmwarePath + filename);
        hexStr = HexWriter.WriteHex(hexStr, bytes, board.DataSize, Board.Version);
        filename = filename.Replace(".", "+Script.");
        File.WriteAllText(filename, hexStr);
        return filename;
    }

    private static string GetFirmwareName(string corename, string path = @"Firmware\")
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
            return null;
        var max = 0;
        string filename = null;
        foreach (var fi in dir.GetFiles())
        {
            var m = Regex.Match(fi.Name, $@"^{corename} v(\d+)\.hex$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var ver = int.Parse(m.Groups[1].Value);
                if (ver > max)
                {
                    max = ver;
                    filename = fi.Name;
                }
            }
        }
        return filename;
    }
}