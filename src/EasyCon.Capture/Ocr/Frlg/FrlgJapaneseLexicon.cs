using System.Text;
using System.Text.Json;

namespace EasyCon.Capture.Ocr.Frlg;

public sealed record FrlgWordMatch(string Text, int Distance, int NextDistance, bool Accepted);

/// <summary>Canonical Japanese labels plus upstream OCR aliases. No species image labels.</summary>
public static class FrlgJapaneseLexicon
{
    private sealed record Entry(string Slug, string Text, string[] Forms);
    private static readonly Dictionary<char, string> _reductions = LoadReductions();
    private static readonly Entry[] _names = LoadNames();
    private static readonly Dictionary<string, Entry[]> _unvoicedNames = _names
        .SelectMany(e => e.Forms.Select(f => (Key: Unvoice(f), Entry: e)))
        .GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.Select(e => e.Entry).Distinct().ToArray());
    private static readonly Entry[] _natures = LoadNatures();
    public static int NameCount => _names.Length;
    public static int NatureCount => _natures.Length;

    private static JsonDocument Resource(string name)
    {
        using Stream stream = typeof(FrlgJapaneseLexicon).Assembly.GetManifestResourceStream("Frlg/Text/" + name)
            ?? throw new InvalidDataException("Missing FRLG dictionary: " + name);
        return JsonDocument.Parse(stream);
    }

    private static Dictionary<char, string> LoadReductions()
    {
        using JsonDocument document = Resource("CharacterReductions.json");
        Dictionary<char, string> result = [];
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
            foreach (char value in property.Value.GetString()!)
                result[value] = property.Name;
        return result;
    }

    private static Entry[] LoadNames()
    {
        using JsonDocument names = Resource("PokemonNameDisplay.json");
        using JsonDocument aliases = Resource("PokemonOCR-jpn.json");
        List<Entry> result = [];
        foreach (JsonProperty property in names.RootElement.EnumerateObject())
        {
            if (!property.Value.TryGetProperty("jpn", out JsonElement label)) continue;
            string text = label.GetString()!;
            List<string> forms = [text];
            if (aliases.RootElement.TryGetProperty(property.Name, out JsonElement values))
                forms.AddRange(values.EnumerateArray().Select(v => v.GetString()!));
            result.Add(new Entry(property.Name, text, forms.Select(Normalize).Distinct().ToArray()));
        }
        return result.ToArray();
    }

    private static Entry[] LoadNatures()
    {
        using JsonDocument document = Resource("NatureCheckerOCR.json");
        return document.RootElement.GetProperty("jpn").EnumerateObject().Select(property =>
        {
            string[] aliases = property.Value.EnumerateArray().Select(v => v.GetString()!).ToArray();
            string text = aliases[0].Replace("せいかく", "", StringComparison.Ordinal);
            return new Entry(property.Name, text, aliases.Concat([text, text + "なせいかく"])
                .Select(Normalize).Distinct().ToArray());
        }).ToArray();
    }

    public static string Normalize(string text)
    {
        StringBuilder result = new();
        foreach (char value in text.Normalize(NormalizationForm.FormKC))
        {
            if (!char.IsLetterOrDigit(value) && value is not 'ー' and not '♀' and not '♂') continue;
            result.Append(_reductions.TryGetValue(value, out string? replacement) ? replacement : value);
        }
        return result.ToString().ToLowerInvariant();
    }

    public static bool ValidTargets(string[] targets) => targets.All(t => _names.Any(e =>
        e.Text == t || e.Slug.Equals(t, StringComparison.OrdinalIgnoreCase)));

    public static FrlgWordMatch Match(string raw, bool nature, string[]? targets = null, bool requireNatureDescriptor = false)
    {
        string text = Normalize(raw);
        if (text.Length < 2 || text.Length > 24) return new("", 99, 99, false);
        Entry[] entries = nature ? _natures : _names;
        // The unrestricted exact match takes precedence over a requested target set.
        Entry[] exact = entries.Where(e => e.Forms.Contains(text)
            && (!requireNatureDescriptor || NatureClauseDistance(text, e) == 0)).ToArray();
        if (exact.Select(e => e.Text).Distinct().Count() == 1)
        {
            Entry entry = exact[0];
            bool allowed = nature || targets == null || targets.Length == 0 || targets.Any(t =>
                t == entry.Text || t.Equals(entry.Slug, StringComparison.OrdinalIgnoreCase));
            return new(entry.Text, 0, 1, allowed);
        }
        // Kana voicing marks are often lost or confused (ダ/タ, プ/ブ). Treat a single
        // such change as normalization only when the unrestricted dictionary has one result.
        // A real exact species above always wins, including one outside the requested targets.
        if (!nature && _unvoicedNames.TryGetValue(Unvoice(text), out Entry[]? unvoiced) && unvoiced.Length == 1)
        {
            Entry entry = unvoiced[0];
            if (entry.Forms.Min(f => Distance(text, f)) == 1)
            {
                bool allowed = targets == null || targets.Length == 0 || targets.Any(t =>
                    t == entry.Text || t.Equals(entry.Slug, StringComparison.OrdinalIgnoreCase));
                return new(entry.Text, 0, 1, allowed);
            }
        }
        if (!nature && targets is { Length: > 0 })
            entries = entries.Where(e => targets.Any(t => t == e.Text || t.Equals(e.Slug, StringComparison.OrdinalIgnoreCase))).ToArray();
        (Entry Entry, int Distance)[] ranked = entries.Select(e => (Entry: e,
            Distance: nature ? NatureDistance(text, e, requireNatureDescriptor)
                : e.Forms.Min(f => Distance(text, f)))).OrderBy(e => e.Distance).ToArray();
        if (ranked.Length == 0) return new("", 99, 99, false);
        (Entry best, int distance) = ranked[0];
        int next = ranked.Skip(1).Where(e => e.Entry.Text != best.Text).Select(e => e.Distance).DefaultIfEmpty(99).First();
        int limit = nature ? 2 : Math.Min(2, Math.Max(1, Normalize(best.Text).Length / 4));
        bool accepted = distance <= limit && next > distance && distance <= text.Length * .34;
        return new(best.Text, distance, next, accepted);
    }

    private static string Unvoice(string text) => new string(text.Normalize(NormalizationForm.FormD)
        .Where(c => c is not '\u3099' and not '\u309A').ToArray()).Normalize(NormalizationForm.FormC);

    private static int NatureDistance(string text, Entry entry, bool requireDescriptor)
    {
        int clause = NatureClauseDistance(text, entry);
        if (requireDescriptor) return clause;
        // A bare nature must retain its full length; a missing character is not a complete name.
        int whole = entry.Forms.Where(f => f.Length == text.Length).Select(f => Distance(text, f)).DefaultIfEmpty(99).Min();
        return Math.Min(whole, clause);
    }

    private static int NatureClauseDistance(string text, Entry entry)
    {
        string name = Normalize(entry.Text);
        string descriptor = Normalize("なせいかく");
        int suffixLength = text.Length - name.Length;
        // The fixed descriptor can stop after なせ / なせい / なせいか. Keep the full nature prefix.
        if (suffixLength < 2 || suffixLength > descriptor.Length) return 99;
        string suffix = text[name.Length..];
        int suffixDistance = Distance(suffix, descriptor[..suffixLength]);
        if (suffixDistance > (suffixLength >= 3 ? 1 : 0)) return 99;
        int nameDistance = Distance(text[..name.Length], name);
        return nameDistance <= 1 ? nameDistance + suffixDistance : 99;
    }

    private static int Distance(string a, string b)
    {
        int[] row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int diagonal = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int previous = row[j];
                row[j] = Math.Min(diagonal + (a[i - 1] == b[j - 1] ? 0 : 1), Math.Min(row[j] + 1, row[j - 1] + 1));
                diagonal = previous;
            }
        }
        return row[b.Length];
    }
}