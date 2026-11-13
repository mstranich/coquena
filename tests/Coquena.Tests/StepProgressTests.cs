namespace Coquena.Tests;

internal static class StepProgressTests
{
    public static void Run(Action<bool, string> check)
    {
        // Redirected: plain lines, no control characters; details only for the failed step.
        var plain = new StringWriter();
        using (var progress = new StepProgress(plain, interactive: false, verbose: false))
        {
            var download = progress.Begin("Descargar modelo");
            var backend = progress.Begin("Preparar backend");
            download.Log("chunk 1/3"); backend.Log("HTTP 503 desde el origen");
            download.Done("2,1 GiB");
            backend.Fail("no se pudo descargar");
        }
        var text = plain.ToString();
        check(!text.Contains('\u001b') && text.Contains("[ok] Descargar modelo — 2,1 GiB") && text.Contains("[error] Preparar backend"),
            "redirected progress prints plain final lines");
        check(text.Contains("  HTTP 503 desde el origen") && !text.Contains("chunk 1/3"), "failed step shows its detail; successful step hides it");

        // Verbose keeps every detail line.
        var verbose = new StringWriter();
        using (var progress = new StepProgress(verbose, interactive: false, verbose: true))
        {
            var step = progress.Begin("Validar receta"); step.Log("capa coquena.yaml"); step.Done();
        }
        check(verbose.ToString().Contains("capa coquena.yaml"), "verbose keeps the full output");

        // Interactive: parallel steps redrawn as a block, fixed with a glyph when they end.
        var terminal = new StringWriter();
        using (var progress = new StepProgress(terminal, interactive: true, verbose: false, Glyphs.Unicode))
        {
            var one = progress.Begin("Uno"); var two = progress.Begin("Dos");
            two.Done(); one.Fail();
        }
        var screen = terminal.ToString();
        check(screen.Contains("\u001b[2F") && screen.Contains("✔ Dos") && screen.Contains("✖ Uno") && screen.IndexOf("✔ Dos", StringComparison.Ordinal) < screen.IndexOf("✖ Uno", StringComparison.Ordinal),
            "interactive progress redraws the running block and fixes each step");

        // An interrupted step is closed as failed.
        var interrupted = new StringWriter();
        using (var progress = new StepProgress(interrupted, interactive: false, verbose: false)) progress.Begin("Iniciar servicio");
        check(interrupted.ToString().Contains("[error] Iniciar servicio"), "open step is closed on dispose");
        // Silent: the TUI reads the same rows and formats them like the CLI.
        using (var silent = StepProgress.Silent())
        {
            var done = silent.Begin("Validar la receta"); done.Done();
            silent.Begin("Preparar backend y modelos");
            var rows = silent.Rows();
            check(rows.Count == 2 && rows[0].State == StepState.Done && rows[1].State == StepState.Running &&
                StepProgress.Format(rows[0], Glyphs.Unicode) == "✔ Validar la receta" &&
                StepProgress.Format(rows[1], Glyphs.Unicode, 0) == "⠋ Preparar backend y modelos", "silent progress exposes rows for the TUI");
        }

        // Hold pauses the redraw for a question; Print writes above the running block.
        var held = new StringWriter();
        using (var progress = new StepProgress(held, interactive: true, verbose: false, Glyphs.Unicode))
        {
            var step = progress.Begin("Iniciar el servicio");
            var before = held.ToString().Length;
            string during;
            // Read while the question is still held: releasing it redraws the running block.
            using (progress.Hold()) { progress.Print("¿Iniciar servicio? [s] Sí, [c] Cancelar:"); Thread.Sleep(3 * Spinner.PeriodMilliseconds); during = held.ToString()[before..]; }
            check(during.Contains("¿Iniciar servicio?") && !during[during.IndexOf('¿')..].Any(c => c is >= '⠀' and <= '⣿'), "held progress does not redraw over a question");
            step.Done();
        }
        check(held.ToString().Contains("✔ Iniciar el servicio"), "progress resumes after a question");
        check(Spinner.Frame(0) == "⠋" && Spinner.Frame(120) == "⠙", "spinner frames advance every 120 ms");
    }
}
