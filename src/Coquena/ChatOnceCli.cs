using System.Globalization;
using System.Text.Json.Nodes;
using Coquena.Chat;
using Coquena.Servers;

namespace Coquena;

// chat <preset> -p|--prompt <text|-> [--json] [--reasoning]: one request without a terminal.
// In text, stdout carries only the answer as it arrives (and the reasoning with --reasoning);
// the metrics go to stderr at the end, so the answer can be piped. --json writes one object.
internal static class ChatOnceCli
{
    public static int Run(Uri address, string service, string entry, string prompt, bool json, bool reasoning, bool load,
        TextWriter output, TextWriter error)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            if (load) error.WriteLine(PresetLoad.RunAsync(new LlamaPresetRouter(client, address), entry, true, cancel.Token).GetAwaiter().GetResult());
            var session = new ChatSession(client, address, entry);
            var printer = new Printer(output, json ? null : reasoning);
            try { session.SendAsync(prompt, () => { lock (session.Gate) printer.Write(session.Turns[^1]); }, cancel.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            var turn = session.Turns[^1];
            if (json) output.WriteLine(Json(service, entry, turn).ToJsonString(new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            else
            {
                printer.Write(turn);
                printer.End();
                error.WriteLine(string.Join(" · ", Metrics(turn).Prepend(ChatSession.FinishLabel(turn.Finish))));
                if (turn.Problem is { } problem) error.WriteLine("chat: " + problem);
            }
            output.Flush();
            return cancel.IsCancellationRequested ? 130 : turn.Problem is not null || turn.Phase == ChatPhase.Failed ? 1 : 0;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    // The same names as the telemetry row of the interactive chat.
    internal static IEnumerable<string> Metrics(ChatTurn turn)
    {
        if (turn.Timings is not { } t) { yield return Messages.ChatNoTelemetry; yield break; }
        yield return "prompt " + ChatSession.Rate(t.PromptRate) + " tk/s · " + t.Prompt + " tk";
        yield return "gen " + ChatSession.Rate(t.PredictedRate) + " tk/s · " + t.Predicted + " tk";
        if (t.Cached + t.Prompt > 0) yield return $"{Messages.ChatCache} {Math.Round(100d * t.Cached / (t.Cached + t.Prompt))}%";
        if (t.Drafted is > 0) yield return $"{Messages.ChatAccepted} {t.Accepted}/{t.Drafted}";
        if (turn.FirstToken is { } first) yield return Messages.ChatFirstToken + " " + first.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
    }

    internal static JsonObject Json(string service, string entry, ChatTurn turn)
    {
        var t = turn.Timings;
        var result = new JsonObject
        {
            ["service"] = service,
            ["preset"] = entry,
            ["response"] = turn.Text.ToString(),
            ["reasoning"] = turn.Reasoning.Length == 0 ? null : turn.Reasoning.ToString(),
            ["finish"] = turn.Finish,
            ["problem"] = turn.Problem,
            ["metrics"] = t is null ? null : new JsonObject
            {
                ["prompt_tokens"] = t.Prompt,
                ["cached_tokens"] = t.Cached,
                ["prompt_tokens_per_second"] = t.PromptRate,
                ["generated_tokens"] = t.Predicted,
                ["generation_tokens_per_second"] = t.PredictedRate,
                ["drafted_tokens"] = t.Drafted,
                ["accepted_tokens"] = t.Accepted,
                ["first_token_seconds"] = turn.FirstToken?.TotalSeconds
            }
        };
        return result;
    }

    // Writes the new part of the reasoning (when shown) and of the answer as they grow.
    internal sealed class Printer(TextWriter output, bool? reasoning)
    {
        private int reasoningWritten, textWritten;
        private bool reasoningOpen;

        public void Write(ChatTurn turn)
        {
            if (reasoning is null) return;
            if (reasoning == true && turn.Reasoning.Length > reasoningWritten)
            {
                if (!reasoningOpen) { output.Write(Messages.DisplayReasoningTag + "\n"); reasoningOpen = true; }
                output.Write(turn.Reasoning.ToString(reasoningWritten, turn.Reasoning.Length - reasoningWritten));
                reasoningWritten = turn.Reasoning.Length;
            }
            if (turn.Text.Length > textWritten)
            {
                if (reasoningOpen) { output.Write("\n\n"); reasoningOpen = false; }
                output.Write(turn.Text.ToString(textWritten, turn.Text.Length - textWritten));
                textWritten = turn.Text.Length;
            }
            output.Flush();
        }

        public void End()
        {
            if (reasoning is null) return;
            if (textWritten > 0 || reasoningWritten > 0) output.WriteLine();
        }
    }
}
