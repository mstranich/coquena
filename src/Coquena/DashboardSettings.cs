using System.Globalization;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Storage;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Coquena;

internal static partial class DashboardCli
{
    // Configuración › Coquena and TUI (docs/design/tui E and F). Coquena edits the same values as
    // `coquena config` through ConfigurationManager; fields are applied only with Guardar. TUI
    // preferences are saved as soon as a box changes.
    internal sealed class SettingsForm
    {
        private const int Column = 23, OriginColumn = 38;
        private readonly IApplication app;
        private readonly ConfigurationManager manager;
        private readonly Home home;
        private readonly bool color;
        private readonly Field models, backends, initial, maximum, history, retention;
        private readonly Label status, tuiStatus;
        private readonly View resourceRows;
        private readonly List<CheckBox> resourceBoxes = [];
        private int shownGpus = -1;
        private Task? directoryChange;
        private readonly CancellationToken closing;

        // The directory move still running, if any; the dashboard waits for it on exit.
        public Task Pending => directoryChange ?? Task.CompletedTask;

        public View Coquena { get; }
        public View Tui { get; }
        public CheckBox FilterLogs { get; }
        public HashSet<string> Hidden { get; private set; }
        public int HistoryLimit { get; private set; }
        public event Action? Changed;

        private sealed record Field(string Name, TextField Text, Label Origin, Func<string> Default)
        {
            public string Loaded { get; set; } = "";
            public bool Own { get; set; }
        }

        public SettingsForm(IApplication app, ConfigurationManager manager, Home home, bool color, CancellationToken closing = default)
        {
            (this.app, this.manager, this.home, this.color, this.closing) = (app, manager, home, color, closing);
            Coquena = new View { Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
            Tui = new View { Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
            status = new Label { X = 1, Y = 14, Width = Dim.Fill(1), Height = 3 };

            Heading(Coquena, 0, "DIRECTORIOS");
            models = Add(1, "Modelos", 14, Dim.Fill(10), "", () => new ModelPaths(home).Root(null));
            backends = Add(2, "Backends", 14, Dim.Fill(10), "", () => new BackendPaths(home).Root(null));
            Heading(Coquena, 3, "OBSERVACIÓN");
            initial = Add(4, "Reconexión inicial", Column, 6, "s", () => new ObserverPreferences().RetryInitialSeconds.ToString(CultureInfo.InvariantCulture));
            maximum = Add(5, "Reconexión máxima", Column, 6, "s", () => new ObserverPreferences().RetryMaxSeconds.ToString(CultureInfo.InvariantCulture));
            history = Add(6, "Histórico de tareas", Column, 6, "", () => "100");
            Heading(Coquena, 7, "LOGS");
            retention = Add(8, "Retención", Column, 9, "", () => ByteSize.Format(LogRetentionPolicy.InitialBytes, CultureInfo.InvariantCulture));
            Heading(Coquena, 9, "PARÁMETROS");
            Text(Coquena, 2, 10, "Catálogo global", Palette.Text);
            var simulate = Button(Coquena, Column, 10, " Simular reset ", SimulateReset);
            Button(Coquena, Pos.Right(simulate) + 2, 10, " Restablecer ", ResetCatalog);
            var save = Button(Coquena, 1, 12, " Guardar ", Save);
            var defaults = Button(Coquena, Pos.Right(save) + 2, 12, " Restablecer ", Defaults);
            Button(Coquena, Pos.Right(defaults) + 2, 12, " Recargar ", () => Load());
            Coquena.Add(status);

            Heading(Tui, 0, "LOG");
            FilterLogs = new CheckBox { X = 2, Y = 1, Text = "Ocultar las peticiones de monitoreo de Coquena" };
            Heading(Tui, 3, "BARRA INFERIOR");
            resourceRows = new View { X = 2, Y = 4, Width = Dim.Fill(), Height = 6 };
            tuiStatus = new Label { X = 1, Y = 11, Width = Dim.Fill(1), Height = 2 };
            Tui.Add(FilterLogs, resourceRows, tuiStatus);
            FilterLogs.ValueChanging += (_, change) =>
            {
                try { manager.SetFilterMonitoringLogs(change.NewValue == CheckState.Checked); Tell(tuiStatus, "", false); }
                catch (Exception error) when (ServiceObservation.ObservationError(error))
                {
                    change.Handled = true;
                    Tell(tuiStatus, "No se pudo guardar la preferencia: " + error.Message, true);
                }
            };
            if (color)
            {
                foreach (var view in new View[] { Coquena, Tui, resourceRows }) Paint(view, Palette.Text, Palette.Base);
                Paint(FilterLogs, Palette.Text, Palette.Base);
            }
            Hidden = [];
            Load(quiet: true);
        }

        // Reads every value again and discards pending edits.
        public void Load(bool quiet = false)
        {
            try
            {
                var retry = manager.Observer();
                var view = manager.ModelDirectory.Show();
                var backendView = manager.BackendDirectory.Show();
                var retentionView = manager.Retention();
                HistoryLimit = manager.InferenceHistory();
                Set(models, view.EffectivePath, view.Own);
                Set(backends, backendView.EffectivePath, backendView.Own);
                Set(initial, retry.RetryInitialSeconds.ToString(CultureInfo.InvariantCulture), retry != new ObserverPreferences());
                Set(maximum, retry.RetryMaxSeconds.ToString(CultureInfo.InvariantCulture), retry != new ObserverPreferences());
                Set(history, HistoryLimit.ToString(CultureInfo.InvariantCulture), HistoryLimit != 100);
                Set(retention, ByteSize.Format(retentionView.MaxBytes, CultureInfo.InvariantCulture), retentionView.Origin != "inicial");
                FilterLogs.Value = manager.FilterMonitoringLogs() ? CheckState.Checked : CheckState.UnChecked;
                Hidden = manager.HiddenBarResources().ToHashSet(StringComparer.Ordinal);
                foreach (var box in resourceBoxes) box.Value = Hidden.Contains((string)box.Data!) ? CheckState.UnChecked : CheckState.Checked;
                if (!quiet) Tell(status, "Valores recargados de " + home.PathFor("config.json") + ".", false);
            }
            catch (Exception error) when (Expected(error)) { Tell(status, "No se pudo leer la configuración: " + error.Message, true); }
            Changed?.Invoke();
        }

        // One row of boxes per GPU of the bar; rebuilt only when the number of GPUs changes.
        public void ShowResources(int gpus)
        {
            if (gpus == shownGpus) return;
            shownGpus = gpus;
            resourceRows.RemoveAll();
            resourceBoxes.Clear();
            var keys = SettingsPresentation.BarResources(gpus);
            for (var i = 0; i < keys.Length; i++)
            {
                var (row, column) = i < 2 ? (0, i) : (1 + (i - 2) / 3, (i - 2) % 3);
                var key = keys[i];
                var box = new CheckBox
                {
                    X = column * (i < 2 ? 11 : 18), Y = row, Text = SettingsPresentation.BarLabel(key), Data = key,
                    Value = Hidden.Contains(key) ? CheckState.UnChecked : CheckState.Checked
                };
                if (color) Paint(box, Palette.Text, Palette.Base);
                box.ValueChanging += (_, change) =>
                {
                    var next = new HashSet<string>(Hidden, StringComparer.Ordinal);
                    if (change.NewValue == CheckState.Checked) next.Remove(key); else next.Add(key);
                    try
                    {
                        manager.SetHiddenBarResources(next);
                        Hidden = next;
                        Tell(tuiStatus, "", false);
                        Changed?.Invoke();
                    }
                    catch (Exception error) when (Expected(error))
                    {
                        change.Handled = true;
                        Tell(tuiStatus, "No se pudo guardar la preferencia: " + error.Message, true);
                    }
                };
                resourceBoxes.Add(box);
                resourceRows.Add(box);
            }
            if (gpus == 0) resourceRows.Add(new Label { Y = 1, Text = "GPU · sin datos" });
        }

        private void Defaults()
        {
            foreach (var field in Fields()) field.Text.Text = field.Default();
            Tell(status, "Valores predeterminados cargados; Guardar los aplica.", false);
        }

        // Validates every field first; nothing is written when one is invalid. Directory moves
        // run in the background and ask, as `config models-dir/backends-dir set` does.
        private void Save()
        {
            if (directoryChange is { IsCompleted: false }) { Tell(status, "Hay un traslado de directorios en curso.", true); return; }
            int first, last, limit;
            long bytes;
            try
            {
                first = Integer(initial, 1, 3600);
                last = Integer(maximum, first, 3600);
                limit = Integer(history, 0, 10000);
                bytes = LogRetentionPolicy.Parse(retention.Text.Text.Trim());
            }
            catch (ConfigurationException error) { Tell(status, error.Message, true); return; }
            var done = new List<string>();
            try
            {
                if (Edited(initial) || Edited(maximum))
                {
                    var preferences = new ObserverPreferences(first, last);
                    manager.SetObserver(preferences == new ObserverPreferences() ? null : preferences);
                    done.Add("reconexión");
                }
                if (Edited(history)) { manager.SetInferenceHistory(limit == 100 ? null : limit); HistoryLimit = limit; done.Add("histórico"); }
                if (Edited(retention)) { manager.SetRetention(bytes == LogRetentionPolicy.InitialBytes ? null : bytes); done.Add("retención"); }
            }
            catch (Exception error) when (Expected(error)) { Tell(status, "No se guardó: " + error.Message, true); return; }
            var moves = new[] { models, backends }.Where(Edited).Select(field => (field, location: Location(field))).ToArray();
            Load(quiet: true);
            foreach (var (field, location) in moves) field.Text.Text = location ?? field.Default();
            Tell(status, done.Count == 0 && moves.Length == 0 ? "Sin cambios." : done.Count == 0 ? "" : "Guardado: " + string.Join(", ", done) + ".", false);
            if (moves.Length == 0) return;
            directoryChange = Task.Run(() =>
            {
                foreach (var (field, location) in moves)
                {
                    var output = new StringWriter();
                    string message;
                    try
                    {
                        var directory = field == models ? (Func<string?, TextWriter, Func<string?>, Func<bool>, bool>)manager.ModelDirectory.Change
                            : manager.BackendDirectory.Change;
                        directory(location, output, () => Ask("Directorio de " + field.Name.ToLowerInvariant(), output.ToString(),
                                ["Cancelar", "Conservar en origen", "Trasladar"]) switch { 1 => "n", 2 => "s", _ => "c" },
                            () => Ask("Sobrescribir archivos", output.ToString(), ["Cancelar", "Sobrescribir"]) == 1);
                        message = output.ToString().Trim().Split('\n')[^1].Trim();
                    }
                    catch (Exception error) when (Expected(error)) { message = "No se cambió el directorio de " + field.Name.ToLowerInvariant() + ": " + error.Message; }
                    if (!closing.IsCancellationRequested)
                        app.Invoke(() => { Load(quiet: true); Tell(status, field.Name + ": " + message, message.StartsWith("No se", StringComparison.Ordinal)); });
                }
            });
        }

        private void SimulateReset()
        {
            var output = new StringWriter();
            try { manager.Parameters.Reset(true, output); }
            catch (Exception error) when (Expected(error)) { output.WriteLine(error.Message); }
            Choose(app, "Simular reset · catálogo global", output.ToString().TrimEnd().Split('\n'), null);
        }

        private void ResetCatalog()
        {
            var output = new StringWriter();
            try
            {
                var applied = manager.Parameters.Reset(false, output,
                    () => Query(app, "Restablecer catálogo global", output.ToString(), "Cancelar", "Restablecer") == 1);
                Tell(status, applied ? "Catálogo global restablecido." : "Cancelado; catálogo conservado.", false);
            }
            catch (Exception error) when (Expected(error)) { Tell(status, "No se restableció el catálogo: " + error.Message, true); }
        }

        private int Ask(string title, string text, string[] actions)
        {
            // Index 0 is always Cancelar. Once the dashboard closes nobody can answer, so the
            // question resolves as Cancelar instead of blocking the move thread forever.
            return AnswerOrCancel(work => app.Invoke(work), () => Query(app, title, text, actions), closing);
        }

        // Asks on the UI thread from a background thread. Returns 0 (Cancelar) when the
        // dashboard is closing, including when the queued question will never run.
        internal static int AnswerOrCancel(Action<Action> invoke, Func<int> query, CancellationToken closing)
        {
            if (closing.IsCancellationRequested) return 0;
            var answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            invoke(() =>
            {
                if (closing.IsCancellationRequested) { answer.TrySetResult(0); return; }
                try { answer.TrySetResult(query()); }
                catch (Exception error) { answer.TrySetException(error); }
            });
            try { return answer.Task.WaitAsync(closing).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (closing.IsCancellationRequested) { return 0; }
        }

        // A path equal to the default location is stored as the default.
        private string? Location(Field field)
        {
            var text = field.Text.Text.Trim();
            return text.Length == 0 || Path.GetFullPath(text, home.Root).TrimEnd('\\', '/')
                .Equals(field.Default().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ? null : text;
        }

        private static bool Edited(Field field) => field.Text.Text.Trim() != field.Loaded;

        private static int Integer(Field field, int minimum, int maximum) =>
            int.TryParse(field.Text.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum
                ? value : throw new ConfigurationException($"{field.Name}: entero entre {minimum} y {maximum}.");

        private IEnumerable<Field> Fields() => [models, backends, initial, maximum, history, retention];

        private void Set(Field field, string value, bool own)
        {
            field.Loaded = value;
            field.Own = own;
            field.Text.Text = value;
            Mark(field);
        }

        private void Mark(Field field)
        {
            var edited = field.Text.Text.Trim() != field.Loaded;
            field.Origin.Text = edited ? "editado" : field.Own ? "propio" : "predet.";
            if (color) Paint(field.Origin, edited ? Palette.Warning : Palette.Muted, Palette.Base);
        }

        private Field Add(int row, string name, int x, Dim width, string unit, Func<string> fallback)
        {
            Text(Coquena, 2, row, name, Palette.Text);
            var text = new TextField { X = x, Y = row, Width = width };
            var origin = new Label { X = width is DimFill ? Pos.AnchorEnd(9) : OriginColumn, Y = row, Width = 8 };
            Coquena.Add(text, origin);
            if (unit.Length > 0) Text(Coquena, x + 7, row, unit, Palette.Text);
            if (color) Paint(text, Palette.Accent, Palette.Card);
            var field = new Field(name, text, origin, fallback);
            text.TextChanged += (_, _) => Mark(field);
            return field;
        }

        private Field Add(int row, string name, int x, int width, string unit, Func<string> fallback) => Add(row, name, x, Dim.Absolute(width), unit, fallback);

        private void Heading(View parent, int row, string text) => Text(parent, 1, row, text, Palette.Muted);

        private void Text(View parent, int x, int row, string text, Color foreground)
        {
            var label = new Label { X = x, Y = row, Text = text };
            if (color) Paint(label, foreground, Palette.Base);
            parent.Add(label);
        }

        private Button Button(View parent, Pos x, int row, string text, Action pressed)
        {
            var button = new Button { X = x, Y = row, Text = text, NoDecorations = true, ShadowStyle = ShadowStyles.None };
            if (color) Paint(button, Palette.Text, Palette.Border);
            button.Accepted += (_, _) => pressed();
            parent.Add(button);
            return button;
        }

        private void Tell(Label label, string text, bool problem)
        {
            label.Text = text;
            if (color) Paint(label, problem ? Palette.Danger : Palette.Muted, Palette.Base);
        }

        private static bool Expected(Exception error) =>
            ServiceObservation.ObservationError(error) || error is ConfigurationException or StorageException or ContractException;
    }
}
