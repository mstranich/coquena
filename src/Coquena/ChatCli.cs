using System.Text;
using Coquena.Backends;
using Coquena.Chat;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena;

// chat <preset> [--service <name> | --global] [--load] [--reasoning]: the quick test of Herramientas › Chat
// in a terminal. With -p/--prompt it sends one request and ends (ChatOnceCli).
// The telemetry row stays below the input and is redrawn in place, without adding lines.
internal static class ChatCli
{
    public static string Usage => Messages.ChatUsage;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["--help"] or ["-h"])
        {
            output.WriteLine(Usage);
            output.WriteLine(Messages.ChatHelp);
            return 0;
        }
        string? reference = null, serviceName = null, prompt = null;
        var valid = true;
        bool load = false, json = false, reasoning = false;
        for (var i = 0; i < args.Length && valid; i++)
        {
            if (args[i] == "--service" && i + 1 < args.Length && serviceName is null) serviceName = args[++i];
            else if (args[i] is "--global" or "-g" && serviceName is null) serviceName = "global";
            else if (args[i] == "--load" && !load) load = true;
            else if (args[i] == "--json" && !json) json = true;
            else if (args[i] == "--reasoning" && !reasoning) reasoning = true;
            else if (args[i] is "-p" or "--prompt" && i + 1 < args.Length && prompt is null) prompt = args[++i];
            else if (!args[i].StartsWith('-') && reference is null) reference = args[i];
            else valid = false;
        }
        if (!valid || reference is null || reference.Length == 0 || reference.StartsWith('/') || reference.EndsWith('/') ||
            reference.Contains("//", StringComparison.Ordinal) || json && prompt is null)
        {
            error.WriteLine(Usage);
            return 2;
        }
        // "-" reads the whole prompt from standard input.
        if (prompt == "-") prompt = Console.In.ReadToEnd();
        if (prompt is not null && prompt.Trim().Length == 0)
        {
            error.WriteLine(Messages.ChatEmptyPrompt);
            return 2;
        }
        if (prompt is null && (Console.IsInputRedirected || Console.IsOutputRedirected))
        {
            error.WriteLine(Messages.ChatNeedsTerminal);
            return 2;
        }
        try
        {
            if (Resolve(reference, serviceName, output, error, offerStart: prompt is null) is not var (target, address, entry)) return 2;
            if (prompt is not null) return ChatOnceCli.Run(address, target.Service!.Name, entry, prompt, json, reasoning, load, output, error);
            return Converse(address, target.Service!.Name, entry, load, reasoning, output);
        }
        catch (Exception e) when (ServiceObservation.ObservationError(e) || e is BackendException or Coquena.Downloads.DownloadException or ModelSourceException or RecipeException or StorageException)
        {
            error.WriteLine("chat: " + e.Message);
            return 2;
        }
    }

    // A candidate destination: a service and the published names that may identify the preset.
    private sealed record Candidate(DashboardCli.Target Target, string[] Entries);

    // The preset is named by its published name (proyecto/clave), by the key of this folder's
    // recipe, or as servicio/proyecto/clave. Without --service, the recipe's destination decides
    // the service; when that does not apply, running services are searched for the name.
    internal static (DashboardCli.Target Target, Uri Address, string Entry)? Resolve(string reference, string? serviceName, TextWriter output, TextWriter error, bool offerStart = true)
    {
        var discovered = DashboardCli.Discover(out var diagnostic);
        var targets = discovered.Where(target => target.Service is not null).ToList();
        var local = discovered.FirstOrDefault(target => !target.Store.Context.IsGlobal && target.Recipe is not null);
        var slash = reference.IndexOf('/');
        string[] names = slash < 0 && local?.ProjectName is { } project ? [project + "/" + reference, reference] : [reference];
        var candidates = new List<Candidate>();

        if (serviceName is not null)
        {
            var chosen = Services(serviceName, targets, local);
            if (chosen.Count == 0) { error.WriteLine(Messages.ChatNoService.Format(serviceName, targets.Count == 0 ? diagnostic : "").Trim()); return null; }
            if (chosen.Count > 1) { error.WriteLine(Messages.ChatServiceAmbiguous.Format(serviceName)); return null; }
            candidates.Add(new(chosen[0], names));
        }
        else
        {
            // servicio/proyecto/clave stays available as an alternative.
            if (slash > 0 && targets.Where(target => target.Service!.Name == reference[..slash]).ToList() is [var named])
                candidates.Add(new(named, [reference[(slash + 1)..]]));
            var owner = slash < 0 ? local : Project(reference[..reference.LastIndexOf('/')], local);
            if (owner?.Destination is { } destination && Services(destination, targets, owner) is [var declared])
                candidates.Add(new(declared, names));
        }

        // A running candidate that publishes the name is the destination.
        foreach (var candidate in candidates)
        {
            var (address, _) = new ServiceObservation(candidate.Target.Store).Endpoint(candidate.Target.Service!);
            if (address is not null && Published(candidate.Target, candidate.Entries) is { } entry) return (candidate.Target, address, entry);
        }

        // A stopped destination is started only with the user's answer.
        foreach (var candidate in candidates)
        {
            var observation = new ServiceObservation(candidate.Target.Store);
            var (address, problem) = observation.Endpoint(candidate.Target.Service!);
            if (address is not null) continue;
            if (problem is not null) { error.WriteLine(Messages.ChatServiceUnavailable.Format(problem)); return null; }
            var name = candidate.Target.Service!.Name;
            if (!offerStart) { error.WriteLine(Messages.PresetServiceStopped.Format(name)); return null; }
            output.Write(Messages.ChatStartQuestion.Format(name));
            if (!Messages.IsYes(Console.ReadLine())) return null;
            Start(candidate.Target, output, error);
            (address, problem) = observation.Endpoint(candidate.Target.Service!);
            if (address is null) { error.WriteLine(Messages.ChatServiceNotReady.Format(problem)); return null; }
            if (Published(candidate.Target, candidate.Entries) is { } entry) return (candidate.Target, address, entry);
            error.WriteLine(Messages.ChatNotPublishedIn.Format(reference, name, Listing(candidate.Target)).Trim());
            return null;
        }

        // Otherwise, the running services are searched for the name.
        var searched = serviceName is null ? targets : candidates.Select(candidate => candidate.Target).ToList();
        var matches = new List<(DashboardCli.Target Target, Uri Address, string Entry)>();
        var known = new List<string>();
        foreach (var target in searched)
        {
            var (address, _) = new ServiceObservation(target.Store).Endpoint(target.Service!);
            if (address is null) continue;
            var entries = Entries(target);
            known.AddRange(entries.Select(entry => entry + " (" + target.Service!.Name + ")"));
            var match = names.FirstOrDefault(entries.Contains) ??
                (slash < 0 ? entries.Where(entry => entry.EndsWith("/" + reference, StringComparison.Ordinal)).ToList() is [var single] ? single : null : null);
            if (match is not null) matches.Add((target, address, match));
        }
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1)
        {
            error.WriteLine(Messages.ChatPublishedInMany.Format(reference));
            foreach (var match in matches) error.WriteLine($"  coquena chat {match.Entry} --service {match.Target.Service!.Name}");
            return null;
        }
        error.WriteLine(Messages.ChatNoPreset.Format(reference) + (targets.Count == 0 ? " " + diagnostic : ""));
        if (known.Count > 0) error.WriteLine(Messages.ChatPublishedPresets.Format(string.Join(", ", known.Distinct())));
        return null;
    }

    // default and global name the designated global service, self the project's own; otherwise a name.
    private static List<DashboardCli.Target> Services(string selector, List<DashboardCli.Target> targets, DashboardCli.Target? project) =>
        selector switch
        {
            "self" => project?.Service is not null ? [project] : [],
            "default" or "global" => targets.Where(target => target.IsDefault && target.Store.Context.IsGlobal).ToList(),
            _ => targets.Where(target => target.Service!.Name == selector).ToList()
        };

    // The recipe with that project name: this folder's, or one of the project index.
    private static DashboardCli.Target? Project(string name, DashboardCli.Target? local)
    {
        if (local?.ProjectName == name) return local;
        foreach (var place in new DashboardCli.DefinitionsSource(Home.Resolve(), Environment.CurrentDirectory).Places())
        {
            if (place.Scope.Global || place.Recipe is null || place.Context is null || place.Scope.Name != name) continue;
            var store = new ScopeStore(place.Context);
            ServiceIdentity? service = null;
            try { service = new RecipeRegistry(store).FindProjectService(place.Recipe.Files); }
            catch (StorageException error) when (error.Code == "unknown-service") { }
            return new(store, service, false, place.Recipe, name, Destination: RecipeReader.Read(place.Recipe).Target);
        }
        return null;
    }

    private static List<string> Entries(DashboardCli.Target target) =>
        new ServiceObservation(target.Store).CaptureAsync(target.Service!).GetAwaiter().GetResult().Entries.Select(entry => entry.Name).ToList();

    private static string? Published(DashboardCli.Target target, string[] names)
    {
        var entries = Entries(target);
        return names.FirstOrDefault(entries.Contains);
    }

    private static string Listing(DashboardCli.Target target) =>
        Entries(target) is { Count: > 0 } entries ? Messages.ChatPresetsList.Format(string.Join(", ", entries)) : "";

    // --load: the router loads the preset while the chat is already open. Until it is loaded,
    // messages are not sent; the text stays in the input and /exit always leaves.
    private sealed class Loader
    {
        private readonly CancellationTokenSource stop = new();
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        public volatile bool Loading = true;
        public volatile string? Problem;

        public Loader(HttpClient client, Uri address, string entry)
        {
            var router = new LlamaPresetRouter(client, address);
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await router.RequestLoadAsync(entry, stop.Token)) { Problem = Messages.ChatLoadRejected; return; }
                    while (true)
                    {
                        var (loaded, failed) = await router.LoadStateAsync(entry, stop.Token);
                        if (failed) { Problem = Messages.ChatLoadFailed; return; }
                        if (loaded) return;
                        await Task.Delay(500, stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException) { Problem = Messages.ChatNoLoadState.Format(error.Message); }
                finally { Loading = false; }
            });
        }

        // While loading, the row says so with the elapsed time; a failed load is shown until the end.
        public string? Status => Loading
            ? DashboardPresentation.Spinner() + Messages.ChatLoadingModel.Format((int)(DateTimeOffset.UtcNow - started).TotalSeconds)
            : Problem is { } problem ? "✗ " + problem : null;

        public bool Blocks => Loading;
        public void Stop() => stop.Cancel();
    }

    // Same operation as Iniciar in the TUI, only after the user's answer.
    private static void Start(DashboardCli.Target target, TextWriter output, TextWriter error)
    {
        var lifecycle = new ScopedServiceProcess(target.Store, output, error);
        var context = target.Store.Context;
        var service = target.Service!;
        var selection = context.IsGlobal
            ? new RecipeImporter(context).ServiceSelection(service.ServiceId)
            : target.Recipe ?? new RecipeSelection(context.Base, new RecipeRegistry(target.Store).Get(service.RecipeId).Files, context.Base);
        var instance = ScopedServiceCli.RunCandidateAsync(lifecycle, new(context, selection, service), false, detached: true,
            privateResources: false, output, error, new ConsoleModelSelectionInteraction(), CancellationToken.None).GetAwaiter().GetResult();
        output.WriteLine(Messages.ServiceInstance.Format(service.Name, instance));
    }

    private static int Converse(Uri address, string service, string entry, bool load, bool reasoning, TextWriter output)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        var session = new ChatSession(client, address, entry);
        var loader = load ? new Loader(client, address, entry) : null;
        var screen = new Screen(output, service + "/" + entry, loader) { ShowReasoning = reasoning };
        output.WriteLine(Messages.ChatWith.Format(service, entry));
        Console.TreatControlCAsInput = true;
        try
        {
            while (true)
            {
                var text = screen.ReadLine(session);
                if (text is null || text.Trim() == "/exit") break;
                if (text.Trim().Length == 0) continue;
                if (text.Trim() == "/reasoning")
                {
                    screen.ShowReasoning = !screen.ShowReasoning;
                    output.WriteLine(screen.ShowReasoning ? Messages.ChatReasoningShown : Messages.ChatReasoningHidden);
                    continue;
                }
                if (!Answer(session, text, screen)) break;
            }
        }
        finally
        {
            Console.TreatControlCAsInput = false;
            loader?.Stop();
            screen.Close();
        }
        return 0;
    }

    // Streams the answer above the telemetry row. Ctrl+C cancels this request and leaves.
    private static bool Answer(ChatSession session, string text, Screen screen)
    {
        using var cancel = new CancellationTokenSource();
        var interrupted = false;
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; interrupted = true; cancel.Cancel(); };
        Console.TreatControlCAsInput = false;
        Console.CancelKeyPress += handler;
        try
        {
            screen.BeginAnswer();
            var gate = new object();
            session.SendAsync(text, () => { lock (gate) screen.Update(session); }, cancel.Token).GetAwaiter().GetResult();
            lock (gate) screen.EndAnswer(session);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            Console.TreatControlCAsInput = true;
        }
        return !interrupted;
    }

    // Input area, answer text and the permanent telemetry row, drawn with ANSI sequences.
    private sealed class Screen(TextWriter output, string target, Loader? loader)
    {
        private int inputRow;       // Row of the cursor inside the input area.
        private int answerColumn;   // Column where the streamed answer continues.
        private int written;        // Characters of the current answer already printed.
        private string? notice;     // Why Enter did not send, until the next key.
        private int thought;        // Characters of the current reasoning already printed.
        public bool ShowReasoning { get; set; }

        private static int Width => Math.Max(20, Console.WindowWidth);

        public string? ReadLine(ChatSession session)
        {
            var buffer = new StringBuilder();
            var cursor = 0;
            inputRow = 0;
            notice = null;
            Draw(buffer.ToString(), cursor, Telemetry(session));
            var shown = Telemetry(session);
            while (true)
            {
                // Without a key, the row is redrawn when it changes (load progress) without touching the text.
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(100);
                    if (Telemetry(session) is var current && current != shown) { shown = current; Draw(buffer.ToString(), cursor, current); }
                    continue;
                }
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { Draw(buffer.ToString(), buffer.Length, Telemetry(session)); return null; }
                switch (key.Key)
                {
                    case ConsoleKey.Enter when loader?.Blocks == true && buffer.ToString().Trim() is { Length: > 0 } pending && pending != "/exit":
                        notice = Messages.ChatCannotSendYet;
                        break;
                    case ConsoleKey.Enter:
                        var line = buffer.ToString();
                        Draw(line, line.Length, null);
                        return line;
                    case ConsoleKey.Backspace when cursor > 0: buffer.Remove(--cursor, 1); break;
                    case ConsoleKey.Delete when cursor < buffer.Length: buffer.Remove(cursor, 1); break;
                    case ConsoleKey.LeftArrow when cursor > 0: cursor--; break;
                    case ConsoleKey.RightArrow when cursor < buffer.Length: cursor++; break;
                    case ConsoleKey.Home: cursor = 0; break;
                    case ConsoleKey.End: cursor = buffer.Length; break;
                    default:
                        if (!char.IsControl(key.KeyChar)) buffer.Insert(cursor++, key.KeyChar);
                        break;
                }
                if (key.Key != ConsoleKey.Enter) notice = null;
                shown = Telemetry(session);
                Draw(buffer.ToString(), cursor, shown);
            }
        }

        // Redraws the input from its first row; the telemetry row (if any) goes below it.
        private void Draw(string text, int cursor, string? telemetry)
        {
            var width = Width;
            var builder = new StringBuilder("\u001b[?25l");
            if (inputRow > 0) builder.Append("\u001b[").Append(inputRow).Append('A');
            builder.Append('\r').Append("\u001b[J").Append("› ").Append(text);
            var rows = (2 + text.Length) / width;
            if (telemetry is null)
            {
                builder.Append("\r\n\u001b[?25h");
                inputRow = 0;
                output.Write(builder.ToString());
                output.Flush();
                return;
            }
            builder.Append("\r\n").Append(Fit(telemetry, width));
            var position = 2 + cursor;
            var row = position / width;
            builder.Append("\u001b[").Append(rows + 1 - row).Append('A').Append('\r');
            if (position % width > 0) builder.Append("\u001b[").Append(position % width).Append('C');
            builder.Append("\u001b[?25h");
            inputRow = row;
            output.Write(builder.ToString());
            output.Flush();
        }

        public void BeginAnswer()
        {
            output.Write("\u001b[1m" + Messages.ChatAssistant + "\u001b[0m\r\n");
            answerColumn = 0;
            written = 0;
            thought = 0;
            output.Write("\u001b[?25l" + Fit("…", Width));
            output.Flush();
        }

        // Writes new answer text above the telemetry row and redraws the row.
        public void Update(ChatSession session)
        {
            var turn = session.Turns[^1];
            // With /reasoning, the reasoning goes first, dimmed and tagged; the answer follows it.
            var reasoning = ShowReasoning && turn.Reasoning.Length > thought ? turn.Reasoning.ToString(thought, turn.Reasoning.Length - thought) : "";
            var opening = ShowReasoning && thought == 0 && reasoning.Length > 0 ? Messages.DisplayReasoningTag + "\n" : "";
            if (reasoning.Length > 0) thought = turn.Reasoning.Length;
            var text = turn.Text.ToString();
            var chunk = text.Length > written ? text[written..] : "";
            var closing = thought > 0 && written == 0 && chunk.Length > 0 ? "\n\n" : "";
            written = text.Length;
            var builder = new StringBuilder("\r\u001b[K\u001b[1A\r");
            if (answerColumn > 0) builder.Append("\u001b[").Append(answerColumn).Append('C');
            var width = Width;
            void Put(string part)
            {
                foreach (var character in part.Replace("\r", ""))
                {
                    if (character == '\n') { builder.Append("\r\n"); answerColumn = 0; continue; }
                    builder.Append(character);
                    answerColumn = (answerColumn + 1) % width;
                }
            }
            if (opening.Length + reasoning.Length > 0) { builder.Append("\u001b[2m"); Put(opening + reasoning); builder.Append("\u001b[0m"); }
            Put(closing + chunk);
            builder.Append("\r\n").Append(Fit(Telemetry(session), width));
            output.Write(builder.ToString());
            output.Flush();
        }

        public void EndAnswer(ChatSession session)
        {
            Update(session);
            var turn = session.Turns[^1];
            // The final telemetry stays above the next input; problems are printed as text.
            var builder = new StringBuilder("\r\n");
            if (turn.Problem is { } problem) builder.Append(problem).Append("\r\n");
            output.Write(builder.Append("\u001b[?25h").ToString());
            output.Flush();
            inputRow = 0;
        }

        public void Close()
        {
            output.Write("\r\u001b[J\u001b[?25h");
            output.Flush();
        }

        private string Telemetry(ChatSession session)
        {
            var turn = session.Turns.LastOrDefault(candidate => candidate.Role == "assistant");
            var t = turn?.Timings;
            var parts = new List<string> { target };
            if (notice is not null) parts.Add(notice);
            if (loader?.Status is { } status) parts.Add(status);
            parts.Add(turn is null ? Messages.ChatNoRequest : turn.Phase switch
            {
                ChatPhase.Waiting => Messages.ChatWaiting,
                ChatPhase.Prompt => Messages.ChatProcessingPrompt + (turn.Progress is { } progress ? $" {Math.Round(progress.Ratio * 100)}%" : ""),
                ChatPhase.Generating => Messages.ChatGenerating,
                ChatPhase.Done => ChatSession.FinishLabel(turn.Finish),
                ChatPhase.Cancelled => Messages.ChatInterrupted,
                _ => Messages.ChatError
            });
            if (t is null) parts.Add(turn is null ? (loader?.Blocks == true ? Messages.ChatExitHint : Messages.ChatSendHint) : Messages.ChatNoTelemetry);
            else
            {
                parts.Add("prompt " + ChatSession.Rate(t.PromptRate) + " tk/s");
                parts.Add("gen " + ChatSession.Rate(t.PredictedRate) + " tk/s · " + t.Predicted + " tk");
                if (t.Cached + t.Prompt > 0) parts.Add($"{Messages.ChatCache} {Math.Round(100d * t.Cached / (t.Cached + t.Prompt))}%");
                if (t.Drafted is > 0) parts.Add($"{Messages.ChatAccepted} {t.Accepted}/{t.Drafted}");
                if (turn?.FirstToken is { } first) parts.Add(Messages.ChatFirstToken + " " + first.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " s");
            }
            return string.Join(" · ", parts);
        }

        private static string Fit(string text, int width)
        {
            var visible = text.Length >= width ? text[..(width - 2)] + "…" : text;
            return "\u001b[2m" + visible + "\u001b[0m\u001b[K";
        }
    }
}
