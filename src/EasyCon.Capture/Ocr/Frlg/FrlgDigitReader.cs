// Ported from PokemonAutomation's FRLG DigitReader and ExactImageMatcher.
// Copyright (c) 2021 Alexander J. Yee. MIT notice: docs/licenses/PokemonAutomation-MIT.txt.
// Source revisions and resource hashes: docs/frlg-ocr-resources.lock.json.
using EzCv;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EasyCon.Capture.Ocr.Frlg;

internal static class FrlgDigitReader
{
    private sealed record DigitTemplate(int Width, int Height, byte[] Pixels, double[] Mean);
    private sealed record Component(Rect Bounds, int Area);

    private static readonly Lazy<DigitTemplate[]> _dialogTemplates = new(() => LoadTemplates("DialogDigits"));
    private static readonly Lazy<DigitTemplate[]> _statTemplates = new(() => LoadTemplates("Digits"));
    private static readonly Lazy<DigitTemplate[]> _levelTemplates = new(() => LoadTemplates("LevelDigits"));
    private static readonly int[] _thresholds = [175, 190, 205];
    private const double MaxRmsd = 85;
    private const double MinMargin = 8;

    // Cached managed pixel buffers own no native handles. Per-read Mats are always disposed.
    private static DigitTemplate[] LoadTemplates(string family)
    {
        Assembly assembly = typeof(FrlgDigitReader).Assembly;
        DigitTemplate[] templates = new DigitTemplate[10];
        for (int digit = 0; digit < 10; digit++)
        {
            string name = $"Frlg/{family}/{digit}.png";
            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidDataException($"Missing embedded FRLG template: {name}");
            using MemoryStream bytes = new();
            stream.CopyTo(bytes);
            using Mat image = Mat.FromImageData(bytes.ToArray());
            byte[] pixels = ReadPixels(image);
            templates[digit] = new DigitTemplate(image.Width, image.Height, pixels, Mean(pixels));
        }
        return templates;
    }

    internal static FrlgReadAttempt[] Read(Mat image, string? debugDirectory, FrlgSceneDefinition definition)
    {
        using Mat prepared = image.Clone();
        if (definition.Kind == "level")
        {
            byte[] pixels = ReadPixels(prepared);
            for (int i = 0; i < pixels.Length; i += 3)
            {
                int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                if (b > g + 25 && r > g + 15)
                { pixels[i] = 240; pixels[i + 1] = 176; pixels[i + 2] = 209; }
                else if (r > 200 && g > 200 && b > 200)
                { pixels[i] = pixels[i + 1] = pixels[i + 2] = 0; }
            }
            CopyPixels(prepared, pixels, 3);
        }
        using Mat firstBlur = new();
        using Mat blurred = new();
        Cv2.GaussianBlur(prepared, firstBlur, new Size(5, 5), 1.5);
        Cv2.GaussianBlur(firstBlur, blurred, new Size(5, 5), 1.5);
        byte[] blurredPixels = ReadPixels(blurred);
        byte[] original = ReadPixels(prepared);
        if (debugDirectory != null)
        {
            Directory.CreateDirectory(debugDirectory);
            File.WriteAllBytes(Path.Combine(debugDirectory, "normalized.png"), image.ToBytes());
            File.WriteAllBytes(Path.Combine(debugDirectory, "blurred.png"), blurred.ToBytes());
        }
        int[] thresholds = definition.Kind == "level" ? [112, 127, 142] : _thresholds;
        return thresholds.Select(threshold => ReadThreshold(prepared, original, blurredPixels, threshold, debugDirectory, definition)).ToArray();
    }

    private static FrlgReadAttempt ReadThreshold(Mat image, byte[] original, byte[] blurred, int threshold, string? debugDirectory,
        FrlgSceneDefinition definition)
    {
        int width = image.Width;
        int height = image.Height;
        bool[] foreground = new bool[width * height];
        for (int i = 0; i < foreground.Length; i++)
            foreground[i] = blurred[3 * i] <= threshold && blurred[3 * i + 1] <= threshold && blurred[3 * i + 2] <= threshold;
        if (debugDirectory != null)
        {
            using Mat binary = new(height, width, MatType.CV_8UC1);
            byte[] mask = foreground.Select(value => value ? (byte)0 : (byte)255).ToArray();
            CopyPixels(binary, mask, 1);
            File.WriteAllBytes(Path.Combine(debugDirectory, $"binary-{threshold}.png"), binary.ToBytes());
        }
        List<Component> allComponents = Components(foreground, width, height);
        Component[] components = allComponents
            .Where(c => c.Area >= 4 && c.Bounds.Width >= 5 && c.Bounds.Height >= 15)
            .OrderBy(c => c.Bounds.X).ToArray();
        List<FrlgDigitMatch> matches = [];
        FrlgReadAttempt Fail(string reason) => new(threshold, "", reason, matches.ToArray());
        if (allComponents.Count > 128)
            return Fail("too-many-components");
        int maxDigits = definition.Kind == "tid" ? 5 : definition.Kind == "hp" ? 7 : 3;
        if (components.Length == 0 || components.Length > maxDigits)
            return Fail("component-count");
        int? slashIndex = null;
        foreach (Component component in components)
        {
            Rect box = component.Bounds;
            if (box.X == 0 || box.Y == 0 || box.X + box.Width == width || box.Y + box.Height == height)
                return Fail("clipped-glyph");
            if (definition.Kind == "hp" && IsSlash(original, width, box))
            {
                if (slashIndex != null) return Fail("multiple-hp-separators");
                slashIndex = matches.Count;
                continue;
            }
            int expectedDigits = Math.Max(1, (int)Math.Ceiling((double)box.Width / box.Height / 0.6 - 0.5));
            if (expectedDigits > maxDigits || matches.Count + expectedDigits > maxDigits)
                return Fail("merged-component-count");
            int splitWidth = box.Width / expectedDigits;
            for (int split = 0; split < expectedDigits; split++)
            {
                int x = box.X + split * splitWidth;
                int right = split == expectedDigits - 1 ? box.X + box.Width : x + splitWidth;
                Rect glyph = Tighten(original, width, new Rect(x, box.Y, right - x, box.Height));
                using Mat crop = new(image, glyph);
                DigitTemplate[] templates = definition.Kind == "level" ? _levelTemplates.Value
                    : definition.Kind is "stat" or "hp" ? _statTemplates.Value : _dialogTemplates.Value;
                (int Digit, double Score)[] scores = templates
                    .Select((template, digit) => (Digit: digit, Score: Rmsd(crop, template)))
                    .OrderBy(item => item.Score).ToArray();
                FrlgDigitMatch match = new(scores[0].Digit, glyph, scores[0].Score, scores[1].Score);
                matches.Add(match);
                if (debugDirectory != null)
                    File.WriteAllBytes(Path.Combine(debugDirectory, $"digit-{threshold}-{matches.Count}.png"), crop.ToBytes());
                // Never silently drop a digit and concatenate the remainder.
                if (match.Rmsd > (definition.Kind == "tid" ? MaxRmsd : 105))
                    return Fail("poor-template-match");
                if (!HasEnoughSeparation(definition.Kind, match.Rmsd, match.RunnerUpRmsd))
                    return Fail("ambiguous-digit");
            }
        }
        if (matches.Count == 0 || definition.Kind == "tid" && matches.Count != 5)
            return Fail("digit-count");
        int minHeight = matches.Min(d => d.Bounds.Height);
        int maxHeight = matches.Max(d => d.Bounds.Height);
        if (minHeight < maxHeight * 0.65 || matches.Max(d => d.Bounds.Y) - matches.Min(d => d.Bounds.Y) > maxHeight * 0.25)
            return Fail("inconsistent-glyph-layout");
        for (int i = 1; i < matches.Count; i++)
        {
            Rect previous = matches[i - 1].Bounds;
            if (matches[i].Bounds.X < previous.X + previous.Width)
                return Fail("overlapping-glyphs");
        }
        string text = string.Concat(matches.Select(d => (char)('0' + d.Digit)));
        string failure;
        if (definition.Kind == "tid") failure = FrlgOcr.ValidateDigits(text);
        else
        {
            if (slashIndex is int separator)
            {
                if (separator < 1 || separator > 3 || text.Length - separator is < 1 or > 3)
                    return Fail("invalid-hp-pair");
                string current = text[..separator];
                text = text[separator..];
                if (int.Parse(current) > int.Parse(text)) return Fail("hp-current-exceeds-maximum");
            }
            failure = text.Length > 3 || text.Length > 1 && text[0] == '0' ? "invalid-number-length"
                : !int.TryParse(text, out int value) || value < definition.Minimum || value > definition.Maximum
                    ? "number-out-of-range" : "";
        }
        return new FrlgReadAttempt(threshold, failure.Length == 0 ? text : "", failure, matches.ToArray());
    }

    internal static bool HasEnoughSeparation(string kind, double rmsd, double runnerUpRmsd)
    {
        if (runnerUpRmsd - rmsd >= MinMargin) return true;
        // The FRLG stat-font 8 is visually close to its runner-up template, even on a clean crop.
        // Keep a low-error reading as a vote; FrlgOcr still requires the same complete value from
        // at least two thresholds. TID and level retain the stricter per-glyph separation rule.
        return kind is "stat" or "hp" && rmsd <= 70;
    }

    private static bool IsSlash(byte[] pixels, int width, Rect box)
    {
        // A slash is a thin, consistently diagonal stroke; digit 1 is vertical and 2 has horizontal caps.
        List<(double X, double Y)> rows = [];
        for (int y = box.Y; y < box.Y + box.Height; y++)
        {
            List<int> xs = [];
            for (int x = box.X; x < box.X + box.Width; x++)
            {
                int i = (y * width + x) * 3;
                if (pixels[i] < 140 && pixels[i + 1] < 140 && pixels[i + 2] < 140) xs.Add(x);
            }
            if (xs.Count > 0) rows.Add((xs.Average(), y));
        }
        if (rows.Count < box.Height * .7) return false;
        double meanX = rows.Average(p => p.X), meanY = rows.Average(p => p.Y);
        double cov = rows.Sum(p => (p.X - meanX) * (p.Y - meanY));
        double varX = rows.Sum(p => (p.X - meanX) * (p.X - meanX));
        double varY = rows.Sum(p => (p.Y - meanY) * (p.Y - meanY));
        return varX > 0 && varY > 0 && cov / varY < -.25 && cov * cov / (varX * varY) > .92;
    }

    private static List<Component> Components(bool[] pixels, int width, int height)
    {
        List<Component> components = [];
        int[] queue = new int[pixels.Length];
        for (int start = 0; start < pixels.Length; start++)
        {
            if (!pixels[start])
                continue;
            int head = 0;
            int tail = 1;
            queue[0] = start;
            pixels[start] = false;
            int minX = start % width, maxX = minX, minY = start / width, maxY = minY;
            while (head < tail)
            {
                int pos = queue[head++];
                int x = pos % width;
                int y = pos / width;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                if (x > 0) Visit(pos - 1);
                if (x + 1 < width) Visit(pos + 1);
                if (y > 0) Visit(pos - width);
                if (y + 1 < height) Visit(pos + width);
            }
            components.Add(new Component(new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1), tail));
            if (components.Count > 128)
                break;

            void Visit(int pos)
            {
                if (!pixels[pos])
                    return;
                pixels[pos] = false;
                queue[tail++] = pos;
            }
        }
        return components;
    }

    private static Rect Tighten(byte[] pixels, int strideWidth, Rect box)
    {
        int minBrightness = 765, maxBrightness = 0;
        for (int y = box.Y; y < box.Y + box.Height; y++)
            for (int x = box.X; x < box.X + box.Width; x++)
            {
                int brightness = Brightness(x, y);
                minBrightness = Math.Min(minBrightness, brightness);
                maxBrightness = Math.Max(maxBrightness, brightness);
            }
        int threshold = (minBrightness + maxBrightness) / 2;
        int minX = box.X + box.Width, minY = box.Y + box.Height, maxX = -1, maxY = -1;
        for (int y = box.Y; y < box.Y + box.Height; y++)
            for (int x = box.X; x < box.X + box.Width; x++)
            {
                if (Brightness(x, y) > threshold)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        if (maxX < minX || maxY < minY)
            return box;
        minX = Math.Max(box.X, minX - 1);
        minY = Math.Max(box.Y, minY - 1);
        maxX = Math.Min(box.X + box.Width, maxX + 2);
        maxY = Math.Min(box.Y + box.Height, maxY + 2);
        return new Rect(minX, minY, maxX - minX, maxY - minY);

        int Brightness(int x, int y)
        {
            int i = (y * strideWidth + x) * 3;
            return pixels[i] + pixels[i + 1] + pixels[i + 2];
        }
    }

    private static double Rmsd(Mat crop, DigitTemplate template)
    {
        using Mat scaled = new();
        Cv2.Resize(crop, scaled, new Size(template.Width, template.Height));
        byte[] pixels = ReadPixels(scaled);
        double[] mean = Mean(pixels);
        double[] factors = Enumerable.Range(0, 3).Select(c =>
            template.Mean[c] == 0 ? 1.0 : Math.Clamp(mean[c] / template.Mean[c], 0.85, 1.15)).ToArray();
        double squares = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            double delta = Math.Clamp(template.Pixels[i] * factors[i % 3], 0, 255) - pixels[i];
            squares += delta * delta;
        }
        return Math.Sqrt(squares / pixels.Length);
    }

    private static double[] Mean(byte[] pixels)
    {
        double[] mean = new double[3];
        for (int i = 0; i < pixels.Length; i++)
            mean[i % 3] += pixels[i];
        for (int c = 0; c < 3; c++)
            mean[c] /= pixels.Length / 3;
        return mean;
    }

    internal static byte[] ReadPixels(Mat image)
    {
        int rowBytes = image.Width * 3;
        byte[] pixels = new byte[rowBytes * image.Height];
        IntPtr data = image.Data;
        int step = checked((int)image.Step());
        for (int row = 0; row < image.Height; row++)
            Marshal.Copy(IntPtr.Add(data, row * step), pixels, row * rowBytes, rowBytes);
        GC.KeepAlive(image);
        return pixels;
    }

    internal static void CopyPixels(Mat image, byte[] pixels, int channels)
    {
        int rowBytes = image.Width * channels;
        IntPtr data = image.Data;
        int step = checked((int)image.Step());
        for (int row = 0; row < image.Height; row++)
            Marshal.Copy(pixels, row * rowBytes, IntPtr.Add(data, row * step), rowBytes);
        GC.KeepAlive(image);
    }
}