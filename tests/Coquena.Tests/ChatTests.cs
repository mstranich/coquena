using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Coquena.Chat;
using Coquena.Observation;

namespace Coquena.Tests;

// Chat against llama-server's streamed chat format: request body, text, timings and progress,
// cancellation and errors, and the rows of CH · Herramientas › Chat at 80×24.
internal static class ChatTests
{
    // Chunks shaped as llama-server sends them with timings_per_token and return_progress.
    private const string Stream = """
        data: {"choices":[{"finish_reason":null,"index":0,"delta":{"role":"assistant","content":null}}],"object":"chat.completion.chunk","prompt_progress":{"total":812,"cache":640,"processed":700,"time_ms":80.5}}

        data: {"choices":[{"finish_reason":null,"index":0,"delta":{"content":"Hola"}}],"object":"chat.completion.chunk","timings":{"cache_n":640,"prompt_n":172,"prompt_ms":254.2,"prompt_per_second":676.6,"predicted_n":1,"predicted_ms":47.1,"predicted_per_second":21.2}}

        data: {"choices":[{"finish_reason":null,"index":0,"delta":{"content":", mundo"}}],"object":"chat.completion.chunk","timings":{"cache_n":640,"prompt_n":172,"prompt_ms":254.2,"prompt_per_second":676.6,"predicted_n":2,"predicted_ms":94.0,"predicted_per_second":21.3,"draft_n":6,"draft_n_accepted":4}}

        data: {"choices":[{"finish_reason":"stop","index":0,"delta":{}}],"object":"chat.completion.chunk","timings":{"cache_n":640,"prompt_n":172,"prompt_ms":254.2,"prompt_per_second":676.6,"predicted_n":2,"predicted_ms":94.0,"predicted_per_second":21.3,"draft_n":6,"draft_n_accepted":4}}

        data: [DONE]

        """;

    public static async Task Run(Action<bool, string> check)
    {
        var handler = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(Stream, Encoding.UTF8, "text/event-stream") });
        using var client = new HttpClient(handler);
        var session = new ChatSession(client, new Uri("http://127.0.0.1:9931"), "código/asistente");
        var updates = 0;
        await session.SendAsync("Saludá", () => updates++, CancellationToken.None);
        var request = JsonNode.Parse(handler.Bodies[0])!;
        check(handler.Paths[0] == "/v1/chat/completions" && request["model"]!.GetValue<string>() == "código/asistente" &&
            request["stream"]!.GetValue<bool>() && request["timings_per_token"]!.GetValue<bool>() && request["return_progress"]!.GetValue<bool>(),
            "the request names the published entry and asks llama-server for per-token timings and prompt progress");
        check(request["temperature"] is null && request["max_tokens"] is null, "the preset keeps its own generation parameters");
        var answer = session.Turns[^1];
        check(answer.Text.ToString() == "Hola, mundo" && answer.Phase == ChatPhase.Done && answer.Finish == "stop" && updates >= 4,
            "streamed text is appended and the finish reason is kept");
        check(answer.Timings is { Cached: 640, Prompt: 172, Predicted: 2, Drafted: 6, Accepted: 4 } t && t.Context == 814,
            "timings of the request carry cache reuse, speculative drafts and context use");
        check(answer.Progress is { Total: 812, Cache: 640 } progress && Math.Abs(progress.Ratio - 60d / 172) < 1e-9,
            "prompt progress excludes reused tokens from its timed share");
        check(answer.GenerationRates.Count == 3 && answer.FirstToken is not null, "the generation curve has one rate per chunk with timings");

        await session.SendAsync("¿Y ahora?", () => { }, CancellationToken.None);
        var second = JsonNode.Parse(handler.Bodies[1])!["messages"]!.AsArray();
        check(second.Count == 3 && second[1]!["role"]!.GetValue<string>() == "assistant" && second[1]!["content"]!.GetValue<string>() == "Hola, mundo",
            "the session keeps the conversation as context while it is open");
        session.Clear();
        check(session.Turns.Count == 0, "a new session forgets the conversation");

        var failing = new Fake(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("""{"error":{"code":400,"message":"model not found","type":"invalid_request_error"}}""") });
        using var failingClient = new HttpClient(failing);
        var broken = new ChatSession(failingClient, new Uri("http://127.0.0.1:9931"), "x/y");
        await broken.SendAsync("hola", () => { }, CancellationToken.None);
        check(broken.Turns[^1] is { Phase: ChatPhase.Failed, Problem: "HTTP 400: model not found" }, "server errors are shown with their message");

        var waiting = new Fake(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); });
        using var waitingClient = new HttpClient(waiting);
        var slow = new ChatSession(waitingClient, new Uri("http://127.0.0.1:9931"), "x/y");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await slow.SendAsync("hola", () => { }, cancel.Token);
        check(slow.Turns[^1] is { Phase: ChatPhase.Cancelled } cancelled && cancelled.Problem!.Contains(Messages.ChatClientClosed.ToString(), StringComparison.Ordinal),
            "cancelling closes this request without claiming that the backend stopped");
        check(slow.Request()["messages"]!.AsArray().Count == 1, "an interrupted answer without text is not sent as context");

        // Rows of CH at 80×24: right column of 56 columns.
        var width = 80 - LivePresentation.TreeWidth - 1;
        var metrics = ChatPresentation.Metrics(answer);
        check(metrics.All(row => Markup.Width(row) <= width), "both metric rows fit the right column");
        check(Markup.Plain(metrics[0]).Contains(Messages.ChatDoneUpper.ToString()) && Markup.Plain(metrics[0]).Contains(Messages.ChatCache + " 79%") &&
            Markup.Plain(metrics[1]).Contains(Messages.ChatAccepted + " 67%") && Markup.Plain(metrics[1]).Contains("2 tk"),
            "metrics show phase, cache reuse of the request and speculative acceptance with its own counts");
        var empty = ChatPresentation.Metrics(null);
        check(Markup.Plain(empty[0]).Contains(Messages.ChatNoRequestUpper.ToString()) && Markup.Plain(empty[1]).Contains(Messages.ChatAccepted + " —"), "absent metrics show «—» instead of zeros");
        var history = ChatPresentation.History(session.Turns.Count == 0 ? broken.Turns : session.Turns, width);
        check(history.All(row => Markup.Width(row) <= width) && Markup.Plain(history[^1]).Contains("model not found"), "history rows wrap to the column and keep errors");
        check(Markup.Width(ChatPresentation.Selector("principal", "código/asistente", "gemma-4-12B Q4_0", null)) <= width &&
            Markup.Parse(ChatPresentation.Selector(null, null, null, null)).Any(span => span.Action == "target"),
            "the selector is a button with service › preset");
        var actions = Markup.Fit(ChatPresentation.Actions(true, true, true), width);
        check(Markup.Width(actions) == width && Markup.Plain(actions).Contains(Messages.ChatStop.ToString().Trim()) && !Markup.Parse(actions).Any(span => span.Action == "live"),
            "while answering, Enviar becomes Detener; there is no Ir a En vivo");
        var tabs = Markup.Fit(LivePresentation.Tabs(5, 1, 2, "⇣ 1 · 68%"), 80);
        check(Markup.Width(tabs) == 80 && Markup.Parse(tabs).Any(span => span.Class == "sel" && span.Action == "tab5" && span.Text == " " + Messages.LabelsTools + " "),
            "Herramientas is a tab and fits the top bar with both indicators");
    }

    private sealed class Fake(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string> Paths { get; } = [];

        public Fake(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request))) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return await respond(request, cancellationToken);
        }
    }
}
