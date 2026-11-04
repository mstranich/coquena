using System.Diagnostics;
using System.Text.Json.Nodes;
using Coquena.Recipes;

namespace Coquena.Tests;

internal static class ArgumentTests
{
    private static int assertions;
    public static void Run()
    {
        assertions = 0;
        var root = Path.Combine("D:\\tmp", "coquena-arguments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var env = new Dictionary<string, string?> { ["VALUE"] = "00123", ["BOOL"] = "false", ["NUMBER"] = "1.25", ["SECRET"] = "private-fixture-value" };
        void Write(string name, string text) => File.WriteAllText(Path.Combine(root, name), text);
        EffectiveRecipe Read(string yaml, params string[] layers)
        {
            Write("coquena.yaml", yaml);
            return RecipeReader.Read(new(root, new[] { "coquena.yaml" }.Concat(layers)), env);
        }
        ArgumentSchema Schema(string definitions) => ArgumentSchema.Load(Read("arg-defs:\n  definitions:\n" + definitions));
        void InvalidSchema(string definition, string code) => Bad(() => Schema("    x: " + definition + "\n"), code);
        ValidatedArgumentBlock Preset(EffectiveRecipe r, ArgumentBackend? backend = null) => ArgumentValidation.Validate(r, backend: backend).Single(x => x.Key == "p");
        EffectiveRecipe With(string definition, string args, string mode = "permissive") => Read("args-mode: " + mode + "\narg-defs:\n  definitions:\n    x: " + definition + "\npresets:\n  p:\n    args: " + args + "\n");
        string[] Cli(EffectiveRecipe r, IArgumentAdapter? adapter = null) => ArgumentEmission.Cli(r, Preset(r), adapter).ToArray();

        Editing(root);

        var typed = Read("""
            presets:
              p:
                args:
                  a: ${VALUE}
                  b: ${VALUE|integer}
                  c: "${BOOL|boolean}"
                  d: ${NUMBER|number}
                  e: ${ABSENT:-4096|integer}
                  f: '${VALUE|integer}'
                  g: {literal: '${VALUE|integer}'}
                  h: prefix-${VALUE}
                  i: ${BOOL|string}
            """);
        var values = Preset(typed).Arguments;
        Check(values["a"].Value.Kind == RecipeKind.String && values["a"].Value.Text == "00123", "default string preserves zeros");
        Check(values["b"].Value.Kind == RecipeKind.Integer, "explicit integer");
        Check(values["c"].Value.Kind == RecipeKind.Boolean && values["c"].Value.Text == "false", "explicit quoted boolean");
        Check(values["d"].Value.Kind == RecipeKind.Number && values["e"].Value.Kind == RecipeKind.Integer, "number and typed default");
        Check(values["f"].Value.Text == "${VALUE|integer}" && values["g"].Literal, "literal suppression");
        Check(values["h"].Value.Kind == RecipeKind.String && values["i"].Value.Kind == RecipeKind.String, "composed text and explicit string");
        Check(!typed.ToString().Contains("00123"), "typed interpolation remains redacted");
        foreach (var expression in new[] { "${VALUE|boolean}", "${VALUE|array}", "prefix-${VALUE|integer}", "${VALUE|integer}-suffix", "${VALUE:-${BOOL|boolean}}", "${BOOL|integer}", "${NUMBER|integer}" })
            BadAny(() => Read("presets:\n  p:\n    args:\n      x: " + expression + "\n"), "invalid explicit interpolation " + expression);
        env["NUMBER"] = "NaN";
        Bad(() => Read("presets:\n  p:\n    args:\n      x: ${NUMBER|number}\n"), "invalid-type");
        env["NUMBER"] = "1.25";
        Bad(() => Read("presets:\n  p:\n    enable: ${BOOL}\n"), "invalid-type");
        Check(!Read("presets:\n  p:\n    enable: ${BOOL|boolean}\n").PresetEnabled("p"), "structural boolean requires explicit type");

        Write("override.yaml", """
            arg-defs:
              definitions:
                x: {type: string, aliases: [short]}
                old: null
                local: {type: boolean}
            """);
        var layered = Read("""
            arg-defs:
              definitions:
                x: {type: integer, minimum: 5}
                old: {type: string}
                inline: {type: string}
            presets:
              p:
                args: {short: hello}
            """, "override.yaml");
        var schema = ArgumentSchema.Load(layered);
        Check(schema.Definitions.Count == 3 && !schema.Definitions.ContainsKey("old"), "layered definitions and null removal");
        Check(schema.Definitions["x"].Get("minimum") is null && Preset(layered).Arguments.ContainsKey("x"), "whole replacement and alias canonicalization");
        Write("remove.yaml", "arg-defs: null\n");
        Check(ArgumentSchema.Load(Read("arg-defs: {definitions: {x: {type: string}}}", "remove.yaml")).Definitions.Count == 0, "whole schema removal");
        Bad(() => Read("arg-defs: {imports: [old.json]}"), "unknown-key");
        var invalid = new (string Definition, string Code)[]
        {
            ("{}", "type-required"), ("{type: wat}", "invalid-schema-type"), ("{type: string, hidden: null}", "unknown-key"),
            ("{type: array}", "items-required"), ("{type: string, items: {type: string}}", "inapplicable-items"),
            ("{type: array, items: {type: array, items: {type: string}}}", "nested-array"),
            ("{type: array, items: {type: string, emit: {}}}", "unknown-key"),
            ("{type: number, minimum: 5, maximum: 4}", "invalid-range"), ("{type: string, minimum: 1}", "invalid-bound"),
            ("{type: integer, enum: []}", "invalid-schema-list"), ("{type: integer, enum: [a]}", "invalid-enum"),
            ("{type: number, enum: [1, 1.0]}", "duplicate-enum"), ("{type: integer, default: nope}", "invalid-default"),
            ("{type: boolean, scope: hardware}", "invalid-scope"),
            ("{type: string, pattern: '(?=a)'}", "invalid-pattern"), ("{type: string, pattern: '(a)\\1'}", "invalid-pattern"),
            ("{type: string, pattern: '['}", "invalid-pattern"),
            ("{any-of: []}", "invalid-schema-list"), ("{type: integer, any-of: [{type: string}]}", "invalid-any-of"),
            ("{type: string, aliases: [x]}", "alias-collision"), ("{type: string, aliases: [z, z]}", "alias-collision"),
            ("{type: boolean, emit: {cli: {mode: value, true-value: yes}}}", "inapplicable-boolean-values"),
            ("{type: string, emit: {cli: {mode: flag}}}", "inapplicable-emission-mode"),
            ("{type: string, emit: {cli: {name: a}}}", "emission-mode-required"),
            ("{type: string, emit: {ini: {mode: value, negative-name: no-x}}}", "unknown-key"),
            ("{type: array, items: {type: string}, emit: {cli: {mode: join}}}", "separator-required"),
            ("{type: string, emit: {cli: {mode: value, separator: ','}}}", "inapplicable-separator"),
            ("{type: boolean, emit: {cli: {mode: value, negative-name: no-x}}}", "inapplicable-negative-name"),
            ("{type: string, applies-to: {min-build: latest}}", "invalid-build"),
            ("{type: string, applies-to: {min-build: b20, max-build: b19}}", "invalid-build-range"),
            ("{type: string, rules: [{}]}", "invalid-rule"),
            ("{type: string, rules: [{requires: {missing: {present: true}}}]}", "unknown-rule-argument"),
            ("{type: string, rules: [{when: {contains: a}, requires: {x: {present: true}}}]}", "condition-type-mismatch"),
            ("{type: string, rules: [{when: {present: true, equals: a}, requires: {x: {present: true}}}]}", "invalid-condition")
        };
        foreach (var item in invalid) InvalidSchema(item.Definition, item.Code);
        Bad(() => Schema("    x: {type: string, aliases: [y]}\n    y: {type: string}"), "alias-collision");

        Check(Cli(With("{type: integer}", "{x: 4}", "strict")).SequenceEqual(["--x", "4"]), "strict known integer");
        foreach (var notation in new[] { "8192", "8192.0", "8.192e3", "8192.5" })
        {
            foreach (var mode in new[] { "strict", "permissive" })
            {
                if (notation == "8192")
                    Check(Preset(With("{type: integer}", "{x: " + notation + "}", mode)).Arguments["x"].Value.Kind == RecipeKind.Integer, "integer accepts integer notation in " + mode);
                else Bad(() => Preset(With("{type: integer}", "{x: " + notation + "}", mode)), "invalid-argument-value");
                Check(Preset(With("{type: number}", "{x: " + notation + "}", mode)).Arguments.Count == 1, "number accepts finite numeric notation in " + mode);
            }
            env["NUMERIC"] = notation;
            var integerExpansion = "presets:\n  p:\n    args:\n      x: ${NUMERIC|integer}\n";
            if (notation == "8192") Check(Preset(Read(integerExpansion)).Arguments["x"].Value.Kind == RecipeKind.Integer, "integer expansion accepts integer notation");
            else Bad(() => Read(integerExpansion), "invalid-type");
            Check(Preset(Read("presets:\n  p:\n    args:\n      x: ${NUMERIC|number}\n")).Arguments["x"].Value.Kind == RecipeKind.Number, "number expansion accepts finite numeric notation");
        }
        Bad(() => Preset(With("{type: integer}", "{x: '4'}")), "invalid-argument-value");
        Bad(() => Preset(With("{type: integer}", "{x: 4, unknown: 1}", "strict")), "unknown-argument");
        Check(Preset(With("{type: integer}", "{x: 4, unknown: 1}")).Arguments.Count == 2, "permissive unknown remains");
        Bad(() => Preset(With("{type: integer, scope: server}", "{x: 4}")), "argument-scope");
        Bad(() => Preset(With("{type: string, aliases: [z]}", "{x: a, z: b}")), "duplicate-argument");
        Bad(() => Preset(With("{type: integer, minimum: 9007199254740993}", "{x: 9007199254740992}")), "invalid-argument-value");
        Check(Preset(With("{type: number, minimum: 1.23, maximum: 1.23}", "{x: 123e-2}")).Arguments.Count == 1, "exact scientific comparison");
        Bad(() => Preset(With("{type: string, pattern: '^a+$'}", "{x: b}")), "invalid-argument-value");
        Check(Preset(With("{type: integer, default: 3}", "{}")).Arguments.Count == 0, "no defaults materialized");
        var union = With("{any-of: [{type: integer}, {type: string}]}", "{x: '${VALUE}'}");
        Check(Preset(union).Arguments["x"].Value.Kind == RecipeKind.String, "any-of does not choose conversion");
        Bad(() => Preset(Read("arg-defs:\n  definitions:\n    x: {type: integer}\npresets:\n  p:\n    args:\n      x: ${VALUE}\n")), "invalid-argument-value");
        Check(Preset(Read("arg-defs:\n  definitions:\n    x: {type: integer}\npresets:\n  p:\n    args:\n      x: ${VALUE|integer}\n")).Arguments["x"].Value.Kind == RecipeKind.Integer, "explicit conversion then schema validation");
        Check(Preset(Read("args-mode: strict\npresets:\n  p:\n    args-mode: permissive\n    args: {unknown: 1}\n")).Arguments.Count == 1, "local mode override");

        var rules = """
            arg-defs:
              definitions:
                config:
                  type: string
                  scope: preset
                  rules:
                    - when: {present: false}
                      requires: {port: {present: true}}
                port: {type: integer, scope: preset}
                public:
                  type: boolean
                  rules:
                    - when: {equals: true}
                      conflicts: {config: {present: true}}
                tags:
                  type: array
                  items: {type: string}
                  rules:
                    - when: {contains: secure}
                      requires: {public: {equals: false}}
            presets:
              p:
                args:
            """;
        Check(Preset(Read(rules + " {port: 9931}\n")).Arguments.Count == 1, "absent config rule accepts port");
        Check(Preset(Read(rules + " {config: './server.conf'}\n")).Arguments.Count == 1, "present config disables absent rule");
        Bad(() => Preset(Read(rules + " {}\n")), "argument-requires");
        Bad(() => Preset(Read(rules + " {config: a, public: true}\n")), "argument-conflicts");
        Check(Preset(Read(rules + " {port: 0, public: false, tags: [secure]}\n")).Arguments.Count == 3, "false and zero are present and contains rule matches");
        Bad(() => Preset(Read(rules + " {port: 1, tags: [secure]}\n")), "argument-requires");
        Check(Preset(Read(rules + " {config: ''}\n")).Arguments.Count == 1, "empty string is present");
        Write("unset.yaml", "presets: {p: {args: {config: null}}}");
        Bad(() => Preset(Read(rules + " {config: a}\n", "unset.yaml")), "argument-requires");

        var applicability = With("{type: integer, applies-to: {distribution: official, min-build: b10, max-build: b20, capabilities: [router]}}", "{x: 1}");
        Check(Preset(applicability).Diagnostics.Count == 1, "unknown evidence is diagnostic");
        Check(Preset(applicability, new("official", "b10", new HashSet<string> { "router" })).Diagnostics.Count == 0, "inclusive minimum build");
        Check(Preset(applicability, new("official", "b20", new HashSet<string> { "router" })).Diagnostics.Count == 0, "inclusive maximum build");
        Bad(() => Preset(applicability, new("official", "b21", new HashSet<string> { "router" })), "argument-not-applicable");
        Bad(() => Preset(applicability, new("fork", "b12")), "argument-not-applicable");
        Check(Preset(With("{type: string, applies-to: {min-build: b10}}", "{x: yes}"), new("fork", "b9")).Diagnostics.Count == 1, "external versions not compared");

        var free = Read("presets: {p: {args: {z: false, text: 'false', a: true, count: 00123}}}");
        Check(Cli(free).SequenceEqual(["--a", "--count", "123", "--text", "false"]), "ordinal free scalar emission");
        Check(Cli(With("{type: boolean, emit: {cli: {mode: flag, name: yes-x, negative-name: no-x}}}", "{x: false}")).SequenceEqual(["--no-x"]), "negative flag");
        Check(Cli(With("{type: boolean, emit: {cli: {mode: value, true-value: enabled, false-value: disabled}}}", "{x: false}")).SequenceEqual(["--x", "disabled"]), "boolean with explicit value");
        Bad(() => Cli(With("{type: boolean, emit: {cli: {mode: value}}}", "{x: true}")), "boolean-representation-required");
        var list = With("{type: array, items: {type: string}, emit: {cli: {mode: repeat}, ini: {mode: repeat}}}", "{x: [a, b]}");
        var adapter = new FixtureAdapter();
        Check(Cli(list, adapter).SequenceEqual(["--x", "a", "--x", "b"]), "accredited repeat retains order");
        Bad(() => Cli(list), "repeat-evidence-required");
        Check(Cli(With("{type: array, items: {type: string}, emit: {cli: {mode: join, separator: ','}}}", "{x: [a, b]}")).SequenceEqual(["--x", "a,b"]), "join emission");
        Bad(() => Cli(With("{type: array, items: {type: string}, emit: {cli: {mode: join, separator: ','}}}", "{x: ['a,b', c]}")), "unrepresentable-join");
        Check(Cli(With("{type: array, items: {type: string}, emit: {cli: {mode: join, separator: ','}}}", "{x: []}")).Length == 0, "empty array emits nothing");
        Bad(() => Cli(Read("presets: {p: {args: {x: [a, b]}}}")), "array-emission-required");
        foreach (var text in new[] { "a\\nb", "a\\u0000b" })
            Bad(() => Cli(Read("presets: {p: {args: {x: \"" + text + "\"}}}")), "unrepresentable-text");
        var collision = Read("arg-defs: {definitions: {x: {type: string, emit: {cli: {mode: value, name: z}}}, y: {type: string, emit: {cli: {mode: value, name: z}}}}}\npresets: {p: {args: {x: a, y: b}}}");
        Bad(() => Cli(collision), "emission-key-collision");
        var special = Read("presets: {p: {args: {x: 'a path/with spaces; $(echo nope) & | " + '"' + "quoted" + '"' + "'}}}");
        var plan = new ArgumentProcessPlan(Cli(special), "");
        var start = plan.StartInfo(Environment.ProcessPath!);
        start.RedirectStandardOutput = true;
        start.ArgumentList.Insert(0, "--argument-echo");
        using (var child = Process.Start(start)!)
        {
            var output = child.StandardOutput.ReadToEnd(); child.WaitForExit();
            var actual = JsonNode.Parse(output)!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            Check(child.ExitCode == 0 && actual.SequenceEqual(plan.Cli), "real subprocess preserves tokens without shell");
        }

        var resource = Read("models: {main: {files: ['./model.gguf']}}\npresets: {p: {args: {model: 'model://main', text: {literal: 'model://missing'}}}}");
        var block = Preset(resource);
        Check(block.Arguments["model"].Reference == new ArgumentReference("model", "main") && block.Arguments["text"].Reference is null, "typed reference versus literal wrapper");
        Bad(() => ArgumentEmission.Cli(resource, block), "unresolved-resource-reference");
        var resolved = ArgumentEmission.Cli(resource, block, resolve: r => r.Name == "main" ? "D:/fixture weights/model.gguf" : throw new Exception());
        Check(resolved.SequenceEqual(["--model", "D:/fixture weights/model.gguf", "--text", "model://missing"]), "one entry path per reference");
        Bad(() => Preset(Read("presets: {p: {args: {model: 'model://missing'}}}")), "unknown-resource-reference");

        var iniRecipe = Read("presets: {p: {args: {ctx-size: 4096, metrics: true, x: 'a path/file'}}}");
        Check(ArgumentEmission.Ini(iniRecipe, Preset(iniRecipe), "p", adapter) == "[p]\nctx-size=4096\nmetrics=true\nx=a path/file\n", "INI exact keys and internal spaces");
        Bad(() => ArgumentEmission.Ini(iniRecipe, Preset(iniRecipe), "bad]section", adapter), "invalid-ini-name");
        foreach (var value in new[] { "a;b", "a#b", " padded", "padded " })
        {
            var r = Read("presets: {p: {args: {x: '" + value + "'}}}");
            Bad(() => ArgumentEmission.Ini(r, Preset(r), "p", adapter), "unrepresentable-ini-value");
        }
        Check(ArgumentEmission.Ini(list, Preset(list), "p", adapter) == "[p]\nx=a\nx=b\n", "fixture codec repeat support");
        var official = new LlamaB11482ArgumentAdapter();
        var extended = Read("""
            arg-defs:
              definitions:
                custom-batch:
                  type: integer
                  scope: preset
                  emit: {ini: {name: batch-size, mode: value}}
            presets:
              p:
                args: {custom-batch: 512, ubatch-size: 128, parallel: 2, n-cpu-moe: 1}
              q:
                args: {custom-batch: 1024, ubatch-size: 256, parallel: 4, n-cpu-moe: 0}
            """);
        var extendedPlan = ArgumentEmission.Compose(extended, ArgumentValidation.Validate(extended), official);
        Check(extendedPlan.Cli.Count == 0 && extendedPlan.Ini.Contains("[p]\nbatch-size=512\nn-cpu-moe=1\nparallel=2\nubatch-size=128\n")
            && extendedPlan.Ini.Contains("[q]\nbatch-size=1024\nn-cpu-moe=0\nparallel=4\nubatch-size=256\n"),
            "partial official catalog does not block custom INI rules or independent preset values");
        var plainBatch = Read("presets: {p: {args: {batch-size: 512}}}");
        Check(ArgumentEmission.Ini(plainBatch, Preset(plainBatch), "p", official) == "[p]\nbatch-size=512\n", "unknown permissive scalar emits to INI");
        var serverOnly = Read("presets: {p: {args: {port: 9931}}}");
        Bad(() => ArgumentEmission.Ini(serverOnly, Preset(serverOnly), "p", official), "unsupported-argument-destination");
        var officialRecipe = Read("presets: {p: {args: {load-on-startup: false, temperature: 0.7}}}");
        Check(ArgumentEmission.Ini(officialRecipe, Preset(officialRecipe), "p", official) == "[p]\nload-on-startup=false\ntemperature=0.7\n", "bounded official codec and mapping");
        var incompatible = Read("presets: {p: {args: {cli-only: a}}, q: {args: {cli-only: b}}}");
        Bad(() => ArgumentEmission.Compose(incompatible, ArgumentValidation.Validate(incompatible), adapter), "incompatible-preset-cli");
        var compatible = Read("presets: {p: {args: {cli-only: a}}, q: {args: {cli-only: a}}}");
        Check(ArgumentEmission.Compose(compatible, ArgumentValidation.Validate(compatible), adapter).Cli.SequenceEqual(["--cli-only", "a"]), "common CLI emitted once");
        var missing = Read("presets: {p: {args: {cli-only: a}}, q: {}} ");
        Bad(() => ArgumentEmission.Compose(missing, ArgumentValidation.Validate(missing), adapter), "incompatible-preset-cli");

        var existing = With("{type: string}", "{x: a}", "strict");
        var before = ArgumentSchema.Load(existing);
        var replacement = Schema("    x: {type: integer}").Definitions["x"];
        var candidate = before.Define("x", replacement);
        Check(ArgumentValidation.Impact(existing, candidate).Single().Code == "invalid-argument-value", "schema edit impact preserves incompatible assignments");
        Check(before.Definitions["x"].Get("type")!.Text == "string" && Preset(existing).Arguments["x"].Value.Text == "a", "candidate does not mutate current schema or args");
        Check(ArgumentValidation.Impact(existing, before.Remove("x")).Single().Code == "unknown-argument", "remove remains invalid under strict");
        var permissive = With("{type: string}", "{x: a}");
        Check(ArgumentValidation.Impact(permissive, ArgumentSchema.Load(permissive).Remove("x")).Count == 0, "removing schema preserves free values");

        var referenceRule = """
            models: {main: {files: ['./main.gguf']}}
            arg-defs:
              definitions:
                choice:
                  type: string
                  rules:
                    - when: {equals: 'model://main'}
                      requires: {required: {present: true}}
                required: {type: boolean}
            presets:
              p:
                args:
            """;
        Bad(() => Preset(Read(referenceRule + " {choice: 'model://main'}\n")), "argument-requires");
        Check(Preset(Read(referenceRule + " {choice: {literal: 'model://main'}}\n")).Arguments.Count == 1, "reference condition does not match a literal of identical text");
        var serverIni = Read("service: {backend: {executable: './server'}, server: {args: {x: a}}}\narg-defs: {definitions: {x: {type: string, scope: server, emit: {ini: {mode: value, name: key}}}}}");
        var serverPlan = ArgumentEmission.Compose(serverIni, ArgumentValidation.Validate(serverIni), adapter);
        Check(serverPlan.Cli.Count == 0 && serverPlan.Ini == "[*]\nkey=a\n", "process INI arguments use global section");
        var overridden = Read("service: {backend: {executable: './server'}, server: {args: {x: a}}}\npresets: {p: {args: {x: b}}}");
        Bad(() => ArgumentEmission.Compose(overridden, ArgumentValidation.Validate(overridden), adapter), "server-preset-cli-conflict");
        var disabled = Read("presets: {p: {args: {cli-only: a}}, q: {enable: false, args: {cli-only: b}}}");
        Check(ArgumentEmission.Compose(disabled, ArgumentValidation.Validate(disabled), adapter).Cli.SequenceEqual(["--cli-only", "a"]), "disabled presets do not affect runtime composition");
        Bad(() => ArgumentEmission.Ini(iniRecipe, Preset(iniRecipe), "*", adapter), "reserved-ini-section");
        Check(Preset(With("{type: string, pattern: '^(?>a+)$'}", "{x: aaa}")).Arguments.Count == 1, "permitted .NET atomic group");
        Check(Preset(With("{type: string, pattern: '^[\\1]+$'}", "{x: \"\\u0001\"}")).Arguments.Count == 1, "octal character class is not a backreference");
        Check(Preset(With("{type: string, pattern: '^[\\[\\](?=)]+$'}", "{x: '(?=)'}")).Arguments.Count == 1, "literal lookaround text inside character class");
        Bad(() => Preset(With("{type: string, pattern: \"(?x)# [\\n(?=a)a\"}", "{x: a}")), "invalid-pattern");
        var slowPattern = With("{type: string, pattern: '^(a+)+$'}", "{x: '" + new string('a', 20_000) + "!'}");
        Bad(() => Preset(slowPattern), "pattern-inconclusive");
        Bad(() => ArgumentEmission.Ini(iniRecipe, Preset(iniRecipe), "p:suffix", adapter), "reserved-ini-section");
        Bad(() => ArgumentEmission.Ini(iniRecipe, Preset(iniRecipe), "DEFAULT", adapter), "reserved-ini-section");
        var secretRecipe = Read("arg-defs:\n  definitions:\n    x: {type: integer}\npresets:\n  p:\n    args:\n      x: ${SECRET}\n");
        try { Preset(secretRecipe); throw new Exception("Expected secret validation failure"); }
        catch (RecipeException e) { Check(!e.Message.Contains(env["SECRET"]!), "validation diagnostics do not expose secret value"); }
        Console.WriteLine($"Argument tests: {assertions} checks OK. Fixtures: {root}");
    }

    private static void Editing(string root)
    {
        var folder = Path.Combine(root, "edit");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "coquena.yaml");
        var definitions = Path.Combine(folder, "defs.yaml");
        var overlay = Path.Combine(folder, "override.yaml");
        var field = Path.Combine(folder, "field.yaml");
        var home = Path.Combine(folder, "home");
        var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        Environment.SetEnvironmentVariable("COQUENA_HOME", home);
        try
        {
            const string source = """
                # recipe
                name: test
                models: {main: {files: [./main.gguf]}, remote: "author/weights:Q4_0"}
                presets:
                  p:
                    args: {model: 'model://main', n: 2, description: old}
                """;
            File.WriteAllText(file, source);
            File.WriteAllText(overlay, "# override\r\n");
            File.WriteAllText(field, "type: string");
            var files = new[] { "-f", file, "-f", overlay };
            int Run(string[] selection, params string[] command)
            {
                using var error = new StringWriter();
                var status = Coquena.Cli.Run([.. selection, .. command], TextWriter.Null, error);
                if (status != 0 && error.ToString().Length == 0) throw new Exception("Expected edit diagnostic.");
                return status;
            }
            Check(Run(files, "param", "define", "description", "--file", field) == 0 &&
                File.ReadAllText(overlay).Contains("arg-defs"), "arg-defs falls back to last layer");
            File.WriteAllText(overlay, "# override\r\n");
            File.WriteAllText(definitions, "arg-defs:\r\n  definitions:\r\n    n:\r\n      type: integer # type\r\n      aliases: [count]\r\n",
                new System.Text.UnicodeEncoding(false, true));
            files = ["-f", file, "-f", definitions, "-f", overlay];

            Check(Run(files, "param", "set", "n", "minimum", "1") == 0 &&
                File.ReadAllText(definitions).Contains("# type") && File.ReadAllBytes(definitions)[0] == 0xff &&
                File.ReadAllText(overlay) == "# override\r\n", "parameter edits target last declaration and preserve encoding");
            Check(Run(files, "preset", "set", "p", "count", "3") == 0 &&
                Run(files, "preset", "set", "p", "description", "\"model://main\"") == 0 &&
                Run(files, "preset", "clone", "p", "q") == 0 &&
                Run(files, "preset", "unset", "p", "description") == 0, "preset override and clone commands");
            var recipe = RecipeReader.Read(new(folder, [file, definitions, overlay]));
            var blocks = ArgumentValidation.Validate(recipe);
            var sourceArgs = blocks.Single(b => b.Key == "p").Arguments;
            var cloneArgs = blocks.Single(b => b.Key == "q").Arguments;
            Check(sourceArgs["n"].Value.Text == "3" && !sourceArgs.ContainsKey("description") &&
                cloneArgs["description"].Literal && cloneArgs["description"].Value.Text == "model://main" &&
                cloneArgs["model"].Reference?.Name == "main", "clone preserves values and bindings independently");
            var schemaBytes = File.ReadAllBytes(definitions);
            Check(Run(files, "param", "set", "n", "type", "boolean") == 2 &&
                File.ReadAllBytes(definitions).AsSpan().SequenceEqual(schemaBytes), "incompatible edits leave source intact");

            var originalOverlay = File.ReadAllText(overlay);
            var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(overlay.ToUpperInvariant())));
            using var gate = new Mutex(false, "CoquenaRecipeEdit-" + id);
            gate.WaitOne();
            var pending = Task.Run(() => Run(files, "preset", "set", "p", "n", "4"));
            try
            {
                Check(SpinWait.SpinUntil(() => pending.IsCompleted || Directory.EnumerateFiles(folder, "*.tmp").Any(),
                    TimeSpan.FromSeconds(10)) && !pending.IsCompleted, "candidate staged before save");
                File.AppendAllText(file, "\r\n# external edit\r\n");
            }
            finally { gate.ReleaseMutex(); }
            Check(pending.GetAwaiter().GetResult() == 3 && File.ReadAllText(overlay) == originalOverlay &&
                File.ReadAllText(file).EndsWith("# external edit\r\n", StringComparison.Ordinal),
                "external layer change rejects staged edit and preserves both files");
            Check(Run(files, "backend", "set", "--executable", "./server.exe") == 0 &&
                Run(files, "model", "add", "--name", "local", "--file", "./local.gguf") == 0 &&
                Run(files, "model", "add", "--name", "local", "--file", "./local.gguf") == 0 &&
                Run(files, "model", "set", "remote", "--revision", "stable") == 0 &&
                Run(files, "model", "companion", "set", "aux", "--type", "mmproj", "--weight", "./aux.gguf") == 0,
                "backend and resource definition commands save declaratively");
            Check(!Directory.Exists(home) && !Directory.Exists(Path.Combine(folder, ".coquena")) &&
                !Directory.EnumerateFiles(folder, "*.tmp").Any(), "save performs no preparation or registration");
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
    }

    private sealed class FixtureAdapter : IArgumentAdapter, IArgumentIniCodec
    {
        private readonly LlamaB11482ArgumentAdapter codec = new();
        public IArgumentIniCodec IniCodec => this;
        public ArgumentEmitRule? Rule(string name, string scope, string destination) => null;
        public bool Supports(string name, string scope, string destination) => destination != "ini" || name != "cli-only";
        public bool Accumulates(string name, string destination) => true;
        public bool SupportsLineBreaks(string name, string destination) => false;
        public string Section(string name, RecipeOrigin origin) => codec.Section(name, origin);
        public string Entry(string key, string value, RecipeOrigin origin) => codec.Entry(key, value, origin);
    }
    private static void Check(bool ok, string message)
    { assertions++; if (!ok) throw new Exception("Argument test failed: " + message); }
    private static void Bad(Action action, string code)
    {
        try { action(); }
        catch (RecipeException e) { Check(e.Code == code, "expected " + code + ", got " + e.Code); return; }
        throw new Exception("Expected argument failure: " + code);
    }
    private static void BadAny(Action action, string message)
    {
        try { action(); }
        catch (RecipeException) { Check(true, message); return; }
        throw new Exception("Expected argument failure: " + message);
    }
}
