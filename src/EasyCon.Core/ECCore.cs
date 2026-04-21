using EasyCon.Capture;
using System.Collections.Immutable;

namespace EasyCon.Core;

public partial class ECCore
{
    public const string ImgDir = "ImgLabel";
    public static IEnumerable<(string name, int index)> GetCaptureSources() => ECCapture.GetCaptureCamera();

    public static IEnumerable<(string, int)> GetCaptureTypes() => ECCapture.GetCaptureTypes();

    public static List<string> GetDeviceNames() => EasyDevice.ECDevice.GetPortNames();

    public static IEnumerable<SearchMethod> GetSearchMethods() => ECSearch.GetEnableSearchMethods();

    public static (IEnumerable<ImgLabel>, int, int) LoadImgLabels(params string[] paths)
    {
        var ILs = ImmutableArray.CreateBuilder<ImgLabel>();
        var set = new HashSet<string>();
        var total = 0;
        var rept = 0;

        foreach (var path in paths)
        {
            var ilpath = Path.Combine(path, ImgDir);
            if (!Directory.Exists(ilpath)) continue;

            foreach (var file in Directory.GetFiles(ilpath, "*.IL"))
            {
                total++;
                try
                {
                    var il = ECSearch.LoadIL(file);
                    if (!set.Add(il.name))
                    {
                        rept++;
                        Console.WriteLine($"重复标签:{il.name}, 路径：{file}");
                        continue;
                    }
                    ILs.Add(il);
                }
                catch
                {
                    Console.WriteLine($"[!错误!]无法加载标签文件:{file}");
                }
            }
        }
        return (ILs, total, rept);
    }
}