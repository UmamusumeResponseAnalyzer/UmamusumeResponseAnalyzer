using System.Collections;
using System.Collections.Specialized;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;
using Attribute = Terminal.Gui.Drawing.Attribute;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;
using UiText = UmamusumeResponseAnalyzer.Localization.TerminalGui;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal enum BootstrapAction { Start, Options, PluginRepository, UpdateData, UpdateProgram, Hachimi, QqGroup }
internal enum BootstrapPhaseId { Config, Scan, Database, Initialize, Listener, StartCallbacks, Host }
internal enum BootstrapPhaseState { Pending, Running, Succeeded, Warning, Failed, Skipped }
internal enum BootstrapPluginState { Scanned, Active, Unavailable, StartFailed }
internal enum BootstrapMode { Preparing, Ready, Busy, Starting, Information }

internal sealed record BootstrapSettings(
    string Version, string WorkingDirectory, string ConfigFile, string ListenHost, int ListenPort,
    string DataLanguage, string TrainerGender, string UpdateSource);

internal sealed record BootstrapPhase(
    BootstrapPhaseId Id, BootstrapPhaseState State, string Detail, string? ExceptionDetails = null);

internal sealed record BootstrapPluginRow(
    string Id, string Name, string Version, BootstrapPluginState State, string Detail,
    string? ExceptionDetails = null, PluginFailureStage? FailureStage = null);

internal sealed record BootstrapLogRow(
    string Status, string Text, string? ExceptionDetails = null, UiSeverity? Severity = null);

internal sealed record BootstrapSnapshot(
    BootstrapSettings? Settings, IReadOnlyList<BootstrapPhase> Phases,
    IReadOnlyList<BootstrapPluginRow> Plugins, IReadOnlyList<BootstrapLogRow> Logs,
    BootstrapMode Mode, string? Listening, BootstrapAction? RunningAction = null);

internal sealed class BootstrapWorkspace : IDisposable
{
    const int MaxLogRows = 128;
    internal const string PanelKey = "status";
    readonly UiHost uiHost;
    readonly object gate = new();
    readonly Dictionary<BootstrapPhaseId, BootstrapPhase> phases = Enum.GetValues<BootstrapPhaseId>()
        .ToDictionary(id => id, id => new BootstrapPhase(id, BootstrapPhaseState.Pending, string.Empty));
    readonly List<BootstrapLogRow> logs = [];
    BootstrapSettings? settings;
    IReadOnlyList<BootstrapPluginRow> plugins = [];
    BootstrapMode mode;
    BootstrapAction? runningAction;
    string? listening;
    BootstrapView? view;
    TaskCompletionSource<BootstrapAction>? menuSelection;
    bool disposed;

    public BootstrapWorkspace(UiHost uiHost, Workspace workspace)
    {
        this.uiHost = uiHost;
        Workspace = workspace;
        uiHost.BindWorkspaceHotkey(Workspace, ConsoleKey.B, ConsoleModifiers.Control, I18N_StartupInformation);
        uiHost.LogAdded += OnLogAdded;
        uiHost.SetPanel(Workspace, PanelKey, I18N_StartupStatus, new WorkspaceContent(() =>
        {
            var created = new BootstrapView(Choose, Cancel, () =>
            {
                view = null;
                lock (gate)
                    menuSelection?.TrySetCanceled();
            });
            view = created;
            RefreshView();
            return created;
        }), fullBleed: true, switchToWorkspace: false);
    }

    public Workspace Workspace { get; }

    internal void ShowPreparing()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            mode = BootstrapMode.Preparing;
        }
        uiHost.RefreshBootstrap();
    }

    internal async Task<BootstrapAction> ShowMenuAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = new TaskCompletionSource<BootstrapAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (menuSelection is { Task.IsCompleted: false })
                throw new InvalidOperationException(I18N_MenuPending);
            menuSelection = selection;
            mode = BootstrapMode.Ready;
            runningAction = null;
        }
        uiHost.RefreshBootstrap();
        using var cancellation = cancellationToken.Register(() => selection.TrySetCanceled(cancellationToken));
        return await selection.Task;
    }

    void Choose(BootstrapAction action)
    {
        lock (gate)
        {
            if (mode != BootstrapMode.Ready || menuSelection?.TrySetResult(action) != true)
                return;
            runningAction = action;
            mode = action == BootstrapAction.Start ? BootstrapMode.Starting : BootstrapMode.Busy;
        }
        uiHost.RefreshBootstrap();
    }

    void Cancel()
    {
        lock (gate)
            menuSelection?.TrySetCanceled();
        uiHost.RequestShutdown();
    }

    internal void ShowInformation()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            menuSelection?.TrySetCanceled();
            menuSelection = null;
            mode = BootstrapMode.Information;
        }
        uiHost.RefreshBootstrap();
    }

    internal void SetSettings(BootstrapSettings value)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            settings = value;
        }
        uiHost.RefreshBootstrap();
    }

    internal void SetListening(string endpoint)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            listening = endpoint;
        }
        uiHost.RefreshBootstrap();
    }

    internal void SetPhase(BootstrapPhaseId id, BootstrapPhaseState state, string detail, string? errorDetails = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            phases[id] = new(id, state, detail, errorDetails);
        }
        uiHost.RefreshBootstrap();
    }

    internal void SetPluginSummary(IReadOnlyList<BootstrapPluginRow> values)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            plugins = values.ToArray();
        }
        uiHost.RefreshBootstrap();
    }

    public void Log(string source, string text, UiSeverity severity = UiSeverity.Info)
    {
        lock (gate)
            ObjectDisposedException.ThrowIf(disposed, this);
        uiHost.Log($"[{source}] {text}", severity);
    }

    void OnLogAdded(UiLogLine line)
    {
        lock (gate)
        {
            if (disposed)
                return;
            logs.Add(new(SeverityLabel(line.Severity), line.Text, line.ExceptionDetails, line.Severity));
            if (logs.Count > MaxLogRows)
                logs.RemoveRange(0, logs.Count - MaxLogRows);
        }
    }

    internal void RefreshView()
    {
        BootstrapSnapshot snapshot;
        lock (gate)
        {
            if (disposed || view is null)
                return;
            snapshot = new(settings, phases.Values.ToArray(), plugins, logs.ToArray(), mode, listening, runningAction);
        }
        view.Update(snapshot);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            menuSelection?.TrySetCanceled();
            menuSelection = null;
        }
        uiHost.LogAdded -= OnLogAdded;
    }

    internal static string SeverityLabel(UiSeverity severity) => severity switch
    {
        UiSeverity.Trace or UiSeverity.Info => UiText.Severity_Info,
        UiSeverity.Success => UiText.Severity_Success,
        UiSeverity.Warning => UiText.Severity_Warning,
        UiSeverity.Error => UiText.Severity_Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
    };

    internal static string ActionLabel(BootstrapAction action) => action switch
    {
        BootstrapAction.Start => I18N_Start,
        BootstrapAction.Options => I18N_Options,
        BootstrapAction.PluginRepository => I18N_PluginRepository,
        BootstrapAction.UpdateData => I18N_UpdateAssets,
        BootstrapAction.UpdateProgram => I18N_UpdateProgram,
        BootstrapAction.Hachimi => I18N_InstallUraCore,
        BootstrapAction.QqGroup => I18N_QqGroup,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    internal static string PhaseLabel(BootstrapPhaseId id) => id switch
    {
        BootstrapPhaseId.Config => I18N_PhaseConfig,
        BootstrapPhaseId.Scan => I18N_PhasePluginScan,
        BootstrapPhaseId.Database => I18N_PhaseDatabase,
        BootstrapPhaseId.Initialize => I18N_PhasePluginInit,
        BootstrapPhaseId.Listener => I18N_PhaseServer,
        BootstrapPhaseId.StartCallbacks => I18N_PhaseStartCallbacks,
        BootstrapPhaseId.Host => I18N_PhaseHost,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    internal static string StateLabel(BootstrapPhaseState state) => state switch
    {
        BootstrapPhaseState.Pending => I18N_Waiting,
        BootstrapPhaseState.Running => I18N_StateRunning,
        BootstrapPhaseState.Succeeded => UiText.Severity_Success,
        BootstrapPhaseState.Warning => UiText.Severity_Warning,
        BootstrapPhaseState.Failed => UiText.Severity_Error,
        BootstrapPhaseState.Skipped => I18N_StateSkipped,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}

internal sealed class BootstrapView : View
{
    static readonly Scheme ToolbarScheme = new(UraTheme.Raised)
    {
        Normal = new Attribute(UraTheme.Ink, UraTheme.Surface),
        Focus = new Attribute(UraTheme.OnSelection, UraTheme.Selection),
        HotFocus = new Attribute(UraTheme.OnSelection, UraTheme.Selection),
        Disabled = new Attribute(UraTheme.Muted, UraTheme.Surface)
    };
    static readonly Scheme StartScheme = new(ToolbarScheme)
    {
        Normal = new Attribute(UraTheme.Sunken, UraTheme.Accent),
        Focus = new Attribute(UraTheme.Sunken, UraTheme.Accent, TextStyle.Bold),
        HotFocus = new Attribute(UraTheme.Sunken, UraTheme.Accent, TextStyle.Bold),
        Highlight = new Attribute(UraTheme.Sunken, UraTheme.OnActiveTab)
    };

    readonly Action<BootstrapAction> choose;
    readonly Action onDisposed;
    readonly Label heading;
    readonly Line headerRule;
    readonly Line toolbarRule;
    readonly Line footerRule;
    readonly Label footerNavigation;
    readonly Label footerExit;
    readonly View toolbar;
    readonly Button[] actions;
    readonly Button more;
    readonly Label summary;
    readonly Label summaryDetail;
    readonly View body;
    readonly Button settingsToggle;
    readonly ListView settingsText;
    readonly Button detailsToggle;
    readonly View details;
    readonly ListView phasesText;
    readonly ListView pluginsText;
    readonly ListView logList;
    readonly Button diagnosticsToggle;
    readonly ListView diagnosticsText;
    readonly View errors;
    readonly ListView errorList;
    readonly Label errorHeading;
    readonly Label errorImpact;
    readonly Button showError;
    readonly Button copyError;
    readonly ListView errorDetails;
    readonly Label copyResult;
    readonly LogListSource logSource = new([]);
    readonly List<ErrorRow> errorRows = [];
    BootstrapSnapshot snapshot = new(null, [], [], [], BootstrapMode.Preparing, null);
    PopoverMenu? moreMenu;
    readonly List<(MenuItem Item, BootstrapAction Action)> moreActions = [];
    PopoverMenu? logContextMenu;
    string? exceptionDetailsToCopy;
    ErrorRow? displayedError;
    bool applyingSnapshot;
    bool focusingByMouse;
    int laidOutBodyHeight;
    bool settingsExpanded;
    bool detailsExpanded;
    bool diagnosticsExpanded;
    bool errorExpanded;
    bool initialized;
    bool userInteracted;
    bool diagnosticsVisible;
    Size laidOutSize;

    internal BootstrapView(Action<BootstrapAction> choose, Action requestShutdown, Action disposed)
    {
        this.choose = choose;
        onDisposed = disposed;
        Id = "bootstrap";
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        heading = new Label { Id = "bootstrap-heading", Text = "URA", X = 2, Y = 1, Height = 1 };
        heading.SetScheme(new Scheme(new Attribute(UraTheme.Text, UraTheme.Ground, TextStyle.Bold)));
        headerRule = new Line { Y = 2, Width = Dim.Fill(), LineAttribute = new Attribute(UraTheme.Rule, UraTheme.Ground) };
        toolbar = new View { Id = "bootstrap-toolbar", X = 2, Y = 3, Width = Dim.Fill(2), Height = 1, CanFocus = true };
        toolbarRule = new Line { Y = Pos.Bottom(toolbar), Width = Dim.Fill(), LineAttribute = new Attribute(UraTheme.Rule, UraTheme.Ground) };
        footerRule = new Line { Y = Pos.AnchorEnd(3), Width = Dim.Fill(), LineAttribute = new Attribute(UraTheme.Rule, UraTheme.Ground) };
        footerNavigation = new Label { Id = "bootstrap-footer-navigation", X = 2, Y = Pos.AnchorEnd(2), Text = I18N_FooterNavigation, Height = 1 };
        footerExit = new Label { Id = "bootstrap-footer-exit", Y = Pos.AnchorEnd(2), Text = I18N_FooterExit, Height = 1 };
        footerNavigation.SetScheme(new Scheme(new Attribute(UraTheme.Muted, UraTheme.Ground)));
        footerExit.SetScheme(new Scheme(new Attribute(UraTheme.Muted, UraTheme.Ground)));
        actions =
        [
            new() { Id = "bootstrap-start", Text = I18N_Start },
            new() { Id = "bootstrap-options", Text = I18N_Options },
            new() { Id = "bootstrap-plugins", Text = I18N_PluginRepository },
            new() { Id = "bootstrap-update-data", Text = I18N_UpdateAssets }
        ];
        var actionValues = new[] { BootstrapAction.Start, BootstrapAction.Options, BootstrapAction.PluginRepository, BootstrapAction.UpdateData };
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actionValues[index];
            actions[index].Accepting += (_, e) => { e.Handled = true; choose(action); };
        }
        more = new Button { Id = "bootstrap-more", Text = $"{I18N_More} ▾" };
        more.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (snapshot.Mode == BootstrapMode.Preparing && !userInteracted &&
                e.Context?.Binding is not MouseBinding)
            {
                summary!.SetFocus();
                return;
            }
            ShowMore();
        };
        foreach (var button in actions.Append(more))
        {
            button.NoDecorations = true;
            button.NoPadding = true;
            button.ShadowStyle = ShadowStyles.None;
            button.TextAlignment = Alignment.Center;
            button.VerticalTextAlignment = Alignment.Center;
            button.MouseHighlightStates = MouseState.In | MouseState.Pressed;
            button.SetScheme(button == actions[0] ? StartScheme : ToolbarScheme);
        }
        toolbar.Add([.. actions, more]);
        summary = new Label { CanFocus = true, TabStop = TabBehavior.TabStop, Id = "bootstrap-summary", Width = Dim.Fill(), Height = 1 };
        summary.TextFormatter.WordWrap = true;
        summaryDetail = new Label { Id = "bootstrap-summary-detail", Width = Dim.Fill(), Height = 0, Visible = false };
        summaryDetail.TextFormatter.WordWrap = true;
        summaryDetail.SetScheme(new Scheme(new Attribute(UraTheme.Muted, UraTheme.Ground)));
        copyResult = new Label { Id = "bootstrap-copy-result", X = 2, Width = Dim.Fill(2), Height = 0, Visible = false };
        copyResult.TextFormatter.WordWrap = true;
        body = new View
        {
            Id = "bootstrap-body", X = 2, Y = Pos.Bottom(toolbarRule) + 1, Width = Dim.Fill(2), Height = Dim.Fill(3),
            CanFocus = true, ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        settingsToggle = new Button { Id = "bootstrap-settings-toggle", Text = $"▸ {I18N_StartupSettings}", NoDecorations = true };
        settingsText = new ListView { Id = "bootstrap-settings", X = 1, Width = Dim.Fill(1), Visible = false, ShowMarks = false, KeystrokeNavigator = null, ViewportSettings = ViewportSettingsFlags.HasScrollBars };
        settingsToggle.Accepting += (_, e) =>
        {
            e.Handled = true;
            settingsExpanded = !settingsExpanded;
            LayoutBody();
        };
        detailsToggle = new Button { Id = "bootstrap-details-toggle", Text = $"▸ {I18N_StartupDetails}", NoDecorations = true };
        details = new View { Id = "bootstrap-details", X = 1, Width = Dim.Fill(1), Visible = false, CanFocus = true };
        phasesText = new ListView { Width = Dim.Fill(), Height = 1, ShowMarks = false, KeystrokeNavigator = null, ViewportSettings = ViewportSettingsFlags.HasScrollBars };
        pluginsText = new ListView { Width = Dim.Fill(), Height = 1, ShowMarks = false, KeystrokeNavigator = null, ViewportSettings = ViewportSettingsFlags.HasScrollBars };
        logList = new ListView
        {
            Id = "bootstrap-logs", Width = Dim.Fill(), Height = 9, ShowMarks = false, MarkMultiple = false,
            KeystrokeNavigator = null, Source = logSource, ViewportSettings = ViewportSettingsFlags.HasScrollBars
        };
        logList.CommandNotBound += LogCommandNotBound;
        logList.MouseBindings.ReplaceCommands(MouseFlags.RightButtonClicked, Command.Activate, Command.Context);
        logList.KeyBindings.Add(Key.F10.WithShift, Command.Context);
        details.Add(phasesText, pluginsText, logList);
        detailsToggle.Accepting += (_, e) =>
        {
            e.Handled = true;
            detailsExpanded = !detailsExpanded;
            LayoutBody();
        };
        diagnosticsToggle = new Button { Id = "bootstrap-diagnostics-toggle", Text = $"▸ {I18N_Diagnostics}", NoDecorations = true };
        diagnosticsText = new ListView { Id = "bootstrap-diagnostics", X = 1, Width = Dim.Fill(1), Visible = false, ShowMarks = false, KeystrokeNavigator = null, ViewportSettings = ViewportSettingsFlags.HasScrollBars };
        diagnosticsToggle.Accepting += (_, e) =>
        {
            e.Handled = true;
            diagnosticsExpanded = !diagnosticsExpanded;
            LayoutBody();
        };
        errors = new View { Id = "bootstrap-errors", Width = Dim.Fill(), Visible = false, CanFocus = true };
        errorList = new ListView { Id = "bootstrap-error-list", Width = Dim.Fill(), Height = 2, ShowMarks = false, KeystrokeNavigator = null };
        errorHeading = new Label { Id = "bootstrap-error-heading", Width = Dim.Fill(), Height = 1 };
        errorHeading.TextFormatter.WordWrap = true;
        errorHeading.SetScheme(new Scheme(new Attribute(UraTheme.Warning, UraTheme.Ground, TextStyle.Bold)));
        errorImpact = new Label { Id = "bootstrap-error-impact", Width = Dim.Fill(), Height = 1 };
        errorImpact.TextFormatter.WordWrap = true;
        errorImpact.SetScheme(new Scheme(new Attribute(UraTheme.Muted, UraTheme.Ground)));
        errorList.ValueChanged += (_, _) =>
        {
            if (applyingSnapshot)
                return;
            if (errorExpanded && errorList.SelectedItem is { } selected && selected < errorRows.Count)
                DisplayError(errorRows[selected]);
            UpdateErrorPresentation();
        };
        showError = new Button { Id = "bootstrap-show-error", Text = I18N_ViewError, NoDecorations = true, NoPadding = true, Height = 1, Width = I18N_ViewError.GetColumns() + 4, TextAlignment = Alignment.Center };
        copyError = new Button { Id = "bootstrap-copy-error", Text = I18N_CopyError, X = Pos.Right(showError) + 1, NoDecorations = true, NoPadding = true, Height = 1, Width = I18N_CopyError.GetColumns() + 4, TextAlignment = Alignment.Center };
        showError.SetScheme(ToolbarScheme);
        copyError.SetScheme(ToolbarScheme);
        errorDetails = new ListView
        {
            Id = "bootstrap-error-details", ShowMarks = false, KeystrokeNavigator = null,
            Width = Dim.Fill(), Height = 8, Visible = false, ViewportSettings = ViewportSettingsFlags.HasScrollBars
        };
        showError.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (errorList.SelectedItem is not { } selected || selected >= errorRows.Count)
                return;
            errorExpanded = !errorExpanded;
            if (errorExpanded)
                DisplayError(errorRows[selected]);
            UpdateErrorPresentation();
        };
        copyError.Accepting += (_, e) =>
        {
            e.Handled = true;
            var text = errorExpanded ? displayedError?.Details
                : errorList.SelectedItem is { } selected && selected < errorRows.Count ? errorRows[selected].Details : null;
            if (text is not null)
                Copy(text);
        };
        errors.Add(errorList, errorHeading, errorImpact, showError, copyError, errorDetails);
        var detailScheme = new Scheme(UraTheme.Base) { Active = UraTheme.Base.Normal };
        foreach (var list in new[] { settingsText, phasesText, pluginsText, diagnosticsText })
            list.SetScheme(detailScheme);
        body.Add(summary, summaryDetail, settingsToggle, settingsText, errors, detailsToggle, details, diagnosticsToggle, diagnosticsText);
        Add(heading, headerRule, toolbar, toolbarRule, body, copyResult, footerRule, footerNavigation, footerExit);
        foreach (var control in new View[] { summary, settingsToggle, settingsText, detailsToggle, phasesText, pluginsText,
            logList, diagnosticsToggle, diagnosticsText, errorList, showError, copyError, errorDetails })
        {
            control.HasFocusChanged += (_, _) =>
            {
                if (control.HasFocus && !focusingByMouse)
                    ScrollIntoBody(control);
            };
        }
        body.MouseEvent += HandleBodyMouse;
        foreach (var list in new[] { settingsText, phasesText, pluginsText, logList, diagnosticsText, errorList, errorDetails })
        {
            list.MouseEvent += HandleBodyMouse;
            list.ValueChanged += (_, _) =>
            {
                if (!applyingSnapshot && list.HasFocus)
                    ScrollIntoBody(list);
            };
        }
        AddCommand(Command.Accept, () => snapshot.Mode != BootstrapMode.Information);
        AddCommand(Command.Quit, () =>
        {
            if (logContextMenu is { Visible: true })
            {
                App?.Popovers?.Hide(logContextMenu);
                logList.SetFocus();
            }
            else if (moreMenu is { Visible: true })
            {
                App?.Popovers?.Hide(moreMenu);
                more.SetFocus();
            }
            else if (errorExpanded)
            {
                errorExpanded = false;
                LayoutBody();
                showError.SetFocus();
            }
            else if (snapshot.Mode is BootstrapMode.Preparing or BootstrapMode.Ready or BootstrapMode.Busy)
                requestShutdown();
            return true;
        });
        KeyBindings.Add(Key.Esc, Command.Quit);
        KeyDown += (_, key) =>
        {
            if (snapshot.Mode == BootstrapMode.Preparing &&
                (key == Key.Tab || key == Key.Tab.WithShift || key == Key.F6 || key == Key.F6.WithShift))
                userInteracted = true;
        };
        Initialized += (_, _) =>
        {
            initialized = true;
            if (snapshot.Mode == BootstrapMode.Ready)
                actions[0].SetFocus();
            else
                summary.SetFocus();
        };
    }

    internal void Update(BootstrapSnapshot value)
    {
        applyingSnapshot = true;
        summary.TabStop = value.Mode == BootstrapMode.Preparing ? TabBehavior.TabStop : TabBehavior.NoStop;
        var previous = snapshot;
        snapshot = value;
        var focusedAction = actions.FirstOrDefault(button => button.HasFocus);
        foreach (var button in actions)
            button.Enabled = value.Mode == BootstrapMode.Ready;
        if (focusedAction is not null && !focusedAction.Enabled)
            more.SetFocus();
        else if (initialized && previous.Mode == BootstrapMode.Preparing && value.Mode == BootstrapMode.Ready &&
            !userInteracted && (App?.TopRunnableView?.MostFocused is null ||
                ReferenceEquals(App.TopRunnableView.MostFocused, more) || ReferenceEquals(App.TopRunnableView.MostFocused, summary)))
            actions[0].SetFocus();
        foreach (var (item, _) in moreActions)
            item.Enabled = value.Mode == BootstrapMode.Ready;

        summary.Text = Summary(value);
        var failed = value.Phases.FirstOrDefault(phase => phase.Id != BootstrapPhaseId.Host && phase.State == BootstrapPhaseState.Failed)
            ?? value.Phases.FirstOrDefault(phase => phase.State == BootstrapPhaseState.Failed);
        summaryDetail.Text = failed is not null
            ? failed.Id == BootstrapPhaseId.Database ? I18N_DataFailureAction
                : failed.Id == BootstrapPhaseId.Listener ? I18N_ListenerFailureAction : I18N_StartupFailureAction
            : value.Plugins.Count == 0 && (value.Mode == BootstrapMode.Ready || value.Listening is not null)
                ? I18N_NoPluginsAvailable : string.Empty;
        summaryDetail.Visible = summaryDetail.Text.Length > 0;
        var summaryAppearance = new Attribute(
            failed is not null ? UraTheme.Error : value.Listening is not null ? UraTheme.Success : UraTheme.Text, UraTheme.Ground);
        summary.SetScheme(new Scheme(summaryAppearance) { Focus = summaryAppearance, HotFocus = summaryAppearance });
        if (value.Settings is { } settings)
        {
            var bindingHost = settings.ListenHost;
            if (IPAddress.TryParse(bindingHost, out var bindingAddress) && bindingAddress.AddressFamily == AddressFamily.InterNetworkV6)
                bindingHost = $"[{bindingAddress}]";
            UpdateList(settingsText,
            [
                $"{I18N_ListenAddress}  {bindingHost}:{settings.ListenPort}",
                $"{I18N_DataLanguage}  {settings.DataLanguage}"
            ]);
            UpdateList(diagnosticsText,
            [
                $"{I18N_Version}  {settings.Version}",
                $"{I18N_WorkingDirectory}  {settings.WorkingDirectory}",
                $"{I18N_ConfigFile}  {settings.ConfigFile}",
                $"{I18N_UpdateSource}  {settings.UpdateSource}"
            ]);
        }
        UpdateList(phasesText, value.Phases.Select(phase =>
            $"{BootstrapWorkspace.StateLabel(phase.State)}  {BootstrapWorkspace.PhaseLabel(phase.Id)}  {phase.Detail}"));
        UpdateList(pluginsText, value.Plugins.Select(plugin =>
            $"{plugin.Name}  {plugin.Version}  {plugin.Detail}"));
        var specificFailure = value.Phases.Any(phase => phase.Id != BootstrapPhaseId.Host && phase.State == BootstrapPhaseState.Failed);
        var nextErrors = value.Phases.Where(phase => phase.State == BootstrapPhaseState.Failed &&
                (phase.Id != BootstrapPhaseId.Host || !specificFailure))
            .Select(phase => new ErrorRow(phase.Id.ToString(), $"{BootstrapWorkspace.PhaseLabel(phase.Id)}  {phase.Detail}",
                phase.ExceptionDetails ?? phase.Detail, IsPhase: true))
            .Concat(value.Plugins.Where(plugin => plugin.State is BootstrapPluginState.Unavailable or BootstrapPluginState.StartFailed)
                .Select(plugin => new ErrorRow(plugin.Id, string.Format(I18N_PhaseFailure, plugin.Name, PluginFailureLabel(plugin)),
                    plugin.ExceptionDetails ?? plugin.Detail,
                    Impact: plugin.State == BootstrapPluginState.StartFailed ? I18N_PluginStillActive : I18N_PluginUnavailable))).ToArray();
        if (!errorRows.SequenceEqual(nextErrors))
        {
            var selectedId = errorList.SelectedItem is { } selected && selected < errorRows.Count ? errorRows[selected].Id : null;
            errorRows.Clear();
            errorRows.AddRange(nextErrors);
            errorList.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(errorRows.Select(row => row.Label)));
            if (errorRows.Count > 0)
                errorList.SelectedItem = Math.Max(0, errorRows.FindIndex(row => row.Id == selectedId));
        }
        UpdateErrorPresentation();
        UpdateLogs(value.Logs);
        LayoutBody();
        SetNeedsDraw();
        applyingSnapshot = false;
    }

    static string PluginFailureLabel(BootstrapPluginRow plugin) => plugin.FailureStage switch
    {
        PluginFailureStage.Scan => I18N_ScanFailed,
        PluginFailureStage.Load => I18N_LoadFailed,
        PluginFailureStage.Initialize => I18N_InitializeFailed,
        PluginFailureStage.Start => I18N_StartCallbackFailed,
        PluginFailureStage.Runtime => I18N_RuntimeFailed,
        null => plugin.State == BootstrapPluginState.StartFailed ? I18N_StartCallbackFailed : I18N_StateUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(plugin), plugin.FailureStage, null)
    };

    static string Summary(BootstrapSnapshot state)
    {
        if (state.Mode == BootstrapMode.Ready)
        {
            var access = state.Settings is { } settings
                ? string.Format(I18N_AccessSummary,
                    settings.ListenHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    IPAddress.TryParse(settings.ListenHost, out var address) && IPAddress.IsLoopback(address)
                        ? I18N_LocalAccess : I18N_DeviceAccess,
                    settings.ListenPort)
                : string.Empty;
            return access;
        }
        if (state.Mode == BootstrapMode.Busy && state.RunningAction is { } action)
            return string.Format(I18N_Running, BootstrapWorkspace.ActionLabel(action));
        var listening = state.Listening is { } endpoint ? $"{I18N_ListeningAddress}  {endpoint}" : string.Empty;
        var failed = state.Phases.FirstOrDefault(phase => phase.Id != BootstrapPhaseId.Host && phase.State == BootstrapPhaseState.Failed)
            ?? state.Phases.FirstOrDefault(phase => phase.State == BootstrapPhaseState.Failed);
        if (failed is not null)
        {
            var failure = failed.Id == BootstrapPhaseId.Database ? failed.Detail
                : string.Format(I18N_PhaseFailure, BootstrapWorkspace.PhaseLabel(failed.Id), failed.Detail);
            return listening.Length == 0 ? failure : $"{listening}\n{failure}";
        }
        var running = state.Phases.LastOrDefault(phase => phase.State == BootstrapPhaseState.Running);
        if (running is not null)
        {
            var progress = running.Detail;
            return listening.Length == 0 ? progress : $"{listening}\n{progress}";
        }
        if (listening.Length > 0)
            return listening;
        return I18N_PreparingStartup;
    }
    static void UpdateList(ListView list, IEnumerable<string> values)
    {
        var rows = values.ToArray();
        if (list.Source?.ToList().Cast<string>().SequenceEqual(rows) == true)
            return;
        var viewport = list.Viewport;
        var selected = list.SelectedItem;
        list.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(rows));
        if (rows.Length > 0)
            list.SelectedItem = Math.Clamp(selected ?? 0, 0, rows.Length - 1);
        list.Viewport = new Rectangle(
            Math.Clamp(viewport.X, 0, Math.Max(0, (list.Source?.MaxItemLength ?? 0) - viewport.Width)),
            Math.Clamp(viewport.Y, 0, Math.Max(0, rows.Length - viewport.Height)),
            viewport.Width, viewport.Height);
    }
    void UpdateLogs(IReadOnlyList<BootstrapLogRow> rows)
    {
        if (logSource.Rows.SequenceEqual(rows))
            return;
        var oldRows = logSource.Rows;
        var oldViewport = logList.Viewport;
        var selected = logList.SelectedItem is { } index && index < oldRows.Count ? oldRows[index] : null;
        var top = oldViewport.Y < oldRows.Count ? oldRows[oldViewport.Y] : null;
        var follow = logContextMenu is not { Visible: true } && (oldRows.Count == 0 ||
            logList.SelectedItem == oldRows.Count - 1 && oldViewport.Y + oldViewport.Height >= oldRows.Count);
        logSource.Update(rows);
        var selectedIndex = selected is null ? -1 : rows.ToList().FindIndex(row => ReferenceEquals(row, selected));
        if (rows.Count > 0)
            logList.SelectedItem = follow ? rows.Count - 1 : Math.Max(0, selectedIndex);
        var topIndex = top is null ? 0 : Math.Max(0, rows.ToList().FindIndex(row => ReferenceEquals(row, top)));
        logList.Viewport = new Rectangle(oldViewport.X, follow ? Math.Max(0, rows.Count - oldViewport.Height) : topIndex,
            oldViewport.Width, oldViewport.Height);
    }

    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        var width = Math.Max(1, Viewport.Width - 4);
        var x = 0;
        var y = 0;
        foreach (var button in actions.Append(more))
        {
            var buttonWidth = Math.Min(width, button.Text.GetColumns() + 4);
            if (x > 0 && x + buttonWidth > width)
            {
                x = 0;
                y += 2;
            }
            button.X = x;
            button.Y = y;
            button.Width = buttonWidth;
            button.Height = 1;
            x += buttonWidth + 1;
        }
        toolbar.Height = y + 1;
        LayoutBody();
        base.OnSubViewLayout(args);
    }

    protected override void OnSubViewsLaidOut(LayoutEventArgs args)
    {
        base.OnSubViewsLaidOut(args);
        if (laidOutSize == Viewport.Size && laidOutBodyHeight == body.Viewport.Height)
            return;
        laidOutSize = Viewport.Size;
        laidOutBodyHeight = body.Viewport.Height;
        if (body.MostFocused is { } focused)
            ScrollIntoBody(focused);
    }

    void LayoutBody()
    {
        var availableWidth = Math.Max(1, Viewport.Width - 4);
        var width = Math.Max(1, availableWidth - (body.VerticalScrollBar.Visible ? 1 : 0));
        var summaryFormatter = new TextFormatter { Text = summary.Text, WordWrap = true, ConstrainToWidth = width };
        var summaryHeight = Math.Max(1, summaryFormatter.GetLines().Count);
        var detailFormatter = new TextFormatter { Text = summaryDetail.Text, WordWrap = true, ConstrainToWidth = width };
        var summaryDetailHeight = summaryDetail.Visible ? Math.Max(1, detailFormatter.GetLines().Count) : 0;
        var feedbackFormatter = new TextFormatter { Text = copyResult.Text, WordWrap = true, ConstrainToWidth = availableWidth };
        var feedbackHeight = copyResult.Visible ? Math.Max(1, feedbackFormatter.GetLines().Count) : 0;
        summary.Height = summaryHeight;
        summaryDetail.Y = summaryHeight + 1;
        summaryDetail.Height = summaryDetailHeight;
        var summaryBlockHeight = summaryHeight + (summaryDetail.Visible ? summaryDetailHeight + 1 : 0);
        copyResult.Height = feedbackHeight;
        var footerWidth = I18N_FooterExit.GetColumns();
        var footerWrapped = I18N_FooterNavigation.GetColumns() + footerWidth + 3 > availableWidth;
        var footerHeight = footerWrapped ? 4 : 3;
        footerRule.Y = Pos.AnchorEnd(footerHeight);
        footerNavigation.Y = Pos.AnchorEnd(footerWrapped ? 3 : 2);
        footerExit.Y = Pos.AnchorEnd(2);
        footerExit.X = footerWrapped ? 2 : Math.Max(2, Viewport.Width - 2 - footerWidth);
        copyResult.Y = Pos.AnchorEnd(footerHeight + feedbackHeight + 1);
        body.Height = Dim.Fill(footerHeight + (copyResult.Visible ? feedbackHeight + 1 : 0));
        settingsToggle.Text = $"{(settingsExpanded ? "▾" : "▸")} {I18N_StartupSettings}";
        detailsToggle.Text = $"{(detailsExpanded ? "▾" : "▸")} {I18N_StartupDetails}";
        diagnosticsToggle.Text = $"{(diagnosticsExpanded ? "▾" : "▸")} {I18N_Diagnostics}";
        showError.Text = errorExpanded ? I18N_HideError : I18N_ViewError;
        settingsToggle.Visible = snapshot.Mode is BootstrapMode.Ready or BootstrapMode.Busy;
        detailsToggle.Visible = snapshot.Mode == BootstrapMode.Information && (snapshot.Listening is not null || snapshot.Phases.Any(phase =>
            phase.Id == BootstrapPhaseId.Listener && phase.State == BootstrapPhaseState.Failed ||
            phase.Id == BootstrapPhaseId.Host && phase.State is BootstrapPhaseState.Succeeded or BootstrapPhaseState.Warning));
        settingsText.Visible = settingsToggle.Visible && settingsExpanded;
        details.Visible = detailsToggle.Visible && detailsExpanded;
        diagnosticsToggle.Visible = diagnosticsVisible;
        diagnosticsText.Visible = diagnosticsVisible && diagnosticsExpanded;
        errorDetails.Visible = errorExpanded;
        errors.Visible = errorRows.Count > 0;
        errorList.Visible = errorRows.Count > 1;
        var y = summaryBlockHeight + 2;
        settingsToggle.Y = y;
        if (settingsToggle.Visible)
            y += 2;
        settingsText.Y = y;
        settingsText.Height = 3;
        if (settingsText.Visible)
            y += 4;
        errors.Y = y;
        var errorListHeight = errorList.Visible ? Math.Clamp(errorRows.Count, 1, 4) : 0;
        errorList.Height = errorListHeight;
        var errorY = errorListHeight;
        errorHeading.Y = errorY;
        if (errorHeading.Visible)
        {
            var formatter = new TextFormatter { Text = errorHeading.Text, WordWrap = true, ConstrainToWidth = width };
            errorHeading.Height = formatter.GetLines().Count;
            errorY += formatter.GetLines().Count + 1;
        }
        errorImpact.Y = errorY;
        if (errorImpact.Visible)
        {
            var formatter = new TextFormatter { Text = errorImpact.Text, WordWrap = true, ConstrainToWidth = width };
            errorImpact.Height = formatter.GetLines().Count;
            errorY += formatter.GetLines().Count + 1;
        }
        showError.Y = errorY;
        var showWidth = Math.Min(width, showError.Text.GetColumns() + 4);
        var copyWidth = Math.Min(width, copyError.Text.GetColumns() + 4);
        var errorActionsWrap = showWidth + copyWidth + 1 > width;
        showError.Width = showWidth;
        copyError.Width = copyWidth;
        copyError.X = errorActionsWrap ? 0 : showWidth + 1;
        copyError.Y = errorY + (errorActionsWrap ? 2 : 0);
        var errorActionsBottom = errorY + (errorActionsWrap ? 3 : 1);
        errorDetails.Y = errorActionsBottom + 1;
        var errorsHeight = errorActionsBottom + 2 + (errorExpanded ? 8 : 0);
        errors.Height = errorsHeight;
        if (errors.Visible)
            y += errorsHeight;
        detailsToggle.Y = y;
        if (detailsToggle.Visible)
            y += 2;
        details.Y = y;
        var phaseHeight = Math.Max(1, snapshot.Phases.Count) + 1;
        phasesText.Height = phaseHeight;
        pluginsText.Visible = snapshot.Plugins.Count > 0;
        pluginsText.Y = phaseHeight + 1;
        var pluginHeight = pluginsText.Visible ? snapshot.Plugins.Count + 1 : 0;
        pluginsText.Height = pluginHeight;
        var logTop = phaseHeight + 1 + (pluginsText.Visible ? pluginHeight + 1 : 0);
        logList.Y = logTop;
        var detailHeight = logTop + 9;
        details.Height = detailHeight;
        if (details.Visible)
            y += detailHeight + 1;
        diagnosticsToggle.Y = y;
        if (diagnosticsVisible)
            y += 2;
        diagnosticsText.Y = y;
        diagnosticsText.Height = 5;
        if (diagnosticsVisible && diagnosticsExpanded)
            y += 6;
        body.SetContentHeight(y);
    }

    void UpdateErrorPresentation()
    {
        var selected = errorExpanded ? displayedError
            : errorList.SelectedItem is { } index && index < errorRows.Count ? errorRows[index] : null;
        errorHeading.Visible = selected is { IsPhase: false };
        errorHeading.Text = selected?.Label ?? string.Empty;
        errorImpact.Visible = selected is { IsPhase: false };
        errorImpact.Text = selected?.Impact ?? string.Empty;
        LayoutBody();
    }

    void DisplayError(ErrorRow error)
    {
        displayedError = error;
        errorDetails.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(
            error.Details.ReplaceLineEndings("\n").Split('\n')));
    }

    void HandleBodyMouse(object? sender, Mouse mouse)
    {
        if (sender is ListView { HasFocus: false } clicked &&
            (mouse.IsPressed || mouse.IsReleased || mouse.IsSingleDoubleOrTripleClicked))
        {
            // Focus must not scroll before the native list resolves the clicked row.
            focusingByMouse = true;
            try { clicked.SetFocus(); }
            finally { focusingByMouse = false; }
        }
        var delta = mouse.Flags switch
        {
            MouseFlags.WheeledUp => -1,
            MouseFlags.WheeledDown => 1,
            _ => 0
        };
        if (delta == 0)
            return;
        if (sender is ListView list)
        {
            var previous = list.Viewport.Y;
            list.ScrollVertical(delta);
            if (list.Viewport.Y != previous)
            {
                mouse.Handled = true;
                return;
            }
        }
        var previousBody = body.Viewport.Y;
        body.ScrollVertical(delta);
        mouse.Handled = body.Viewport.Y != previousBody;
    }

    void ScrollIntoBody(View control)
    {
        if (body.Viewport.Height <= 0)
            return;
        var top = control.ViewportToScreen(Point.Empty).Y - body.ViewportToScreen(Point.Empty).Y;
        var height = Math.Min(control.Viewport.Height, body.Viewport.Height);
        if (control is ListView { SelectedItem: { } selected } list)
        {
            list.EnsureSelectedItemVisible();
            top += selected - list.Viewport.Y;
            height = 1;
        }
        if (top < 0)
            body.ScrollVertical(top);
        else if (top + height > body.Viewport.Height)
            body.ScrollVertical(top + height - body.Viewport.Height);
    }
    void ShowMore()
    {
        userInteracted = true;
        if (moreMenu is null)
        {
            var items = new List<MenuItem>();
            foreach (var action in new[] { BootstrapAction.UpdateProgram, BootstrapAction.Hachimi, BootstrapAction.QqGroup })
            {
                if (action == BootstrapAction.Hachimi && !OperatingSystem.IsWindows())
                    continue;
                var item = new MenuItem(BootstrapWorkspace.ActionLabel(action), action: () => choose(action))
                {
                    Enabled = snapshot.Mode == BootstrapMode.Ready
                };
                moreActions.Add((item, action));
                items.Add(item);
            }
            items.Add(new MenuItem(I18N_Diagnostics, action: () =>
            {
                diagnosticsVisible = true;
                diagnosticsExpanded = true;
                LayoutBody();
                diagnosticsToggle.SetFocus();
            }));
            var menu = new Menu(items.ToArray()) { BorderStyle = LineStyle.Single };
            menu.SetScheme(UraTheme.Raised);
            UraTheme.UseRuleBorder(menu);
            moreMenu = new PopoverMenu(menu) { App = App };
            moreMenu.SetScheme(UraTheme.Raised);
            App?.Popovers?.Register(moreMenu);
        }
        moreMenu.Target = new WeakReference<View>(more);
        moreMenu.MakeVisible(more.ViewportToScreen(new Point(0, more.Viewport.Height)));
    }

    void LogCommandNotBound(object? sender, CommandEventArgs e)
    {
        if (e.Context?.Command != Command.Context)
            return;
        e.Handled = true;
        ShowLogContextMenu(e.Context.Binding is MouseBinding { MouseEvent: { } mouse }
            ? mouse.ScreenPosition
            : logList.ViewportToScreen(new Point(0, Math.Max(0, (logList.SelectedItem ?? 0) - logList.Viewport.Y))));
    }

    void ShowLogContextMenu(Point position)
    {
        if (logList.SelectedItem is not { } selected || selected >= logSource.Rows.Count ||
            logSource.Rows[selected].ExceptionDetails is not { } text)
            return;
        exceptionDetailsToCopy = text;
        if (logContextMenu is null)
        {
            var menu = new Menu(new MenuItem[]
            {
                new(I18N_CopyBacktrace, action: () =>
                {
                    if (exceptionDetailsToCopy is { } details)
                        Copy(details);
                })
            }) { BorderStyle = LineStyle.Single };
            menu.SetScheme(UraTheme.Raised);
            UraTheme.UseRuleBorder(menu);
            logContextMenu = new PopoverMenu(menu) { App = App };
            logContextMenu.SetScheme(UraTheme.Raised);
            App?.Popovers?.Register(logContextMenu);
        }
        logContextMenu.Target = new WeakReference<View>(logList);
        logContextMenu.MakeVisible(position);
    }

    void Copy(string text)
    {
        copyResult.Visible = true;
        try
        {
            if (App?.Clipboard?.TrySetClipboardData(text) == true)
            {
                copyResult.Text = I18N_Copied;
                LayoutBody();
                return;
            }
        }
        catch (Exception exception)
        {
            TerminalUi.LogException("Clipboard", exception);
        }
        copyResult.Text = I18N_CopyBacktraceFailed;
        LayoutBody();
        TerminalUi.Notify("URA", I18N_CopyBacktraceFailed, UiSeverity.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var popover in new[] { moreMenu, logContextMenu })
            {
                if (popover is null)
                    continue;
                popover.App?.Popovers?.Hide(popover);
                popover.App?.Popovers?.DeRegister(popover);
                popover.Dispose();
            }
            onDisposed();
        }
        base.Dispose(disposing);
    }

    sealed record ErrorRow(string Id, string Label, string Details, bool IsPhase = false, string Impact = "");

    sealed class LogListSource(IReadOnlyList<BootstrapLogRow> rows) : IListDataSource
    {
        internal IReadOnlyList<BootstrapLogRow> Rows { get; private set; } = rows;
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public int Count => Rows.Count;
        public int MaxItemLength => Rows.Count == 0 ? 0 : Rows.Max(row => Display(row).GetColumns());
        public bool SuspendCollectionChangedEvent { get; set; }
        internal void Update(IReadOnlyList<BootstrapLogRow> value)
        {
            Rows = value;
            if (!SuspendCollectionChangedEvent)
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        public bool IsMarked(int item) => false;
        public void SetMark(int item, bool value) { }
        public IList ToList() => Rows.Select(Display).ToList();
        public bool RenderMark(ListView listView, int item, int row, bool isMarked, bool markMultiple) => false;
        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX)
        {
            var line = Rows[item];
            var current = listView.SelectedItem == item;
            var rowAttribute = listView.GetAttributeForRole(current
                ? listView.HasFocus ? VisualRole.Focus : VisualRole.Active : VisualRole.Normal);
            var status = $"● {line.Status}";
            var statusAttribute = current || line.Severity is not { } severity
                ? rowAttribute : new Attribute(UraTheme.SeverityColor(severity), rowAttribute.Background);
            var text = Display(line);
            var visible = CellText.Fit(viewportX == 0 ? text : SkipColumns(text, viewportX), Math.Max(0, width));
            var statusPart = CellText.Clip(visible, Math.Max(0, status.GetColumns() - viewportX));
            listView.Move(col, row);
            listView.SetAttribute(statusAttribute);
            listView.AddStr(statusPart);
            listView.SetAttribute(rowAttribute);
            listView.AddStr(visible[statusPart.Length..]);
        }
        public void Dispose() { }
        static string Display(BootstrapLogRow row) => $"● {row.Status} {row.Text.ReplaceLineEndings("  ")}";
        static string SkipColumns(string text, int columns)
        {
            var elements = StringInfo.GetTextElementEnumerator(text);
            var skipped = 0;
            while (columns > 0 && elements.MoveNext())
            {
                var element = elements.GetTextElement();
                columns -= Math.Max(0, element.GetColumns());
                skipped += element.Length;
            }
            return text[skipped..];
        }
    }
}
