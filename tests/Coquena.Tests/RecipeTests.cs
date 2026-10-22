using System.Globalization;
using System.Text;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class RecipeTests
{
    private static int assertions;
    public static void Run()
    {
        assertions = 0;
        var root = Path.Combine("D:\\tmp", "coquena-recipes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        string Write(string name, string content)
        {
            var file = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content, new UTF8Encoding(false)); return file;
        }
        EffectiveRecipe Read(string yaml, params string[] overrides)
        {
            Write("coquena.yaml", yaml);
            return RecipeReader.Read(new RecipeSelection(root, new[] { "coquena.yaml" }.Concat(overrides)), environment);
        }
        void Bad(string yaml, string code) => Fail(() => Read(yaml), code);
        RecipeValue Args(EffectiveRecipe recipe) => recipe.Root.Get("presets")!.Get("p")!.Get("args")!;

        var editable = Read("");
        Check(editable.Root.Fields.Count == 0 && editable.Target == "default" && editable.ArgsMode == "permissive", "empty editable recipe and defaults");
        Check(!Directory.Exists(Path.Combine(root, ".coquena")), "pure reading creates no project state");
        Check(Read("{}").Root.Fields.Count == 0, "flow root");
        Check(Read("# comentario\n").Root.Fields.Count == 0, "comment-only root");
        var typed = Read("""
            presets:
              p:
                args:
                  yes-text: yes
                  no-text: no
                  on-text: on
                  date: 2026-10-22
                  number: 8192
                  text: "8192"
                  true-flag: true
                  false-text: "false"
                  decimal: +001.2500
                  exp: 1e2
                  empty-text: ""
                  removed:
                  values: [a, 4, false]
            """);
        foreach (var key in new[] { "yes-text", "no-text", "on-text", "date", "text", "false-text", "empty-text" })
            Check(Args(typed).Get(key)!.Kind == RecipeKind.String, "YAML 1.2 string " + key);
        Check(Args(typed).Get("number")!.Kind == RecipeKind.Integer && Args(typed).Get("true-flag")!.Kind == RecipeKind.Boolean, "native scalar types");
        Check(Args(typed).Get("removed") is null && Args(typed).Get("empty-text")!.Text == "", "null removal differs from empty string");
        Check(Args(typed).Get("decimal")!.ToString() == "1.2500", "numeric rendering preserves decimal precision");
        Check(typed.PresetEnabled("p") && !typed.PresetWarmup("p") && typed.PresetArgsMode("p") == "permissive", "preset defaults without materializing fields");

        var baseYaml = """
            name: demo
            target: self
            args-mode: strict
            service:
              backend: {version: latest, variant: 'cuda@12'}
              server:
                args: {port: 9931, tags: [a, b]}
            models:
              main: owner/repo:Q4_0
            presets:
              p:
                args: {model: 'model://main', a: 1, remove: 2}
              old: {enable: false}
            arg-defs:
              definitions:
                a: {type: integer, minimum: 1}
            """;
        Write("overrides/second.yaml", """
            service:
              backend: {executable: ./bin/server.exe}
              server:
                args: {tags: [c]}
            presets:
              p:
                warmup: true
                args: {a: 3, remove: null}
              old: ~
            arg-defs:
              definitions:
                a: {type: string}
            """);
        var composed = Read(baseYaml, "overrides/second.yaml");
        var backend = composed.Root.Get("service")!.Get("backend")!;
        Check(backend.Get("executable")!.Text == "./bin/server.exe" && backend.Get("version") is null && backend.Get("variant") is null, "backend replaced atomically");
        Check(composed.Root.Get("service")!.Get("server")!.Get("args")!.Get("port")!.Text == "9931", "map inherited");
        Check(composed.Root.Get("service")!.Get("server")!.Get("args")!.Get("tags")!.Items.Single().Text == "c", "list replaced");
        Check(Args(composed).Get("a")!.Text == "3" && Args(composed).Get("remove") is null && composed.Root.Get("presets")!.Get("old") is null, "partial preset and null deletion");
        Check(composed.Root.Get("arg-defs")!.Get("definitions")!.Get("a")!.Get("minimum") is null, "whole argument definition replacement");
        Check(Args(composed).Get("model")!.Origin.File == Path.Combine(root, "coquena.yaml") && Args(composed).Get("a")!.Origin.File.EndsWith("second.yaml"), "per-field provenance");
        Check(Args(composed).Get("a")!.Origin.Line == 8 && Args(composed).Get("a")!.Origin.Column > 0 && Args(composed).Get("a")!.Origin.Path.Contains("args"), "one-based logical diagnostics");
        Check(composed.Root.Get("models")!.Get("main")!.Get("repository")!.Text == "owner/repo", "scalar model normalized");
        Check(composed.PresetArgsMode("p") == "strict" && composed.PresetWarmup("p"), "effective options inherited");
        var calls = 0;
        Check(composed.ResolveTarget(_ => { calls++; throw new Exception(); }).IsSelf && calls == 0, "self target doesn't consult global");
        var consumer = Read("presets: {p: {}} ");
        Check(consumer.ResolveTarget(selector => { Check(selector == "default", "omitted target means default"); return "id"; }).ServiceId == "id", "default resolver");
        Check(consumer.ResolveTarget(selector => selector, "named").ServiceId == "named", "explicit target override");
        Fail(() => consumer.ResolveTarget(_ => "id", "self"), "self-requires-service");
        Bad("target: self", "self-requires-service");
        Bad("service: {}", "backend-required");
        Bad("service: {backend: {executable: a, version: latest}}", "invalid-backend-selection");
        Bad("service: {backend: {version: latest}}", "invalid-backend-selection");
        Bad("service: {backend: {executable: ''}}", "empty-value");
        Bad("service: {backend: {executable: a}, server: {health: {interval-ms: 99}}}", "invalid-health-interval");
        Bad("service: {backend: {executable: a}, server: {health: {interval-ms: 10001}}}", "invalid-health-interval");
        Bad("service: {backend: {executable: a}, server: {logs: {retention: {max-bytes: 0}}}}", "positive-required");

        environment["CTX"] = "8192"; environment["FLAG"] = "true"; environment["EMPTY"] = "";
        environment["SECRET"] = "sensitive-content-123"; environment["NEXT"] = "${MISSING}";
        var expanded = Read("""
            presets:
              p:
                enable: ${FLAG}
                args:
                  unquoted: ${CTX}
                  quoted: "${CTX}"
                  single: '${MISSING}'
                  wrapped: {literal: '${MISSING}'}
                  escaped: $$SECRET
                  shell: $(echo hi)
                  secret: $SECRET
                  once: $NEXT
                  fallback: ${MISSING:-${CTX}}
                  empty-default: ${EMPTY:-fallback}
                  empty-kept: ${EMPTY-fallback}
                  plus-empty: ${EMPTY+present}
                  plus-nonempty: ${EMPTY:+present}
                  missing-plus: ${MISSING+present}
            """);
        Check(expanded.PresetEnabled("p"), "unquoted structural scalar converted");
        Check(Args(expanded).Get("unquoted")!.Kind == RecipeKind.String && Args(expanded).Get("unquoted")!.CanConvert, "unknown argument remains text with conversion metadata");
        Check(Args(expanded).Get("unquoted")!.ConvertInterpolatedScalar("integer").Kind == RecipeKind.Integer, "schema can convert unquoted full expansion");
        Check(Args(expanded).Get("quoted")!.ConvertInterpolatedScalar("integer").Kind == RecipeKind.String, "quoted expansion remains string");
        Check(Args(expanded).Get("single")!.Text == "${MISSING}" && Args(expanded).Get("wrapped")!.Get("literal")!.Text == "${MISSING}", "single quotes and literal suppress expansion");
        Check(Args(expanded).Get("escaped")!.Text == "$SECRET" && Args(expanded).Get("shell")!.Text == "$(echo hi)", "dollar escaping and no shell");
        Check(Args(expanded).Get("once")!.Text == "${MISSING}" && Args(expanded).Get("fallback")!.Text == "8192", "no recursive expanded value and nested fallback");
        Check(Args(expanded).Get("empty-default")!.Text == "fallback" && Args(expanded).Get("empty-kept")!.Text == "", "minus empty semantics");
        Check(Args(expanded).Get("plus-empty")!.Text == "present" && Args(expanded).Get("plus-nonempty")!.Text == "" && Args(expanded).Get("missing-plus")!.Text == "", "plus empty semantics");
        Check(!expanded.ToString().Contains("sensitive-content-123") && expanded.ToString().Contains("redacted"), "effective rendering redacts expansions");
        Check(!expanded.Layers[0].ToString().Contains("sensitive-content-123"), "layer rendering redacts expansions");
        Bad("presets: {p: {enable: '${FLAG}'}}", "invalid-type");
        Bad("presets: {p: {enable: \"${FLAG}\"}}", "invalid-type");
        environment["FLAG"] = "not-bool";
        Bad("presets: {p: {enable: '${FLAG}'}}", "invalid-type");
        Bad("presets: {p: {enable: ${FLAG}}}", "yaml-syntax"); // braces must be quoted in a flow collection
        Bad("presets:\n  p:\n    enable: ${FLAG}\n", "invalid-type");
        environment["FLAG"] = "true";
        Bad("name: $MISSING", "variable-required");
        Bad("name: ${EMPTY:?private custom message}", "variable-required");
        Check(Args(Read("presets:\n  p:\n    args:\n      a: ${EMPTY?message}\n")).Get("a")!.Text == "", "question operator permits present empty");
        Bad("name: ${MISSING?private custom message}", "variable-required");
        Bad("name: ${MISSING", "interpolation-syntax");
        Bad("name: ${MISSING/foo}", "interpolation-syntax");
        Bad("name: ${MISSING:-" + string.Concat(Enumerable.Repeat("${MISSING:-", 33)) + "x" + new string('}', 34), "interpolation-limit");
        environment["INJECTION"] = "{enable: true}";
        Check(Args(Read("presets:\n  p:\n    args:\n      x: $INJECTION\n")).Get("x")!.Kind == RecipeKind.String, "expansion never reparses YAML");
        Bad("presets:\n  p:\n    enable: $INJECTION\n", "invalid-type");
        Check(Read("models:\n  '$MISSING': owner/repo:Q4\n").Root.Get("models")!.Get("$MISSING") is not null, "keys never interpolate");

        Write("env/first.env", """
            # env selected explicitly
            CTX=11
            EMPTY=env-value
            ORDER=first
            ORDER=last-in-file
            DOUBLE="a\nb\t\\\""
            SINGLE='first
            second $MISSING'
            INLINE=value # comment
            HASH=value#hash
            HASH_START=#literal
            COMMENT_ONLY= #comment
            ABSENT
            LITERAL='$MISSING'
            ALREADY=${CTX}
            """);
        Write("env/second.env", "ORDER=second\nFROM_FIRST=${ALREADY}\nNEW=\n");
        Write("coquena.yaml", """
            presets:
              p:
                args:
                  ctx: $CTX
                  empty: ${EMPTY-fallback}
                  order: $ORDER
                  inline: $INLINE
                  hash: $HASH
                  hash-start: $HASH_START
                  comment-only: $COMMENT_ONLY
                  single: $SINGLE
                  double: $DOUBLE
                  literal: $LITERAL
                  previous: $FROM_FIRST
                  new: ${NEW+present}
                  absent: ${ABSENT-fallback}
            """);
        var envRecipe = RecipeReader.Read(new RecipeSelection(root, envFiles: ["env/first.env", "env/second.env"]), environment);
        var envArgs = Args(envRecipe);
        Check(envArgs.Get("ctx")!.Text == "8192" && envArgs.Get("empty")!.Text == "", "process wins env including empty");
        Check(envArgs.Get("order")!.Text == "second" && envArgs.Get("previous")!.Text == "8192", "env order and process precedence during env interpolation");
        Check(envArgs.Get("inline")!.Text == "value" && envArgs.Get("hash")!.Text == "value#hash", "env inline comments require preceding space");
        Check(envArgs.Get("hash-start")!.Text == "#literal" && envArgs.Get("comment-only")!.Text == "", "env hash after equals needs space for comment");
        Check(envArgs.Get("single")!.Text == "first\nsecond $MISSING" && envArgs.Get("literal")!.Text == "$MISSING", "env single quotes multiline literal");
        Check(envArgs.Get("double")!.Text == "a\nb\t\\\"", "env double quote escapes");
        Check(envArgs.Get("new")!.Text == "present" && envArgs.Get("absent")!.Text == "fallback", "env empty versus absent");
        Write("invalid.env", "FORWARD=$LATER\nLATER=late\n");
        Fail(() => RecipeReader.Read(new RecipeSelection(root, envFiles: ["invalid.env"]), environment), "variable-required");
        Write("invalid.env", "BAD-NAME=secret\n");
        Fail(() => RecipeReader.Read(new RecipeSelection(root, envFiles: ["invalid.env"]), environment), "env-syntax");
        Write("invalid.env", "X=\"bad\\q\"\n");
        Fail(() => RecipeReader.Read(new RecipeSelection(root, envFiles: ["invalid.env"]), environment), "env-syntax");
        Write("invalid.env", "X='open\n");
        Fail(() => RecipeReader.Read(new RecipeSelection(root, envFiles: ["invalid.env"]), environment), "env-syntax");
        Write(".env", "CTX=automatic-must-not-load");
        Check(Args(Read("presets:\n  p:\n    args:\n      x: ${CTX-fallback}\n")).Get("x")!.Text == "8192", "env not auto-detected");

        foreach (var yaml in new[] { "backend: {}", "server: {}", "project: {}", "version: 1", "service: {name: forbidden}", "presets: {p: {model: main}}", "service: {server: {wat: null}}", "arg-defs: {definitions: {x: {unknown: null}}}" })
            Bad(yaml, "unknown-key");
        Bad("name: a\nname: b", "duplicate-key");
        Bad("presets: {p: {args: {x: 1, x: 2}}}", "duplicate-key");
        Bad("{true: value}", "yaml-key-type");
        Bad("? [a, b]\n: value", "yaml-complex-key");
        Bad("name: &anchor value", "yaml-anchor");
        Bad("name: *anchor", "yaml-alias");
        Bad("<<: {name: merged}", "yaml-merge-key");
        Bad("name: !secret value", "yaml-tag");
        Bad("---\nname: a\n---\nname: b", "multiple-documents");
        Bad("%YAML 1.1\n---\nname: a", "yaml-version");
        Bad("[a, b]", "root-map-required");
        Bad("null", "root-map-required");
        Bad("~", "root-map-required");
        Bad("service: []", "map-required");
        Bad("presets: {p: {args: {x: {bad: value}}}}", "unknown-key");
        Bad("presets: {p: {args: {x: {literal: null}}}}", "invalid-literal");
        Bad("presets: {p: {args: {x: [a, null]}}}", "invalid-argument-item");
        Bad("presets: {p: {args: {x: [[a]]}}}", "invalid-argument-item");
        Bad("presets: {p: {args: {x: .NaN}}}", "non-finite-number");
        Bad("presets: {p: {args: {x: -.inf}}}", "non-finite-number");
        Bad("presets: {p: {args: {x: 1e999}}}", "non-finite-number");
        Bad("name: ''", "invalid-name");
        Bad("name: 'bad/name'", "invalid-name");
        Bad("args-mode: unknown", "invalid-args-mode");
        Bad("arg-defs: {definitions: {x: {type: integer, minimum: {hidden: true}}}}", "invalid-type");
        Bad("models: {m: 'owner/repo'}", "invalid-model-shorthand");
        Bad("models: {m: {repository: owner/repo}}", "invalid-resource-selection");
        Bad("models: {m: {repository: owner/repo, quantization: Q4, files: [a.gguf]}}", "invalid-resource-selection");
        Bad("models: {m: {repository: invalid, quantization: Q4}}", "invalid-repository");
        Bad("models: {m: {files: []}}", "empty-files");
        Bad("models: {m: {files: [a.gguf], revision: main}}", "invalid-local-selection");
        Bad("models: {m: {repository: owner/repo, files: ['D:/local/a.gguf']}}", "mixed-resource-paths");
        Bad("models: {m: {repository: owner/repo, files: ['https://example.com/a.gguf']}}", "mixed-resource-paths");
        Bad("companions: {c: {files: [a.gguf]}}", "invalid-companion-type");
        Check(Read("companions: {c: {type: mmproj, files: [./weights/a.gguf]}} ").Root.Get("companions")!.Get("c")!.Get("type")!.Text == "mmproj", "local companion");
        Check(Read("models: {m: {files: ['D:/external weights/a.gguf']}} ").Root.Get("models")!.Get("m")!.Get("files")!.Items.Single().Text!.StartsWith("D:"), "external model path no IO or adoption");
        environment["MODEL"] = "owner/repo:Q4_0";
        Check(Read("models: {m: $MODEL}").Root.Get("models")!.Get("m")!.Get("quantization")!.Text == "Q4_0", "interpolated model shorthand");
        Check(Read("models: {m: $MODEL}").Root.Get("models")!.Get("m")!.Get("repository")!.Sensitive, "shorthand expansion retains redaction");
        Write("remove.yaml", "service: null\n");
        Fail(() => Read("service: {mystery: null}", "remove.yaml"), "unknown-key");
        Check(Read("service: {server: {args: {port: 9931}}}", "overrides/second.yaml").Root.Get("service")!.Get("backend")!.Get("executable") is not null, "effective requirements checked after composition");
        Write("nulls.yaml", "name: null\nmodels: null\npresets: {p: null}\n");
        var removed = Read("name: demo\nmodels: {m: owner/repo:Q4}\npresets: {p: {}}", "nulls.yaml");
        Check(removed.Root.Get("name") is null && removed.Root.Get("models") is null && removed.Root.Get("presets")!.Fields.Count == 0, "whole blocks removed");

        Write("sub/coquena.yaml", "{}");
        Write("sub/override.yaml", "{}");
        Write("sub/local.env", "X=value");
        var selection = new RecipeSelection(root, ["sub/coquena.yaml", "override.yaml"], envFiles: ["local.env"]);
        Check(selection.Base == Path.Combine(root, "sub") && selection.Files[1] == Path.GetFullPath("sub/override.yaml", root) && selection.EnvFiles[0] == Path.GetFullPath("sub/local.env", root), "all later paths use first file base");
        Check(selection.ResolvePath("../external/file.gguf") == Path.GetFullPath("external/file.gguf", root), "external relative path resolved against base");
        var explicitSelection = new RecipeSelection(root, ["coquena.yaml", "sub/override.yaml"], ".", ["sub/local.env"]);
        Check(explicitSelection.Base == root && explicitSelection.Files[1] == Path.GetFullPath("sub/override.yaml", root), "explicit base");
        Fail(() => new RecipeSelection(root, Enumerable.Repeat("coquena.yaml", 65)), "layer-limit");
        Fail(() => new RecipeSelection(root, envFiles: Enumerable.Repeat("local.env", 64)), "layer-limit");
        Fail(() => new RecipeSelection(root, ["-"]), "invalid-file-selection");
        Fail(() => RecipeReader.Read(new RecipeSelection(root, ["missing.yaml"]), environment), "file-unreadable");
        Write("max.yaml", "{}");
        Check(RecipeReader.Read(new RecipeSelection(root, Enumerable.Repeat("max.yaml", 64)), environment).Layers.Count == 64, "64 layers accepted");
        Write("unused-override.yaml", "name: valid");
        Fail(() => Read("name: $MISSING", "unused-override.yaml"), "variable-required");
        Check(Args(Read("presets:\n  p:\n    args:\n      x: " + string.Concat(Enumerable.Repeat("${MISSING:-", 32)) + "ok" + new string('}', 32))).Get("x")!.Text == "ok", "32 nested expansions accepted");
        var depth = "presets: {p: {args: {x: " + new string('[', 65) + "a" + new string(']', 65) + "}}}";
        Bad(depth, "yaml-limit");
        var many = "presets:\n  p:\n    args:\n" + string.Concat(Enumerable.Range(0, 50001).Select(x => "      key" + x + ": a\n"));
        Bad(many, "yaml-limit");
        Write("oversized.yaml", new string(' ', 16 * 1024 * 1024 + 1));
        Fail(() => RecipeReader.Read(new RecipeSelection(root, ["oversized.yaml"]), environment), "file-limit");
        File.WriteAllBytes(Path.Combine(root, "invalid.yaml"), [0xff, 0xfe, 0xff]);
        Fail(() => RecipeReader.Read(new RecipeSelection(root, ["invalid.yaml"]), environment), "invalid-encoding");
        File.WriteAllBytes(Path.Combine(root, "invalid.env"), [0xff, 0xfe, 0xff]);
        Fail(() => RecipeReader.Read(new RecipeSelection(root, ["max.yaml"], envFiles: ["invalid.env"]), environment), "invalid-utf8");
        foreach (var encoding in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true), new UTF32Encoding(false, true), new UTF32Encoding(true, true), new UnicodeEncoding(false, false), new UnicodeEncoding(true, false), new UTF32Encoding(false, false), new UTF32Encoding(true, false) })
        {
            File.WriteAllText(Path.Combine(root, "encoded.yaml"), "name: español\n", encoding);
            Check(RecipeReader.Read(new RecipeSelection(root, ["encoded.yaml"]), environment).Name == "español", "YAML Unicode encoding " + encoding.WebName);
        }

        Write("coquena.yaml", "name: modified\n{}");
        var registryFolder = Path.Combine(root, "registered"); Directory.CreateDirectory(registryFolder);
        Write("registered/coquena.yaml", "name: changed\n");
        var registry = new RecipeRegistry(new ScopeStore(ScopeContext.Project(Path.Combine(registryFolder, "coquena.yaml"))));
        var identity = registry.Register(["coquena.yaml"], "original");
        var stable = RecipeReader.Read(new RecipeSelection(registryFolder), environment, identity);
        Check(stable.Name == "original" && stable.Root.Get("name")!.Text == "changed", "explicit identity preserves registered name without inferring by filenames");
        var home = new Home(Path.Combine(root, "home"));
        var globals = new RecipeRegistry(new ScopeStore(ScopeContext.Global(home)));
        var recipeIdentity = globals.Register(["a.yaml"], "global"); var service = globals.RegisterService(recipeIdentity.RecipeId);
        globals.SetDefaultService(service.ServiceId);
        var beforeRevision = new ScopeStore(ScopeContext.Global(home)).Read().Revision;
        Check(editable.ResolveTarget(globals.ResolveGlobalService).ServiceId == service.ServiceId, "real global default registry lookup");
        Check(editable.ResolveTarget(globals.ResolveGlobalService, "global").ServiceId == service.ServiceId, "real named registry lookup");
        Check(new ScopeStore(ScopeContext.Global(home)).Read().Revision == beforeRevision, "target resolution does not write registry");
        Fail(() => editable.ResolveTarget(globals.ResolveGlobalService, "absent"), "unknown-service");
        Fail(() => editable.ResolveTarget(_ => throw new StorageException("corrupt-state", "sensitive-content-123")), "corrupt-state");
        Check(!Directory.Exists(Path.Combine(root, ".coquena")), "all reading leaves project untouched");
        Console.WriteLine($"OK: {assertions} verificaciones de recetas 045. Fixtures: {root}");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("045: " + description);
        assertions++;
    }
    private static void Fail(Action action, string code)
    {
        try { action(); }
        catch (RecipeException e)
        {
            Check(e.Code == code, "expected " + code + ", got " + e.Code);
            Check(e.Origin.Line > 0 && e.Origin.Column > 0 && e.Message.Contains(e.Phase + "/" + code), "diagnostic location and phase");
            Check(!e.Message.Contains("sensitive-content-123") && !e.Message.Contains("private custom message") && e.InnerException is null, "diagnostic redaction");
            return;
        }
        throw new Exception("045: expected failure " + code);
    }
}
