using EasyCon.Capture;
using EasyCon.Core.Maa;
using OpenCvSharp;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) { Console.Error.WriteLine("usage: MaaReplayCheck <source-package> <output> <screenshots>"); return 2; }
string sourceRoot = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
Dictionary<string, string> SourceDigests() => Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
    .Where(path => Path.GetExtension(path).Equals(".IL", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".ecs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".flow.json", StringComparison.OrdinalIgnoreCase))
    .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(sourceRoot, path), path => Hash(File.ReadAllBytes(path)));
Dictionary<string, string> before = SourceDigests();
File.WriteAllText(Path.Combine(output, "sources.before.json"), JsonSerializer.Serialize(before, new JsonSerializerOptions { WriteIndented = true }));
LabelDiscovery discovery = LegacyLabelCatalog.Scan([sourceRoot], recursive: true);
LabelConversionResult conversion = MaaLabelConverter.Build(discovery, Path.Combine(output, "cache"), LegacyCompatibilityProfile.LegacyGui);
LabelGeneration generation = conversion.Generation!;
File.WriteAllText(Path.Combine(output, "conversion.json"), JsonSerializer.Serialize(new { generation.Id, generation.Directory, conversion.Report }, new JsonSerializerOptions { WriteIndented = true }));
using MaaVisionService vision = new(generation);
LabelGeneration cliGeneration = MaaLabelConverter.Build(discovery, Path.Combine(output, "cache-cli"), LegacyCompatibilityProfile.LegacyCli).Generation!;
using MaaVisionService cli = new(cliGeneration);
using StreamWriter details = new(Path.Combine(output, "replay.jsonl"));
List<object> frames = [];
List<object> errors = [];
List<object> memory = [];
Dictionary<string, int> routes = new();
HashSet<string> cliChecked = [];
int comparisons = 0, integerDifferences = 0, floatDifferences = 0, boxDifferences = 0, nonfinite = 0;
int nativeDifferences = 0, nativeIntegerDifferences = 0;
double maximumRawDifference = 0;
Stopwatch elapsed = Stopwatch.StartNew();
void Memory()
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    using Process process = Process.GetCurrentProcess(); process.Refresh();
    memory.Add(new
    {
        comparisons,
        managed = GC.GetTotalMemory(true),
        privateBytes = process.PrivateMemorySize64,
        threads = process.Threads.Count,
        retainedResults = vision.Session.RetainedResults,
        queued = vision.Session.QueuedRequests,
        activeResourceVersions = MaaVisionService.ActiveResourceVersions
    });
}
Memory();
string screenshots = Path.GetFullPath(args[2]);
foreach (string file in Directory.GetFiles(screenshots, "*.png").Order(StringComparer.Ordinal))
{
    byte[] png = File.ReadAllBytes(file);
    using Mat image = Cv2.ImDecode(png, ImreadModes.Color);
    frames.Add(new { file, image.Width, image.Height, pngSha256 = Hash(png), normalization = "already-BGR8/1920x1080-no-resize" });
    foreach (MaaLabelEntry entry in generation.Labels.Values.Where(entry => entry.Error == null))
    {
        ImgLabel label = entry.ReadLabel();
        try
        {
            System.Drawing.Point location = label.Search(image, out double expected, "")[0];
            if (!double.IsFinite(expected))
            {
                try { vision.MatchImage(entry.Name, png); errors.Add(new { frame = file, label = entry.Name, error = "nonfinite baseline silently accepted" }); }
                catch (InvalidOperationException ex) when (ex.ToString().Contains("NaN/Infinity")) { nonfinite++; }
                continue;
            }
            LegacyRecognitionTrace actual = vision.MatchImage(entry.Name, png);
            int expectedGui = (int)expected, expectedCli = (int)Math.Ceiling(expected);
            int actualCli = LegacyAlgorithmAdapter.ToLegacyInteger(new(actual.Raw, default, entry.Method), LegacyCompatibilityProfile.LegacyCli);
            bool floatEqual = actual.LegacyFloat == expected;
            bool integerEqual = actual.LegacyInteger == expectedGui && actualCli == expectedCli;
            bool boxEqual = actual.Box[0] == location.X + label.RangeX && actual.Box[1] == location.Y + label.RangeY;
            if (!floatEqual) floatDifferences++;
            if (!integerEqual) integerDifferences++;
            if (!boxEqual) boxDifferences++;
            if (actual.NativeRaw is double native)
            {
                double difference = Math.Abs(native - actual.Raw);
                if (difference != 0) nativeDifferences++;
                maximumRawDifference = Math.Max(maximumRawDifference, difference);
                RawMatchResult raw = new(native, default, entry.Method);
                if (LegacyAlgorithmAdapter.ToLegacyInteger(raw, LegacyCompatibilityProfile.LegacyGui) != expectedGui ||
                    LegacyAlgorithmAdapter.ToLegacyInteger(raw, LegacyCompatibilityProfile.LegacyCli) != expectedCli) nativeIntegerDifferences++;
            }
            if (cliChecked.Add(((int)entry.Method).ToString() + ":" + Path.GetFileName(file)))
            {
                LegacyRecognitionTrace actualCliTrace = cli.MatchImage(entry.Name, png);
                if (actualCliTrace.LegacyInteger != expectedCli) errors.Add(new { frame = file, label = entry.Name, error = "CLI service integer mismatch" });
            }
            routes[actual.Route] = routes.GetValueOrDefault(actual.Route) + 1;
            details.WriteLine(JsonSerializer.Serialize(new
            {
                frame = Path.GetFileName(file),
                label = entry.Name,
                method = (int)entry.Method,
                expected,
                expectedGui,
                expectedCli,
                actualCli,
                floatEqual,
                integerEqual,
                boxEqual,
                trace = actual
            }));
            comparisons++;
            if (comparisons % 100 == 0) { details.Flush(); Memory(); Console.WriteLine($"replay {comparisons}, errors={errors.Count}, integerDiff={integerDifferences}, elapsed={elapsed.Elapsed}"); }
        }
        catch (Exception ex) { errors.Add(new { frame = file, label = entry.Name, error = ex.ToString() }); }
        finally { label.ImgBase64 = ""; }
    }
}
Memory();
Dictionary<string, string> after = SourceDigests();
File.WriteAllText(Path.Combine(output, "sources.after.json"), JsonSerializer.Serialize(after, new JsonSerializerOptions { WriteIndented = true }));
bool unchanged = before.Count == after.Count && before.All(item => after.GetValueOrDefault(item.Key) == item.Value);
object summary = new
{
    atUtc = DateTimeOffset.UtcNow,
    runtime = MaaEnvironment.RuntimeVersion,
    conversion.Report,
    sourceFileCount = before.Count,
    sourceFilesUnchanged = unchanged,
    frames,
    comparisons,
    floatDifferences,
    integerDifferences,
    boxDifferences,
    nonfiniteBaselineDiagnosed = nonfinite,
    nativeDifferences,
    nativeIntegerDifferences,
    maximumRawDifference,
    cliServiceMethodFrameChecks = cliChecked.Count,
    routes,
    memory,
    errors,
    elapsed = elapsed.Elapsed,
    positiveAndNearNegativeCoverageForEveryLabel = "pending",
    realHardwareAndGameClosedLoop = "not performed"
};
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"finished comparisons={comparisons}, floatDiff={floatDifferences}, intDiff={integerDifferences}, boxDiff={boxDifferences}, errors={errors.Count}, unchanged={unchanged}");
return comparisons > 0 && errors.Count == 0 && floatDifferences == 0 && integerDifferences == 0 && boxDifferences == 0 && unchanged ? 0 : 1;