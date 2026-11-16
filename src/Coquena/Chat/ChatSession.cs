using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coquena.Chat;

// Request timings reported by llama-server for this response (`timings`, with
// timings_per_token). They belong to the request, not to the entry or the service.
internal sealed record ChatTimings(int Cached, int Prompt, double PromptMs, double? PromptRate,
    int Predicted, double PredictedMs, double? PredictedRate, int? Drafted, int? Accepted)
{
    // Tokens the slot holds after this response: cached + processed prompt + generated.
    public int Context => Cached + Prompt + Predicted;

    internal static ChatTimings? Parse(JsonNode? node) => node is JsonObject t ? new(
        Int(t["cache_n"]), Int(t["prompt_n"]), Number(t["prompt_ms"]) ?? 0, Positive(Number(t["prompt_per_second"])),
        Int(t["predicted_n"]), Number(t["predicted_ms"]) ?? 0, Positive(Number(t["predicted_per_second"])),
        t["draft_n"] is null ? null : Int(t["draft_n"]), t["draft_n_accepted"] is null ? null : Int(t["draft_n_accepted"])) : null;

    private static double? Positive(double? value) => value is > 0 and < double.PositiveInfinity ? value : null;
    private static int Int(JsonNode? node) => (int)(Number(node) ?? 0);
    internal static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;
}

// Prompt processing progress (`prompt_progress` with return_progress): total, reused, processed.
internal sealed record ChatProgress(int Total, int Cache, int Processed, double Milliseconds)
{
    internal static ChatProgress? Parse(JsonNode? node) => node is JsonObject p ? new(
        (int)(ChatTimings.Number(p["total"]) ?? 0), (int)(ChatTimings.Number(p["cache"]) ?? 0),
        (int)(ChatTimings.Number(p["processed"]) ?? 0), ChatTimings.Number(p["time_ms"]) ?? 0) : null;

    // The timed share excludes reused tokens, as the server documents it.
    public double Ratio => Total - Cache <= 0 ? 1 : Math.Clamp((Processed - Cache) / (double)(Total - Cache), 0, 1);
}

internal enum ChatPhase { Waiting, Prompt, Generating, Done, Cancelled, Failed }

internal sealed class ChatTurn(string role, string text, DateTimeOffset at)
{
    public string Role { get; } = role;
    public DateTimeOffset At { get; } = at;
    public StringBuilder Text { get; } = new(text);
    public StringBuilder Reasoning { get; } = new();
    public ChatPhase Phase { get; set; } = role == "user" ? ChatPhase.Done : ChatPhase.Waiting;
    public ChatTimings? Timings { get; set; }
    public ChatProgress? Progress { get; set; }
    public string? Finish { get; set; }
    public string? Problem { get; set; }
    public TimeSpan? FirstToken { get; set; }
    // Generation rate per received chunk with timings, for the curve of this request.
    public List<double> GenerationRates { get; } = [];
    public List<double> PromptRates { get; } = [];
}

// One test conversation with a published entry. Context lives only while the session is open:
// nothing is persisted and the dialog is not a benchmark result.
internal sealed class ChatSession(HttpClient client, Uri address, string entry)
{
    private readonly List<ChatTurn> turns = [];

    // Writes of the streamed answer and reads of whoever draws it take this lock: the answer
    // arrives on a worker thread while the TUI enumerates turns, text and rates.
    public object Gate { get; } = new();
    public Uri Address { get; } = address;
    public string Entry { get; } = entry;
    public IReadOnlyList<ChatTurn> Turns => turns;
    public ChatTurn? Current => turns.Count > 0 && turns[^1].Role == "assistant" && turns[^1].Phase is ChatPhase.Waiting or ChatPhase.Prompt or ChatPhase.Generating ? turns[^1] : null;
    public ChatTimings? LastTimings => turns.LastOrDefault(turn => turn.Timings is not null)?.Timings;

    // Body for llama-server's chat endpoint: the preset keeps its own sampling parameters.
    internal JsonObject Request()
    {
        var messages = new JsonArray();
        foreach (var turn in turns)
        {
            if (turn.Role == "assistant" && turn.Text.Length == 0) continue;
            if (turn.Role == "assistant" && turn.Phase is ChatPhase.Waiting or ChatPhase.Prompt or ChatPhase.Generating) continue;
            messages.Add((JsonNode)new JsonObject { ["role"] = turn.Role, ["content"] = turn.Text.ToString() });
        }
        return new JsonObject
        {
            ["model"] = Entry,
            ["messages"] = messages,
            ["stream"] = true,
            ["timings_per_token"] = true,
            ["return_progress"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true }
        };
    }

    // Sends a message and streams the answer into the last turn; `changed` runs on every chunk.
    // Cancelling closes this client's connection; that does not prove the backend stopped.
    public async Task SendAsync(string text, Action changed, CancellationToken cancellation)
    {
        if (Current is not null) throw new InvalidOperationException(Messages.ChatResponseRunning);
        JsonObject body;
        var answer = new ChatTurn("assistant", "", DateTimeOffset.Now);
        lock (Gate)
        {
            turns.Add(new ChatTurn("user", text, DateTimeOffset.Now));
            body = Request();
            turns.Add(answer);
        }
        changed();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Address, "/v1/chat/completions"))
            { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellation);
                lock (Gate) Fail(answer, $"HTTP {(int)response.StatusCode}: " + ServerMessage(detail));
                changed();
                return;
            }
            using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(cancellation) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                bool applied;
                lock (Gate) applied = Apply(answer, data, clock.Elapsed);
                if (applied) changed();
            }
            lock (Gate)
                if (answer.Phase is not (ChatPhase.Failed or ChatPhase.Done))
                {
                    answer.Phase = ChatPhase.Done;
                    answer.Finish ??= Messages.DisplayNoReasonReported.ToString();
                }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            lock (Gate)
            {
                answer.Phase = ChatPhase.Cancelled;
                answer.Problem = Messages.ChatClientClosed;
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException)
        {
            lock (Gate) Fail(answer, error.Message);
        }
        changed();
    }

    // Starts a new session: forgets the conversation; the server keeps its own logs.
    public void Clear()
    {
        if (Current is not null) throw new InvalidOperationException(Messages.ChatResponseRunning);
        lock (Gate) turns.Clear();
    }

    internal static bool Apply(ChatTurn answer, string data, TimeSpan elapsed)
    {
        if (JsonNode.Parse(data) is not JsonObject chunk) return false;
        if (chunk["error"] is { } error)
        {
            Fail(answer, error is JsonObject detail ? detail["message"]?.ToString() ?? error.ToJsonString() : error.ToString());
            return true;
        }
        if (ChatProgress.Parse(chunk["prompt_progress"]) is { } progress)
        {
            answer.Progress = progress;
            if (answer.Phase == ChatPhase.Waiting) answer.Phase = ChatPhase.Prompt;
        }
        if (ChatTimings.Parse(chunk["timings"]) is { } timings)
        {
            answer.Timings = timings;
            if (timings.Predicted > 0 && timings.PredictedRate is { } rate) answer.GenerationRates.Add(rate);
            else if (timings.PromptRate is { } prompt) answer.PromptRates.Add(prompt);
        }
        if (chunk["choices"] is JsonArray { Count: > 0 } choices && choices[0] is JsonObject choice)
        {
            var delta = choice["delta"] as JsonObject;
            var content = delta?["content"] is JsonValue c && c.TryGetValue<string>(out var s) ? s : null;
            var reasoning = delta?["reasoning_content"] is JsonValue r && r.TryGetValue<string>(out var t) ? t : null;
            if (!string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(reasoning))
            {
                answer.FirstToken ??= elapsed;
                answer.Phase = ChatPhase.Generating;
                if (content is not null) answer.Text.Append(content);
                if (reasoning is not null) answer.Reasoning.Append(reasoning);
            }
            if (choice["finish_reason"] is JsonValue f && f.TryGetValue<string>(out var finish))
            {
                answer.Finish = finish;
                answer.Phase = ChatPhase.Done;
            }
        }
        return true;
    }

    private static void Fail(ChatTurn answer, string message)
    {
        answer.Phase = ChatPhase.Failed;
        answer.Problem = message;
    }

    private static string ServerMessage(string body)
    {
        try { if (JsonNode.Parse(body)?["error"] is { } error) return error is JsonObject o ? o["message"]?.ToString() ?? body : error.ToString(); }
        catch (JsonException) { }
        return body.Length > 200 ? body[..200] + "…" : body;
    }

    // Short label of the finish reason reported by the server.
    public static string FinishLabel(string? finish) => finish switch
    {
        "stop" => Messages.ChatFinish,
        "length" => Messages.ChatFinishLength,
        null => "",
        _ => Messages.ChatFinishOther.Format(finish)
    };

    public static string Rate(double? value) => value is { } v ? v.ToString(v >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) : "—";
}
