using Coquena.Chat;

namespace Coquena.Tests;

internal static class ChatOnceTests
{
    public static void Run(Action<bool, string> check)
    {
        ChatTurn Turn()
        {
            var turn = new ChatTurn("assistant", "", DateTimeOffset.Now);
            turn.Reasoning.Append("think");
            return turn;
        }
        var plain = new StringWriter();
        var hidden = new ChatOnceCli.Printer(plain, reasoning: false);
        var turn = Turn();
        hidden.Write(turn);
        turn.Text.Append("Hel"); hidden.Write(turn);
        turn.Text.Append("lo"); hidden.Write(turn);
        hidden.End();
        check(plain.ToString() == "Hello" + Environment.NewLine, "stdout carries only the answer, streamed in order: " + plain);

        var shown = new StringWriter();
        var visible = new ChatOnceCli.Printer(shown, reasoning: true);
        turn = Turn();
        visible.Write(turn);
        turn.Text.Append("Hi"); visible.Write(turn);
        check(shown.ToString() == Messages.DisplayReasoningTag + "\nthink\n\nHi", "--reasoning writes the tagged reasoning before the answer: " + shown);

        var none = new StringWriter();
        var json = new ChatOnceCli.Printer(none, reasoning: null);
        json.Write(turn); json.End();
        check(none.ToString().Length == 0, "--json does not stream text");

        turn.Finish = "stop";
        turn.Timings = new ChatTimings(10, 90, 100, 900, 20, 500, 40, 8, 6);
        turn.FirstToken = TimeSpan.FromSeconds(1.5);
        var node = ChatOnceCli.Json("default-service", "code/chat", turn);
        check(node["response"]!.GetValue<string>() == "Hi" && node["reasoning"]!.GetValue<string>() == "think" &&
            node["finish"]!.GetValue<string>() == "stop" && node["metrics"]!["generated_tokens"]!.GetValue<int>() == 20 &&
            node["metrics"]!["accepted_tokens"]!.GetValue<int>() == 6 && node["preset"]!.GetValue<string>() == "code/chat",
            "--json carries answer, reasoning, finish and metrics: " + node.ToJsonString());
        var metrics = string.Join(" · ", ChatOnceCli.Metrics(turn));
        check(metrics.Contains("gen 40,0 tk/s · 20 tk") || metrics.Contains("gen 40.0 tk/s · 20 tk"), "metrics use the telemetry names: " + metrics);
        check(ChatOnceCli.Metrics(new ChatTurn("assistant", "", DateTimeOffset.Now)).Single() == Messages.ChatNoTelemetry, "without timings the metrics say so");
    }
}
