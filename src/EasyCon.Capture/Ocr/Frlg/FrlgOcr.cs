using EzCv;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace EasyCon.Capture.Ocr.Frlg;

public sealed record FrlgDigitMatch(int Digit, Rect Bounds, double Rmsd, double RunnerUpRmsd);
public sealed record FrlgReadAttempt(int Threshold, string Text, string Failure, FrlgDigitMatch[] Digits);

/// <summary>Quality is a template separation score, not a calibrated probability.</summary>
public sealed record FrlgReadResult(string Scene, string Text, string Failure, int Quality,
    double ElapsedMilliseconds, FrlgReadAttempt[] Attempts)
{
    public bool Success => Failure.Length == 0 && Text.Length != 0;
    public FrlgTextAttempt[] TextAttempts { get; init; } = [];
}

/// <summary>FRLG scene routing. Coordinates supplied to OCR are capture-frame pixels.</summary>
public static class FrlgOcr
{
    public const string JapaneseTid = "FRLG_JPN_TID";
    public const string EnglishTid = "FRLG_EN_TID";
    public const string Version = "frlg-jpn-r9";

    public static bool IsScene(string scene) => FrlgScenes.Find(scene) != null;

    /// <summary>PokemonAutomation's default Switch game box composed with its TID region.</summary>
    public static Rect DefaultRegion(string scene, int width, int height)
    {
        return FrlgScenes.DefaultRegion(scene, width, height);
    }

    public static FrlgReadResult ReadFrame(Mat? frame, Rect region, string scene, string? debugDirectory = null,
        FrlgTextReader? textReader = null)
    {
        Stopwatch timer = Stopwatch.StartNew();
        FrlgReadResult Fail(string reason) => new(scene, "", reason, 0, timer.Elapsed.TotalMilliseconds, []);
        if (!IsScene(scene))
            return Fail("unsupported-scene");
        if (frame == null || frame.Empty())
            return Fail("no-frame");
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || (long)region.X + region.Width > frame.Width || (long)region.Y + region.Height > frame.Height)
            return Fail("invalid-region");

        // Normalize the font scale before using the upstream 5x5 blur and pixel-size gates.
        double scale = 1080.0 / frame.Height;
        int width = (int)Math.Round(region.Width * scale);
        int height = (int)Math.Round(region.Height * scale);
        if (width < 5 || height < 15 || width > 1500 || height > 400)
            return Fail("region-size-out-of-range");

        using Mat roi = new(frame, region);
        using Mat color = new();
        if (roi.Channels() == 4)
            Cv2.CvtColor(roi, color, ColorConversionCodes.BGRA2BGR);
        else if (roi.Channels() == 1)
            Cv2.CvtColor(roi, color, ColorConversionCodes.GRAY2BGR);
        else if (roi.Channels() != 3)
            return Fail("unsupported-pixel-format");
        using Mat normalized = new();
        Cv2.Resize(roi.Channels() == 3 ? roi : color, normalized, new Size(width, height));

        FrlgSceneDefinition definition = FrlgScenes.Find(scene)!;
        if (!definition.IsText && scene.Contains(':')) return Fail("invalid-scene-options");
        if (definition.Kind == "wild-level")
        {
            if (textReader != null) return textReader.ReadNumber(normalized, scene, debugDirectory);
            using FrlgTextReader reader = new();
            return reader.ReadNumber(normalized, scene, debugDirectory);
        }
        if (definition.IsText)
        {
            if (textReader != null) return textReader.Read(normalized, scene, debugDirectory);
            using FrlgTextReader reader = new();
            return reader.Read(normalized, scene, debugDirectory);
        }
        FrlgReadAttempt[] attempts = FrlgDigitReader.Read(normalized, debugDirectory, definition);
        FrlgReadAttempt[] accepted = attempts.Where(a => a.Failure.Length == 0).ToArray();
        string[] candidates = accepted.Select(a => a.Text).Distinct(StringComparer.Ordinal).ToArray();
        string? confirmed = ConfirmDigitAttempts(attempts, definition.Kind);
        string failure = confirmed != null ? "" : candidates.Length > 1 ? "threshold-conflict"
            : "insufficient-threshold-agreement";
        string text = confirmed ?? "";
        FrlgReadAttempt[] votes = accepted.Where(a => a.Text == text).ToArray();
        int quality = text.Length == 0 ? 0 : (int)Math.Clamp(votes.Min(a => a.Digits.Min(d =>
            (d.RunnerUpRmsd - d.Rmsd) / Math.Max(d.RunnerUpRmsd, 1))) * 100, 0, 100);
        FrlgReadResult result = new(scene, text, failure, quality, timer.Elapsed.TotalMilliseconds, attempts);
        if (debugDirectory != null)
        {
            Directory.CreateDirectory(debugDirectory);
            File.WriteAllBytes(Path.Combine(debugDirectory, "region.png"), roi.ToBytes());
            File.WriteAllText(Path.Combine(debugDirectory, "result.json"),
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        return result;
    }

    internal static string? ConfirmDigitAttempts(FrlgReadAttempt[] attempts, string kind)
    {
        FrlgReadAttempt[] accepted = attempts.Where(a => a.Failure.Length == 0).ToArray();
        string[] confirmed = accepted.GroupBy(a => a.Text, StringComparer.Ordinal)
            .Where(group => group.Count() >= 2).Select(group => group.Key).ToArray();
        if (confirmed.Length != 1) return null;
        // TID and summary level keep the original all-accepted-votes-must-agree rule. Stat glyphs
        // can contain one unstable threshold, so a unique two-of-three result is sufficient.
        return kind is "stat" or "hp" || accepted.All(a => a.Text == confirmed[0]) ? confirmed[0] : null;
    }

    internal static string ValidateDigits(string text)
    {
        if (text.Length != 5)
            return "digit-count";
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value > 65535)
            return "tid-out-of-range";
        return "";
    }
}