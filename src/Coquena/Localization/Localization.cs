using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coquena.Storage;

namespace Coquena;

// One user-facing text. English lives in code; its key is the member name in Messages.
// Other languages map the same keys in JSON files. Converting to string (ToString,
// interpolation, implicit conversion) returns the text in the active language.
public sealed class Text
{
    public string Key { get; }
    public string English { get; }

    public Text(string english, [CallerMemberName] string key = "")
    {
        (English, Key) = (english, key);
        Localization.Register(this);
    }

    public override string ToString() => Localization.Lookup(Key, English);
    public string Format(params object?[] values) => string.Format(CultureInfo.CurrentCulture, ToString(), values);
    public MessageData Data(params object?[] values) => MessageData.Create(this, values);
    public static implicit operator string(Text text) => text.ToString();
}

// A language that can be chosen: embedded in the binary or a file in <home>/languages.
public sealed record LanguageSource(string Code, string Origin, string? Path);

public static partial class Localization
{
    public const string Auto = "auto";
    public const string English = "en-US";
    public const string Directory = "languages";
    private const string ResourcePrefix = "languages/";

    private static readonly Dictionary<string, Text> registry = new(StringComparer.Ordinal);
    private static readonly Lock gate = new();
    private static IReadOnlyDictionary<string, string>? table;

    // Code of the language in use; English until Apply chooses another.
    public static string Current { get; private set; } = English;

    internal static void Register(Text text)
    {
        lock (gate)
        {
            if (!registry.TryAdd(text.Key, text))
                throw new InvalidOperationException("Duplicated text key: " + text.Key);
        }
    }

    public static string Lookup(string key, string english) =>
        table is { } active && active.TryGetValue(key, out var value) ? value : english;

    internal static Text? Find(string key)
    {
        Messages.Load();
        lock (gate) return registry.GetValueOrDefault(key);
    }

    // Every text of the program with its English wording, ordered by key.
    public static IReadOnlyList<Text> Texts()
    {
        Messages.Load();
        lock (gate) return registry.Values.OrderBy(t => t.Key, StringComparer.Ordinal).ToArray();
    }

    // Languages available to choose: English, embedded ones and external files. An external
    // file with the code of an embedded language replaces it.
    public static IReadOnlyList<LanguageSource> Available(Home? home)
    {
        var found = new Dictionary<string, LanguageSource>(StringComparer.OrdinalIgnoreCase)
        {
            [English] = new(English, "built-in", null)
        };
        foreach (var name in typeof(Localization).Assembly.GetManifestResourceNames())
            if (name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            {
                var code = name[ResourcePrefix.Length..^5];
                found[code] = new(code, "embedded", null);
            }
        var folder = home?.PathFor(Directory);
        if (folder is not null && System.IO.Directory.Exists(folder))
            foreach (var file in System.IO.Directory.EnumerateFiles(folder, "*.json"))
            {
                var code = System.IO.Path.GetFileNameWithoutExtension(file);
                if (ValidCode(code)) found[code] = new(code, "external", file);
            }
        return found.Values.OrderBy(l => l.Code, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool ValidCode(string code) => CodePattern().IsMatch(code);

    // auto: the exact culture first, then another variant of the same language, then English.
    public static string Resolve(string? setting, CultureInfo culture, IReadOnlyList<LanguageSource> available)
    {
        if (setting is not null && !string.Equals(setting, Auto, StringComparison.OrdinalIgnoreCase))
            return available.FirstOrDefault(l => string.Equals(l.Code, setting, StringComparison.OrdinalIgnoreCase))?.Code ?? English;
        for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
            if (available.FirstOrDefault(l => string.Equals(l.Code, current.Name, StringComparison.OrdinalIgnoreCase)) is { } exact)
                return exact.Code;
        var language = culture.TwoLetterISOLanguageName;
        return available.Where(l => string.Equals(l.Code.Split('-')[0], language, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Code).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? English;
    }

    // Chooses the language from the configured setting (null means auto). A language that
    // cannot be loaded falls back to English and returns the problem as a warning.
    public static string? Apply(string? setting, Home? home, CultureInfo? culture = null)
    {
        Use(English, null);
        var code = setting ?? Auto;
        try
        {
            var available = Available(home);
            code = Resolve(setting, culture ?? CultureInfo.CurrentUICulture, available);
            if (setting is not null && !string.Equals(setting, Auto, StringComparison.OrdinalIgnoreCase) &&
                !available.Any(l => string.Equals(l.Code, setting, StringComparison.OrdinalIgnoreCase)))
                return Messages.LanguageNotLoaded.Format(setting, Messages.LanguageUnavailable.Format(setting));
            var source = available.First(l => l.Code == code);
            Use(source.Code, Load(source));
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or StorageException)
        {
            Use(English, null);
            return Messages.LanguageNotLoaded.Format(code, error.Message);
        }
    }

    // Applies the configured language when the program starts. It reads config.json only if
    // it exists, so commands such as --help never create the home.
    public static void Start(TextWriter error)
    {
        Home? home = null;
        string? setting = null;
        try
        {
            home = Home.Resolve();
            if (File.Exists(home.PathFor("config.json"))) setting = new Configuration.ConfigurationManager(home).Language();
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or StorageException or JsonException or Contracts.ContractException) { }
        if (Apply(setting, home) is { } warning) error.WriteLine(warning);
    }

    internal static void Use(string code, IReadOnlyDictionary<string, string>? texts)
    {
        table = texts;
        Current = code;
    }

    // Reads a language and checks it against the English texts. Unknown keys and keys whose
    // placeholders differ make the file invalid; missing keys fall back to English.
    public static IReadOnlyDictionary<string, string>? Load(LanguageSource source)
    {
        if (source.Origin == "built-in") return null;
        using var stream = source.Path is { } path ? File.OpenRead(path)
            : typeof(Localization).Assembly.GetManifestResourceStream(ResourcePrefix + source.Code + ".json")
              ?? throw new InvalidDataException(source.Code);
        // Reject duplicate keys and non-string values before deserializing. A malformed
        // collaborator file must never make a later formatting operation fail.
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(Messages.LanguageEmpty);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateObject())
            if (entry.Value.ValueKind != JsonValueKind.String || !texts.TryAdd(entry.Name, entry.Value.GetString()!))
                throw new InvalidDataException(Messages.LanguageInvalidValue.Format(entry.Name));
        Check(texts);
        return texts;
    }

    public static void Check(IReadOnlyDictionary<string, string> texts)
    {
        var english = Texts().ToDictionary(t => t.Key, t => t.English, StringComparer.Ordinal);
        foreach (var (key, value) in texts)
        {
            if (!english.TryGetValue(key, out var original))
                throw new InvalidDataException(Messages.LanguageUnknownKey.Format(key));
            if (value is null || !Markers(original).SetEquals(Markers(value)))
                throw new InvalidDataException(Messages.LanguagePlaceholders.Format(key));
            try { _ = System.Text.CompositeFormat.Parse(value); }
            catch (FormatException) { throw new InvalidDataException(Messages.LanguagePlaceholders.Format(key)); }
        }
    }

    // Positional placeholders used by a text, ignoring escaped braces.
    public static HashSet<int> Placeholders(string text)
    {
        try { _ = System.Text.CompositeFormat.Parse(text); }
        catch (FormatException) { throw new InvalidDataException(Messages.LanguageInvalidFormat); }
        var indexes = new HashSet<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
            var start = ++i;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            if (!int.TryParse(text.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                throw new InvalidDataException(Messages.LanguageInvalidFormat);
            indexes.Add(index);
        }
        return indexes;
    }

    private static HashSet<string> Markers(string text)
    {
        _ = Placeholders(text);
        var markers = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
            var end = text.IndexOf('}', i + 1);
            markers.Add(text[i..(end + 1)]);
            i = end;
        }
        return markers;
    }

    // Every key with the wording of the language: a starting point for a translation.
    public static string Export(string code, Home? home)
    {
        var source = Available(home).FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(code);
        var texts = Load(source);
        var all = Texts().ToDictionary(t => t.Key, t => texts is not null && texts.TryGetValue(t.Key, out var v) ? v : t.English, StringComparer.Ordinal);
        return JsonSerializer.Serialize(all, Readable.DictionaryStringString);
    }

    // Accents and symbols stay readable for translators instead of \u escapes.
    private static readonly LanguageJson Readable = new(new JsonSerializerOptions
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex CodePattern();
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LanguageJson : System.Text.Json.Serialization.JsonSerializerContext;
