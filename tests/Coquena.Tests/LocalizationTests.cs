using System.Globalization;
using System.Text.Json;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class LocalizationTests
{
    public static void RunNative(string executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-native-language-" + Guid.NewGuid().ToString("N"));
        var previous = Localization.Current;
        var checks = 0;
        void Check(bool value, string code)
        {
            if (!value) throw new Exception(code);
            checks++;
        }
        (int Code, string Output, string Error) Invoke(params string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.Environment["COQUENA_HOME"] = root;
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20_000)) { process.Kill(true); throw new Exception("native-language-timeout"); }
            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        try
        {
            Check(Invoke("--help").Code == 0 && !Directory.Exists(root), "native-auto-help-without-home");
            Check(Invoke("config", "language", "list").Code == 0 && !Directory.Exists(root), "native-discovery-without-home");
            foreach (var code in new[] { "en-US", "es-AR" })
            {
                Check(Invoke("config", "language", "set", code).Code == 0, "native-language-set");
                Localization.Apply(code, null);
                var shown = Invoke("config", "language", "show");
                Check(shown.Code == 0 && shown.Error.Length == 0 && shown.Output.TrimEnd() == Messages.LanguageShow.Format(code, code), "native-language-used-after-restart");
                var help = Invoke("--help");
                Check(help.Code == 0 && help.Error.Length == 0 && help.Output.Replace("\r\n", "\n").TrimEnd() == Messages.Help.ToString().TrimEnd(), "native-help-language");
                var exported = Invoke("config", "language", "export", code);
                using var json = JsonDocument.Parse(exported.Output);
                Check(exported.Code == 0 && json.RootElement.EnumerateObject().Count() == Localization.Texts().Count, "native-export-complete");
                Check(Invoke("config", "language", "set", "zz-ZZ").Code == 2, "native-unavailable-language-rejected");
            }
            Check(Invoke("config", "language", "unset").Code == 0 && new ConfigurationManager(new Home(root)).Language() is null, "native-unset-restores-auto");
            var folder = Path.Combine(root, "languages");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "fr-FR.json");
            File.WriteAllText(path, "{\"TreeEmpty\":\"native-external-marker\"}");
            Check(Invoke("config", "language", "set", "fr-FR").Code == 0, "native-external-selected");
            using (var exported = JsonDocument.Parse(Invoke("config", "language", "export", "fr-FR").Output))
                Check(exported.RootElement.GetProperty(nameof(Messages.TreeEmpty)).GetString() == "native-external-marker" &&
                    exported.RootElement.GetProperty(nameof(Messages.Help)).GetString() == Messages.Help.English, "native-external-with-english-fallback");
            foreach (var contents in new string?[] { "{\"Help\":\"{0}\"}", null })
            {
                if (contents is null) File.Delete(path); else File.WriteAllText(path, contents);
                Localization.Apply("en-US", null);
                var shown = Invoke("config", "language", "show");
                Check(shown.Code == 0 && shown.Output.TrimEnd() == Messages.LanguageShow.Format("fr-FR", "en-US") &&
                    shown.Error.Contains("fr-FR", StringComparison.Ordinal), "native-invalid-or-missing-file-fallback");
                Check(new ConfigurationManager(new Home(root)).Language() == "fr-FR", "native-fallback-preserves-preference");
            }
        }
        finally
        {
            Localization.Apply(previous, null);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        Console.WriteLine($"Native localization checks: {checks}.");
    }

    public static void Run()
    {
        var checks = 0;
        void Check(bool value, string code)
        {
            if (!value) throw new Exception(code);
            checks++;
        }
        var previous = Localization.Current;
        var culture = CultureInfo.CurrentCulture;
        var root = Path.Combine(Path.GetTempPath(), "coquena-localization-" + Guid.NewGuid().ToString("N"));
        var home = new Home(root);
        try
        {
            var english = Localization.Texts().ToDictionary(t => t.Key, t => t.English);
            var available = Localization.Available(home);
            Check(!Directory.Exists(root), "discovery-does-not-create-home");
            Check(available.Any(l => l.Code == "en-US") && available.Any(l => l.Code == "es-AR"), "delivered-languages");
            foreach (var source in available.Where(l => l.Origin == "embedded"))
            {
                var texts = Localization.Load(source)!;
                Check(english.Keys.Order().SequenceEqual(texts.Keys.Order()), "embedded-keys-complete");
                foreach (var (key, value) in texts)
                    Check(Localization.Placeholders(english[key]).SetEquals(Localization.Placeholders(value)), "embedded-markers-" + key);
            }
            Check(Localization.Resolve(null, CultureInfo.GetCultureInfo("es-AR"), available) == "es-AR", "auto-exact");
            Check(Localization.Resolve(null, CultureInfo.GetCultureInfo("es-ES"), available) == "es-AR", "auto-language");
            Check(Localization.Resolve(null, CultureInfo.GetCultureInfo("ja-JP"), available) == "en-US", "auto-fallback");
            Check(Localization.Placeholders("{{{0}}} {1,8:N2}").SetEquals([0, 1]), "escaped-braces-with-markers");
            var folder = home.PathFor("languages");
            Directory.CreateDirectory(folder);
            var key1 = nameof(Messages.TreeEmpty);
            var key2 = nameof(Messages.LanguageShow);
            var marker = "collaborator-value";
            void Write(string code, string json) => File.WriteAllText(Path.Combine(folder, code + ".json"), json);
            Write("fr-FR", JsonSerializer.Serialize(new Dictionary<string, string> { [key1] = marker }, LanguageJson.Default.DictionaryStringString));
            Check(Localization.Apply("fr-FR", home) is null && Localization.Current == "fr-FR", "external-language-selected");
            Check(Messages.TreeEmpty.ToString() == marker, "external-key");
            Check(Messages.LanguageShow.ToString() == english[key2], "missing-key-falls-back-to-english");
            Check(CultureInfo.CurrentCulture.Equals(culture), "language-preserves-system-formats");
            using (var exported = JsonDocument.Parse(Localization.Export("fr-FR", home)))
            {
                Check(exported.RootElement.EnumerateObject().Count() == english.Count, "export-complete");
                Check(exported.RootElement.GetProperty(key1).GetString() == marker &&
                    exported.RootElement.GetProperty(key2).GetString() == english[key2], "export-resolved-values");
            }
            Write("es-AR", JsonSerializer.Serialize(new Dictionary<string, string> { [key1] = marker }, LanguageJson.Default.DictionaryStringString));
            Check(Localization.Apply("es-AR", home) is null && Messages.TreeEmpty.ToString() == marker, "external-replaces-embedded");
            foreach (var invalid in new[] { "{", "[]", "{\"unknown-key\":\"value\"}",
                "{\"" + key1 + "\":null}", "{\"" + key1 + "\":1}",
                "{\"" + key1 + "\":\"a\",\"" + key1 + "\":\"b\"}",
                "{\"" + key2 + "\":\"{0}\"}", "{\"" + key1 + "\":\"{\"}",
                "{\"" + key2 + "\":\"{0:Q} {1}\"}" })
            {
                Write("fr-FR", invalid);
                Check(Localization.Apply("fr-FR", home) is not null && Localization.Current == "en-US" &&
                    Messages.TreeEmpty.ToString() == english[key1], "invalid-file-falls-back-atomically");
            }
            var manager = new ConfigurationManager(home);
            manager.SetLanguage("fr-FR");
            File.Delete(Path.Combine(folder, "fr-FR.json"));
            Check(Localization.Apply(manager.Language(), home) is not null && Localization.Current == "en-US" &&
                manager.Language() == "fr-FR", "missing-file-preserves-preference");
            manager.SetLanguage(null);
            Check(manager.Language() is null, "unset-restores-auto");
            foreach (var code in new[] { "en-US", "es-AR" })
            {
                File.Delete(Path.Combine(folder, "es-AR.json"));
                Localization.Apply(code, home);
                Check(Messages.Error("stable-code", Messages.InvalidArguments).EndsWith("(stable-code).", StringComparison.Ordinal), "error-code-independent-of-language");
            }
            Localization.Apply("en-US", null);
            var diagnostic = Messages.HealthHttp.Data(503);
            var serialized = ContractJson.Write(diagnostic, ContractJsonContext.Default.MessageData);
            Localization.Apply("es-AR", null);
            var restored = JsonSerializer.Deserialize(serialized, ContractJsonContext.Default.MessageData)!;
            Check(restored.Code == Messages.HealthHttp.Key && restored.Arguments.SequenceEqual(["503"]), "diagnostic-persists-code-and-arguments");
            Check(ContractJson.Write(restored, ContractJsonContext.Default.MessageData) == serialized, "diagnostic-document-independent-of-language");
            Check(restored.ToString() == Messages.HealthHttp.Format(503), "diagnostic-rendered-at-read-time");
            Localization.Apply("en-US", null);
            var factory = ParameterManager.Factory();
            var factoryJson = ContractJson.Write(factory, ContractJsonContext.Default.ParametersDocument);
            Localization.Apply("es-AR", null);
            Check(ContractJson.Write(ParameterManager.Factory(), ContractJsonContext.Default.ParametersDocument) == factoryJson, "factory-catalog-independent-of-language");
            var ownDescription = factory.Definitions.Single(d => d.Name == "host");
            Check(ownDescription.Description.Length == 0 && ownDescription.DescriptionKey == Messages.ParameterHostDefault.Key &&
                Messages.ParameterDescription(ownDescription) == Messages.ParameterHostDefault.ToString(), "own-description-stores-key-and-renders-on-demand");
            Check(Messages.ParameterDescription(ownDescription with { Description = "user-supplied-description" }) == "user-supplied-description", "user-description-remains-content");
            var upstream = factory.Definitions.Single(d => d.Name == "ctx-size");
            Check(upstream.DescriptionKey is null && Messages.ParameterDescription(upstream) == upstream.Description, "upstream-description-remains-content");
            foreach (var code in new[] { "en-US", "es-AR" })
            {
                Localization.Apply(code, null);
                var codes = Observation.DefinitionsPresentation.Views(new("fixture", true, false, true, true));
                Check(codes.SequenceEqual(["backends", "services", "models", "parameters", "presets", "updates", "maintenance"]), "view-codes-independent-of-language");
            }
        }
        finally
        {
            Localization.Apply(previous, null);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        Console.WriteLine($"Localization checks: {checks}.");
    }
}
