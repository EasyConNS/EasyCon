// Paddle preprocessing follows PokemonAutomation ML_PaddleOCRPipeline (MIT).
// Pinned model provenance and notices: docs/FRLG-OCR-SOURCES.md.
using EzCv;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Text;

namespace EasyCon.Capture.Ocr.Frlg;

public sealed record FrlgTextAttempt(string Backend, int Threshold, string Raw, double Confidence,
    string Candidate, int Distance, bool Accepted, string Failure, bool LexiconAccepted = true);

/// <summary>Owns lazily initialized text engines. Calls are serialized; native engines are disposed by the owner.</summary>
public sealed class FrlgTextReader : IDisposable
{
    private readonly object _sync = new();
    private readonly string _modelDirectory;
    private InferenceSession? _paddle;
    private string[]? _characters;
    private IOcrRecognizer? _tesseract;
    private bool _disposed;

    public FrlgTextReader(string? modelDirectory = null)
    {
        string? configured = Environment.GetEnvironmentVariable("EASYCON_FRLG_MODELS");
        _modelDirectory = modelDirectory
            ?? (!string.IsNullOrWhiteSpace(configured) ? configured : null)
            ?? Path.Combine(AppContext.BaseDirectory, "models", "frlg");
    }

    public FrlgReadResult Read(Mat image, string scene, string? debugDirectory = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stopwatch timer = Stopwatch.StartNew();
            List<FrlgTextAttempt> attempts = [];
            FrlgReadResult Result(string text, string failure, int quality = 0) =>
                new(scene, text, failure, quality, timer.Elapsed.TotalMilliseconds, []) { TextAttempts = attempts.ToArray() };
            bool nature = FrlgScenes.Find(scene)!.Kind == "nature";
            string[] targets = FrlgScenes.Targets(scene);
            if (targets.Length > 0 && (nature || !FrlgJapaneseLexicon.ValidTargets(targets)))
                return Result("", "invalid-target-set");
            // Short natures leave the met-level clause inside the fixed-width region.
            // Split only at a word-sized gap, then require a nature descriptor in the retained clause.
            using Mat clause = nature ? FirstNatureClause(image) : image.Clone();
            bool splitClause = clause.Width != image.Width;
            if (TouchesInk(clause)) return Result("", "clipped-text");
            char? gender = nature ? null : DetectGenderMarker(clause);

            using Mat resized = new();
            Cv2.Resize(clause, resized, new Size(Math.Max(1, clause.Width * 69 / clause.Height), 69));
            // White padding keeps blur from turning a complete top diacritic into an apparent clipped glyph.
            using Mat padded = PadWhite(resized, 6);
            using Mat blur = new();
            Cv2.GaussianBlur(padded, blur, new Size(5, 5), 1.5);
            Cv2.GaussianBlur(blur, blur, new Size(5, 5), 1.5);
            List<(int Threshold, Mat Image)> variants = [];
            try
            {
                foreach (int threshold in new[] { 160, 184, 208, 128, 96 })
                {
                    Mat? variant = BinarizeAndCrop(blur, threshold);
                    if (variant == null) continue;
                    variants.Add((threshold, variant));
                    if (debugDirectory != null)
                    {
                        Directory.CreateDirectory(debugDirectory);
                        File.WriteAllBytes(Path.Combine(debugDirectory, $"text-{threshold}.png"), variant.ToBytes());
                    }
                    ReadVariant("PaddleOCR", threshold, variant);
                    FrlgTextAttempt[] strong = attempts.Where(a => a.Accepted && a.Distance == 0 && a.Confidence >= .80).ToArray();
                    if (strong.Length >= 2 && attempts.Where(a => a.Accepted).All(a => a.Candidate == strong[0].Candidate))
                        return Result(strong[0].Candidate, "", (int)(strong.Average(a => a.Confidence) * 100));
                }
                if (variants.Count == 0) return Result("", "no-text");
                FrlgTextAttempt[] paddle = attempts.Where(a => a.Accepted).ToArray();
                string[] primary = paddle.Select(a => a.Candidate).Distinct().ToArray();
                // Require two strong, complete, consistent primary reads; otherwise obtain a second opinion.
                if (primary.Length == 1 && paddle.Count(a => a.Confidence >= .80 && a.Distance == 0) >= 2)
                    return Result(primary[0], "", (int)(paddle.Average(a => a.Confidence) * 100));
                if (!nature && ConfirmedNameFromPrimaryVariants(attempts.ToArray()) is string confirmedName)
                {
                    FrlgTextAttempt[] votes = attempts.Where(a => a.Backend == "PaddleOCR"
                        && a.LexiconAccepted && a.Candidate == confirmedName
                        && a.Failure.Length == 0 && a.Distance <= 1
                        && a.Confidence >= .60).ToArray();
                    return Result(confirmedName, "", (int)(votes.Average(a => a.Confidence) * 100));
                }
                foreach ((int threshold, Mat variant) in variants)
                    ReadVariant("Tesseract", threshold, variant);
                FrlgTextAttempt[] secondary = attempts.Where(a => a.Backend == "Tesseract" && a.Accepted).ToArray();
                if (nature && NatureConfirmedByBothBackends(paddle, secondary))
                    return Result(primary[0], "", (int)(secondary.Average(a => a.Confidence) * 100));
                string[] confirmed = secondary.GroupBy(a => a.Candidate).Where(g => g.Count() >= 2
                    && (primary.Contains(g.Key) || primary.Length == 0 && g.Count(a => a.Distance == 0 && a.Confidence >= .70) >= 2))
                    .Select(g => g.Key).ToArray();
                if (confirmed.Length == 1 && secondary.All(a => a.Candidate == confirmed[0]))
                    return Result(confirmed[0], "", (int)(secondary.Average(a => a.Confidence) * 100));
                return Result("", attempts.All(a => a.Failure.Length > 0) ? "text-backends-unavailable"
                    : primary.Length > 1 || confirmed.Length > 1 ? "text-candidate-conflict" : "insufficient-text-agreement");
            }
            finally { foreach ((int _, Mat variant) in variants) variant.Dispose(); }

            void ReadVariant(string backend, int threshold, Mat variant)
            {
                try
                {
                    OcrRecognizeResult raw = backend == "PaddleOCR" ? Paddle(variant) : Tesseract(variant);
                    string matchText = !nature && gender != null && FrlgJapaneseLexicon.Normalize(raw.Text) == "ニドラン"
                        ? raw.Text + gender : raw.Text;
                    FrlgWordMatch match = FrlgJapaneseLexicon.Match(matchText, nature, targets, splitClause);
                    attempts.Add(new(backend, threshold, raw.Text, raw.Confidence, match.Text, match.Distance,
                        match.Accepted && raw.Confidence >= (match.Distance == 0 ? .55 : .75), "", match.Accepted));
                }
                catch (Exception ex) { attempts.Add(new(backend, threshold, "", 0, "", 99, false, ex.Message)); }
            }
        }
    }

    public FrlgReadResult ReadNumber(Mat image, string scene, string? debugDirectory = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stopwatch timer = Stopwatch.StartNew();
            List<FrlgTextAttempt> attempts = [];
            FrlgReadResult Result(string text, string failure, int quality = 0) =>
                new(scene, text, failure, quality, timer.Elapsed.TotalMilliseconds, []) { TextAttempts = attempts.ToArray() };
            FrlgSceneDefinition definition = FrlgScenes.Find(scene)!;

            using Mat resized = new();
            Cv2.Resize(image, resized, new Size(Math.Max(1, image.Width * 69 / image.Height), 69));
            using Mat padded = PadWhite(resized, 6);
            using Mat blur = new();
            Cv2.GaussianBlur(padded, blur, new Size(5, 5), 1.5);
            Cv2.GaussianBlur(blur, blur, new Size(5, 5), 1.5);
            List<(int Threshold, Mat Image)> variants = [];
            try
            {
                foreach (int threshold in new[] { 160, 184, 208, 128, 96 })
                {
                    Mat? variant = BinarizeAndCrop(blur, threshold);
                    if (variant == null) continue;
                    variants.Add((threshold, variant));
                    if (debugDirectory != null)
                    {
                        Directory.CreateDirectory(debugDirectory);
                        File.WriteAllBytes(Path.Combine(debugDirectory, $"number-{threshold}.png"), variant.ToBytes());
                    }
                    ReadVariant("PaddleOCR", threshold, variant);
                    FrlgTextAttempt[] primary = attempts.Where(a => a.Backend == "PaddleOCR" && a.Accepted).ToArray();
                    string[] candidates = primary.Select(a => a.Candidate).Distinct().ToArray();
                    if (candidates.Length == 1 && primary.Count(a => a.Candidate == candidates[0]) >= 2)
                        return Result(candidates[0], "", (int)(primary.Average(a => a.Confidence) * 100));
                }
                if (variants.Count == 0) return Result("", "no-text");
                foreach ((int threshold, Mat variant) in variants)
                    ReadVariant("Tesseract", threshold, variant);
                FrlgTextAttempt[] accepted = attempts.Where(a => a.Accepted).ToArray();
                string[] confirmed = accepted.GroupBy(a => a.Candidate).Where(g =>
                        g.Select(a => a.Backend).Distinct().Count() >= 2)
                    .Select(g => g.Key).ToArray();
                if (confirmed.Length == 1 && accepted.All(a => a.Candidate == confirmed[0]))
                    return Result(confirmed[0], "", (int)(accepted.Average(a => a.Confidence) * 100));
                return Result("", attempts.All(a => a.Failure.Length > 0) ? "text-backends-unavailable"
                    : accepted.Select(a => a.Candidate).Distinct().Count() > 1 ? "number-candidate-conflict"
                    : "insufficient-number-agreement");
            }
            finally { foreach ((int _, Mat variant) in variants) variant.Dispose(); }

            void ReadVariant(string backend, int threshold, Mat variant)
            {
                try
                {
                    OcrRecognizeResult raw = backend == "PaddleOCR" ? Paddle(variant) : Tesseract(variant);
                    string number = NormalizeNumber(raw.Text, definition);
                    attempts.Add(new(backend, threshold, raw.Text, raw.Confidence, number, 0,
                        number.Length > 0 && raw.Confidence >= .55, ""));
                }
                catch (Exception ex) { attempts.Add(new(backend, threshold, "", 0, "", 99, false, ex.Message)); }
            }
        }
    }

    private static string NormalizeNumber(string raw, FrlgSceneDefinition definition)
    {
        StringBuilder digits = new();
        foreach (char value in raw.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(value)) continue;
            if (value is < '0' or > '9') return "";
            digits.Append(value);
        }
        string text = digits.ToString();
        return text.Length is < 1 or > 3 || text.Length > 1 && text[0] == '0'
            || !int.TryParse(text, out int number) || number < definition.Minimum || number > definition.Maximum ? "" : text;
    }

    internal static bool NameConfirmedByPrimaryVariants(FrlgTextAttempt[] primary)
        => ConfirmedNameFromPrimaryVariants(primary) != null;

    private static string? ConfirmedNameFromPrimaryVariants(FrlgTextAttempt[] primary)
    {
        FrlgTextAttempt[] paddle = primary.Where(a => a.Backend == "PaddleOCR" && a.Failure.Length == 0
            && a.LexiconAccepted && a.Candidate.Length > 0 && a.Distance <= 1).ToArray();
        // Require one complete exact dictionary read. Its independent support may be either stronger
        // than it, or slightly weaker when the exact read itself is already high-confidence. This
        // recovers small dakuten (キングラー) without allowing two merely fuzzy guesses to agree.
        // A lone bad threshold must not veto a candidate confirmed by two independent variants.
        string[] confirmed = paddle.GroupBy(a => a.Candidate).Where(group =>
                group.Any(exact => exact.Distance == 0 && exact.Confidence >= .60
                    && group.Any(support => support.Threshold != exact.Threshold
                        && support.Distance <= 1 && support.Confidence >= .60)))
            .Select(group => group.Key).ToArray();
        return confirmed.Length == 1 ? confirmed[0] : null;
    }

    private OcrRecognizeResult Tesseract(Mat image)
    {
        _tesseract ??= new TesseractEngineFactory().CreateRecognizer("jpn", Path.Combine(_modelDirectory, "tessdata"), "LSTM_ONLY", "SINGLE_LINE");
        return _tesseract.Recognize(image.ToBytes());
    }

    private OcrRecognizeResult Paddle(Mat image)
    {
        if (_paddle == null)
        {
            using SessionOptions options = new() { IntraOpNumThreads = 2, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
            _characters = File.ReadAllLines(Path.Combine(_modelDirectory, "chinese", "dict.txt"));
            _paddle = new InferenceSession(Path.Combine(_modelDirectory, "chinese", "rec.onnx"), options);
        }
        int width = Math.Clamp((int)Math.Round(48.0 * image.Width / image.Height), 24, 2048);
        using Mat resized = new();
        Cv2.Resize(image, resized, new Size(width, 48));
        byte[] pixels = FrlgDigitReader.ReadPixels(resized);
        DenseTensor<float> input = new(new[] { 1, 3, 48, width });
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < width; x++)
                for (int c = 0; c < 3; c++)
                    input[0, c, y, x] = pixels[(y * width + x) * 3 + 2 - c] / 255f;
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output = _paddle.Run(
            new[] { NamedOnnxValue.CreateFromTensor(_paddle.InputMetadata.Keys.First(), input) });
        Tensor<float> tensor = output.First().AsTensor<float>();
        if (tensor.Rank != 3 || tensor.Dimensions[0] != 1 || tensor.Dimensions[2] < _characters!.Length + 1
            || tensor.Dimensions[2] > _characters.Length + 2)
            throw new InvalidDataException("Paddle model and character dictionary shapes disagree.");
        StringBuilder text = new();
        List<double> confidence = [];
        float[] scores = tensor.ToArray();
        int classes = tensor.Dimensions[2];
        int previous = 0;
        for (int t = 0; t < tensor.Dimensions[1]; t++)
        {
            int offset = t * classes;
            int best = 0;
            for (int c = 1; c < classes; c++)
                if (scores[offset + c] > scores[offset + best]) best = c;
            if (best != 0 && best != previous)
            {
                text.Append(best <= _characters.Length ? _characters[best - 1] : " ");
                confidence.Add(scores[offset + best]);
            }
            previous = best;
        }
        return new(text.ToString(), confidence.Count == 0 ? 0 : (float)confidence.Average());
    }

    private static Mat? BinarizeAndCrop(Mat image, int threshold)
    {
        byte[] pixels = FrlgDigitReader.ReadPixels(image);
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1, count = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int i = (y * image.Width + x) * 3;
                bool dark = pixels[i] <= threshold && pixels[i + 1] <= threshold && pixels[i + 2] <= threshold;
                if (dark) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); count++; }
                pixels[i] = pixels[i + 1] = pixels[i + 2] = dark ? (byte)0 : (byte)255;
            }
        double ratio = count / (double)(image.Width * image.Height);
        if (ratio < .01 || ratio > .50 || maxY - minY < 12) return null;
        // Reject touching text instead of accepting a plausible word from a clipped region.
        if (minX == 0 || minY == 0 || maxX == image.Width - 1 || maxY == image.Height - 1) return null;
        int padX = Math.Max(4, (maxX - minX + 1) / 20), padY = Math.Max(2, (maxY - minY + 1) / 20);
        int left = Math.Max(0, minX - padX), top = Math.Max(0, minY - padY);
        using Mat binary = image.Clone();
        FrlgDigitReader.CopyPixels(binary, pixels, 3);
        using Mat crop = new(binary, new Rect(left, top, Math.Min(image.Width, maxX + padX + 1) - left,
            Math.Min(image.Height, maxY + padY + 1) - top));
        return crop.Clone();
    }

    private static Mat PadWhite(Mat image, int padding)
    {
        Mat result = new();
        Cv2.Resize(image, result, new Size(image.Width + 2 * padding, image.Height + 2 * padding));
        byte[] pixels = Enumerable.Repeat((byte)255, result.Width * result.Height * 3).ToArray();
        byte[] source = FrlgDigitReader.ReadPixels(image);
        for (int row = 0; row < image.Height; row++)
            Array.Copy(source, row * image.Width * 3, pixels, ((row + padding) * result.Width + padding) * 3, image.Width * 3);
        FrlgDigitReader.CopyPixels(result, pixels, 3);
        return result;
    }

    private static bool TouchesInk(Mat image)
    {
        byte[] pixels = FrlgDigitReader.ReadPixels(image);
        int left = 0, right = 0, top = 0, bottom = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                if (x != 0 && y != 0 && x != image.Width - 1 && y != image.Height - 1) continue;
                int i = (y * image.Width + x) * 3;
                if (pixels[i] >= 96 || pixels[i + 1] >= 96 || pixels[i + 2] >= 96) continue;
                if (x == 0) left++;
                if (x == image.Width - 1) right++;
                if (y == 0) top++;
                if (y == image.Height - 1) bottom++;
            }
        // Horizontal clipping can remove an entire character. A few pixels on the top or bottom,
        // however, can be a raised dakuten or the stem of the Nidoran gender marker.
        return left >= 2 || right >= 2 || top >= 10 || bottom >= 10;
    }

    internal static char? DetectGenderMarker(Mat image)
    {
        byte[] pixels = FrlgDigitReader.ReadPixels(image);
        int blue = 0, red = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int i = (y * image.Width + x) * 3;
                int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                if (b - r > 35 && b - g > 15) blue++;
                if (r - b > 35 && r - g > 15) red++;
            }
        // English fixtures colour the marker blue/red. Japanese FRLG renders it dark, so only
        // use colour when it is unambiguous and otherwise inspect the marker shape below.
        if (blue >= 40 && blue >= red * 2) return '♂';
        if (red >= 40 && red >= blue * 2) return '♀';
        bool Dark(int x, int y)
        {
            int i = (y * image.Width + x) * 3;
            return pixels[i] < 160 && pixels[i + 1] < 160 && pixels[i + 2] < 160;
        }

        bool[] inkColumns = new bool[image.Width];
        for (int x = 0; x < image.Width; x++)
            for (int y = 0; y < image.Height; y++)
                if (Dark(x, y)) { inkColumns[x] = true; break; }
        int right = Array.FindLastIndex(inkColumns, value => value);
        if (right < 0) return null;
        int separator = Math.Max(4, image.Height / 18), gap = 0, left = 0;
        for (int x = right - 1; x >= 0; x--)
        {
            if (!inkColumns[x]) { gap++; continue; }
            if (gap >= separator) { left = x + gap + 1; break; }
            gap = 0;
        }
        int minY = image.Height, maxY = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = left; x <= right; x++)
                if (Dark(x, y)) { minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        int width = right - left + 1, height = maxY - minY + 1;
        if (width < 12 || width > 60 || height < 18 || height > image.Height) return null;
        for (int y = minY + height / 2; y <= maxY; y++)
        {
            int rowLeft = right + 1, rowRight = left - 1, count = 0;
            for (int x = left; x <= right; x++)
                if (Dark(x, y)) { rowLeft = Math.Min(rowLeft, x); rowRight = Math.Max(rowRight, x); count++; }
            if (rowRight - rowLeft + 1 >= width * .65 && count >= width * .35) return '♀';
        }
        return '♂';
    }

    internal static bool NatureConfirmedByBothBackends(FrlgTextAttempt[] primary, FrlgTextAttempt[] secondary)
    {
        FrlgTextAttempt[] paddle = primary.Where(a => a.Accepted && a.Backend == "PaddleOCR").ToArray();
        FrlgTextAttempt[] tesseract = secondary.Where(a => a.Accepted && a.Backend == "Tesseract").ToArray();
        // Exact readings from both engines, plus another strong primary vote, can confirm a nature.
        // A single fuzzy vote or a conflicting accepted candidate cannot satisfy this rule.
        return paddle.Any(a => a.Distance == 0 && a.Confidence >= .90)
            && paddle.Count(a => a.Distance <= 1 && a.Confidence >= .85) >= 2
            && tesseract.Any(a => a.Distance == 0 && a.Confidence >= .70)
            && paddle.Concat(tesseract).Select(a => a.Candidate).Distinct().Count() == 1;
    }

    private static Mat FirstNatureClause(Mat image)
    {
        byte[] pixels = FrlgDigitReader.ReadPixels(image);
        bool[] inkColumns = new bool[image.Width];
        int top = image.Height, bottom = -1, first = image.Width;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int i = (y * image.Width + x) * 3;
                if (pixels[i] >= 96 || pixels[i + 1] >= 96 || pixels[i + 2] >= 96) continue;
                inkColumns[x] = true;
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                first = Math.Min(first, x);
            }
        int glyphHeight = bottom - top + 1;
        if (glyphHeight < 12) return image.Clone();
        int gapStart = -1;
        for (int x = first; x < image.Width; x++)
        {
            if (!inkColumns[x])
            {
                if (gapStart < 0) gapStart = x;
                continue;
            }
            if (gapStart >= 0 && x - gapStart >= glyphHeight * .7 && gapStart - first >= glyphHeight * 2)
            {
                using Mat crop = new(image, new Rect(0, 0, (gapStart + x) / 2, image.Height));
                return crop.Clone();
            }
            gapStart = -1;
        }
        return image.Clone();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _tesseract?.Dispose();
            _paddle?.Dispose();
        }
    }
}