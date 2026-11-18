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
    // Configuración › General and TUI (docs/design/tui E and F). Coquena edits the same values as
    // `coquena config` through ConfigurationManager; fields are applied only with Guardar. TUI
    // preferences are saved as soon as a box changes.
    internal sealed class SettingsForm
    {
        private const int Column = 23, OriginColumn = 38;
        private readonly IApplication app;
        private readonly ConfigurationManager manager;
        private readonly Home home;
        private readonly bool color;
        private readonly Field models, backends, initial, maximum, interval, retention;
        private readonly Label status, tuiStatus;
        private readonly View resourceRows;
        private readonly Button language;
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
            status = new Label { X = 1, Y = 16, Width = Dim.Fill(1), Height = 3 };

            Heading(Coquena, 0, Messages.SettingsLanguage);
            language = Button(Coquena, 2, 1, LanguageLabel(), ChooseLanguage);
            Heading(Coquena, 2, Messages.SettingsDirectories);
            models = Add(3, Messages.SettingsModels, 14, Dim.Fill(10), "", () => new ModelPaths(home).Root(null));
            backends = Add(4, Messages.SettingsBackends, 14, Dim.Fill(10), "", () => new BackendPaths(home).Root(null));
            Heading(Coquena, 5, Messages.SettingsObservation);
            initial = Add(6, Messages.SettingsRetryInitial, Column, 6, "s", () => new ObserverPreferences().RetryInitialSeconds.ToString(CultureInfo.InvariantCulture));
            maximum = Add(7, Messages.SettingsRetryMaximum, Column, 6, "s", () => new ObserverPreferences().RetryMaxSeconds.ToString(CultureInfo.InvariantCulture));
            interval = Add(8, Messages.SettingsInterval, Column, 6, "s", () => ObserverInterval.Format(ObserverInterval.Default.TotalSeconds));
            Heading(Coquena, 9, Messages.SettingsLogs);
            retention = Add(10, Messages.SettingsRetention, Column, 9, "", () => ByteSize.Format(LogRetentionPolicy.InitialBytes, CultureInfo.InvariantCulture));
            Heading(Coquena, 11, Messages.SettingsParameters);
            Text(Coquena, 2, 12, Messages.SettingsGlobalCatalog, Palette.Text);
            var simulate = Button(Coquena, Column, 12, Messages.SettingsSimulateReset, SimulateReset);
            Button(Coquena, Pos.Right(simulate) + 2, 12, Messages.SettingsReset, ResetCatalog);
            var save = Button(Coquena, 1, 14, Messages.SettingsSave, Save);
            var defaults = Button(Coquena, Pos.Right(save) + 2, 14, Messages.SettingsReset, Defaults);
            Button(Coquena, Pos.Right(defaults) + 2, 14, Messages.SettingsReload, () => Load());
            Coquena.Add(status);

            Heading(Tui, 0, Messages.SettingsLog);
            FilterLogs = new CheckBox { X = 2, Y = 1, Text = Messages.SettingsHideMonitoring };
            Heading(Tui, 3, Messages.SettingsBottomBar);
            resourceRows = new View { X = 2, Y = 4, Width = Dim.Fill(), Height = 6 };
            tuiStatus = new Label { X = 1, Y = 11, Width = Dim.Fill(1), Height = 2 };
            Tui.Add(FilterLogs, resourceRows, tuiStatus);
            FilterLogs.ValueChanging += (_, change) =>
            {
                try { manager.SetFilterMonitoringLogs(change.NewValue == CheckState.Checked); Tell(tuiStatus, "", false); }
                catch (Exception error) when (ServiceObservation.ObservationError(error))
                {
                    change.Handled = true;
                    Tell(tuiStatus, Messages.SettingsPreferenceNotSaved.Format(error.Message), true);
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
                Set(models, view.EffectivePath, view.Own);
                Set(backends, backendView.EffectivePath, backendView.Own);
                Set(initial, retry.RetryInitialSeconds.ToString(CultureInfo.InvariantCulture), retry.OwnRetry);
                Set(maximum, retry.RetryMaxSeconds.ToString(CultureInfo.InvariantCulture), retry.OwnRetry);
                Set(interval, ObserverInterval.Format(retry.Interval(ObserverInterval.Default).TotalSeconds), retry.IntervalSeconds is not null);
                ObservationTimeline.Interval = retry.Interval(ObserverInterval.Default);
                Set(retention, ByteSize.Format(retentionView.MaxBytes, CultureInfo.InvariantCulture), retentionView.Origin != "initial");
                FilterLogs.Value = manager.FilterMonitoringLogs() ? CheckState.Checked : CheckState.UnChecked;
                Hidden = manager.HiddenBarResources().ToHashSet(StringComparer.Ordinal);
                foreach (var box in resourceBoxes) box.Value = Hidden.Contains((string)box.Data!) ? CheckState.UnChecked : CheckState.Checked;
                if (!quiet) Tell(status, Messages.SettingsReloaded.Format(home.PathFor("config.json")), false);
            }
            catch (Exception error) when (Expected(error)) { Tell(status, Messages.SettingsNotRead.Format(error.Message), true); }
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
                        Tell(tuiStatus, Messages.SettingsPreferenceNotSaved.Format(error.Message), true);
                    }
                };
                resourceBoxes.Add(box);
                resourceRows.Add(box);
            }
            if (gpus == 0) resourceRows.Add(new Label { Y = 1, Text = Messages.SettingsGpuNoData });
        }

        // Configured language, as `config language show`: auto shows the language it resolves to.
        private string LanguageLabel()
        {
            string? setting = null;
            try { setting = manager.Language(); }
            catch (Exception error) when (Expected(error)) { }
            return " " + LanguageName(setting) + " ";
        }

        private string LanguageName(string? code) => code is null
            ? Messages.SettingsLanguageAuto.Format(Localization.Resolve(null, CultureInfo.CurrentUICulture, Localization.Available(home)))
            : code;

        // Saved at once, like the other TUI preferences; the texts change when the TUI restarts.
        private void ChooseLanguage()
        {
            var codes = new List<string?> { null };
            codes.AddRange(Localization.Available(home).Select(l => l.Code));
            string? setting = null;
            try { setting = manager.Language(); }
            catch (Exception error) when (Expected(error)) { }
            var current = codes.IndexOf(setting);
            if (DropDown(app, codes.Select(LanguageName).ToArray(), Math.Max(0, current), current, language) is not { } chosen || chosen == current) return;
            try
            {
                manager.SetLanguage(codes[chosen]);
                language.Text = LanguageLabel();
                Tell(status, Messages.SettingsLanguageSaved.Format(LanguageName(codes[chosen])), false);
            }
            catch (Exception error) when (Expected(error)) { Tell(status, Messages.SettingsPreferenceNotSaved.Format(error.Message), true); }
        }

        private void Defaults()
        {
            foreach (var field in Fields()) field.Text.Text = field.Default();
            Tell(status, Messages.SettingsDefaultsLoaded, false);
        }

        // Validates every field first; nothing is written when one is invalid. Directory moves
        // run in the background and ask, as `config models-dir/backends-dir set` does.
        private void Save()
        {
            if (directoryChange is { IsCompleted: false }) { Tell(status, Messages.SettingsMoveRunning, true); return; }
            int first, last;
            long bytes;
            double pace;
            try
            {
                first = Integer(initial, 1, 3600);
                last = Integer(maximum, first, 3600);
                try { pace = ObserverInterval.Parse(interval.Text.Text); }
                catch (ArgumentException error) { throw new ConfigurationException(interval.Name + ": " + error.Message); }
                bytes = LogRetentionPolicy.Parse(retention.Text.Text.Trim());
            }
            catch (ConfigurationException error) { Tell(status, error.Message, true); return; }
            var done = new List<string>();
            try
            {
                if (Edited(initial) || Edited(maximum) || Edited(interval))
                {
                    var own = manager.Observer().IntervalSeconds;
                    var seconds = !Edited(interval) ? own : pace == ObserverInterval.Default.TotalSeconds ? null : pace;
                    manager.SetObserver(new ObserverPreferences(first, last, seconds).OrNull());
                    if (Edited(initial) || Edited(maximum)) done.Add(Messages.SettingsReconnection);
                    if (Edited(interval)) done.Add(Messages.SettingsIntervalDone);
                }
                if (Edited(retention)) { manager.SetRetention(bytes == LogRetentionPolicy.InitialBytes ? null : bytes); done.Add(Messages.SettingsRetentionDone); }
            }
            catch (Exception error) when (Expected(error)) { Tell(status, Messages.SettingsNotSaved.Format(error.Message), true); return; }
            var moves = new[] { models, backends }.Where(Edited).Select(field => (field, location: Location(field))).ToArray();
            Load(quiet: true);
            foreach (var (field, location) in moves) field.Text.Text = location ?? field.Default();
            Tell(status, done.Count == 0 && moves.Length == 0 ? Messages.SettingsNoChanges : done.Count == 0 ? "" : Messages.SettingsSaved.Format(string.Join(", ", done)), false);
            if (moves.Length == 0) return;
            directoryChange = Task.Run(() =>
            {
                foreach (var (field, location) in moves)
                {
                    var output = new StringWriter();
                    string message;
                    var failed = false;
                    try
                    {
                        var directory = field == models ? (Func<string?, TextWriter, Func<string?>, Func<bool>, bool>)manager.ModelDirectory.Change
                            : manager.BackendDirectory.Change;
                        directory(location, output, () => Ask(Messages.SettingsDirectoryOf.Format(field.Name.ToLowerInvariant()), output.ToString(),
                                [Messages.SettingsCancel, Messages.SettingsKeepAtOrigin, Messages.SettingsMove]) switch { 1 => Messages.NoAnswer, 2 => Messages.YesAnswer, _ => "" },
                            () => Ask(Messages.SettingsOverwriteFiles, output.ToString(), [Messages.SettingsCancel, Messages.SettingsOverwrite]) == 1);
                        message = output.ToString().Trim().Split('\n')[^1].Trim();
                    }
                    catch (Exception error) when (Expected(error)) { message = Messages.SettingsDirectoryNotChanged.Format(field.Name.ToLowerInvariant(), error.Message); failed = true; }
                    if (!closing.IsCancellationRequested)
                        app.Invoke(() => { Load(quiet: true); Tell(status, Messages.SettingsFieldMessage.Format(field.Name, message), failed); });
                }
            });
        }

        private void SimulateReset()
        {
            var output = new StringWriter();
            try { manager.Parameters.Reset(true, output); }
            catch (Exception error) when (Expected(error)) { output.WriteLine(error.Message); }
            Choose(app, Messages.SettingsSimulateTitle, output.ToString().TrimEnd().Split('\n'), null);
        }

        private void ResetCatalog()
        {
            var output = new StringWriter();
            try
            {
                var applied = manager.Parameters.Reset(false, output,
                    () => Query(app, Messages.SettingsResetTitle, output.ToString(), Messages.SettingsCancel, Messages.SettingsResetButton) == 1);
                Tell(status, applied ? Messages.SettingsCatalogReset : Messages.SettingsCatalogKept, false);
            }
            catch (Exception error) when (Expected(error)) { Tell(status, Messages.SettingsCatalogNotReset.Format(error.Message), true); }
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
                ? value : throw new ConfigurationException(Messages.SettingsInteger.Format(field.Name, minimum, maximum));

        private IEnumerable<Field> Fields() => [models, backends, initial, maximum, interval, retention];

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
            field.Origin.Text = edited ? Messages.SettingsEdited : field.Own ? Messages.SettingsOwn : Messages.SettingsDefault;
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
