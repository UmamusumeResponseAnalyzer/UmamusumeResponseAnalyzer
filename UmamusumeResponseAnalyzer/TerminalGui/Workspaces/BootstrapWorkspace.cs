using System.Collections.ObjectModel;
using System.Data;
using System.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;
using UiText = UmamusumeResponseAnalyzer.Localization.TerminalGui;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class BootstrapWorkspace : IDisposable
{
    const int MaxLogRows = 128;
    internal const string PanelKey = "status";
    const string MenuPanelKey = "menu";

    readonly UiHost uiHost;
    readonly object gate = new();
    readonly List<(string Label, string Value)> settings = [];
    readonly Dictionary<string, BootstrapPhase> phases = new()
    {
        ["config"] = new(I18N_PhaseConfig, UiSeverity.Info, I18N_Waiting),
        ["plugin-scan"] = new(I18N_PhasePluginScan, UiSeverity.Info, I18N_Waiting),
        ["database"] = new(I18N_PhaseDatabase, UiSeverity.Info, I18N_Waiting),
        ["plugin-init"] = new(I18N_PhasePluginInit, UiSeverity.Info, I18N_Waiting),
        ["server"] = new(I18N_PhaseServer, UiSeverity.Info, I18N_Waiting),
        ["host"] = new(I18N_PhaseHost, UiSeverity.Info, I18N_Waiting)
    };
    readonly List<BootstrapPluginRow> plugins = [];
    readonly List<BootstrapLogRow> logs = [];
    TaskCompletionSource<string>? menuSelection;
    bool showingMenu;
    bool disposed;

    public BootstrapWorkspace(UiHost uiHost, Workspace workspace)
    {
        this.uiHost = uiHost;
        Workspace = workspace;
        uiHost.BindWorkspaceHotkey(
            Workspace,
            ConsoleKey.B,
            ConsoleModifiers.Control,
            I18N_StartupInformation);
        uiHost.LogAdded += OnLogAdded;
        Refresh();
    }

    public Workspace Workspace { get; }

    internal void ShowPreparingMenu(string title, IReadOnlyList<string> choices)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            SetMenuPanel(title, choices, null);
        }
    }

    internal async Task<string> ShowMenuAsync(
        string title,
        IReadOnlyList<string> choices,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ThrowIfDisposed();
            if (menuSelection is { Task.IsCompleted: false })
                throw new InvalidOperationException(I18N_MenuPending);
            menuSelection = selection;
            SetMenuPanel(title, choices, selection);
        }
        using var cancellation = cancellationToken.Register(() => selection.TrySetCanceled(cancellationToken));
        return await selection.Task;
    }

    void SetMenuPanel(string title, IReadOnlyList<string> choices, TaskCompletionSource<string>? selection)
    {
        showingMenu = true;
        uiHost.SetPanel(
            Workspace,
            MenuPanelKey,
            title,
            new WorkspaceContent(() => new BootstrapMenuView(title, choices, selection, uiHost.RequestShutdown)),
            fullBleed: true,
            switchToWorkspace: true);
    }

    internal void ShowInformation()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            menuSelection?.TrySetCanceled();
            menuSelection = null;
            showingMenu = false;
            uiHost.RemovePanel(Workspace, MenuPanelKey);
            Refresh();
            uiHost.SwitchWorkspace(Workspace);
        }
    }

    public void SetSettings(IReadOnlyList<(string Label, string Value)> values)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            settings.Clear();
            settings.AddRange(values);
        }
        Refresh();
    }

    public void SetPhase(string key, string label, UiSeverity severity, string detail)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            phases[key] = new(label, severity, detail);
        }
        Refresh();
    }

    public void SetPluginSummary(IReadOnlyList<BootstrapPluginRow> values)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            plugins.Clear();
            plugins.AddRange(values);
        }
        Refresh();
    }

    public void Log(string source, string text, UiSeverity severity = UiSeverity.Info)
    {
        lock (gate)
            ThrowIfDisposed();
        uiHost.Log($"[{source}] {text}", severity);
    }

    void OnLogAdded(UiLogLine line)
    {
        lock (gate)
        {
            if (disposed)
                return;
            logs.Add(new(
                SeverityLabel(line.Severity),
                line.Text,
                line.ExceptionDetails));
            if (logs.Count > MaxLogRows)
                logs.RemoveRange(0, logs.Count - MaxLogRows);
        }
    }

    void Refresh()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (showingMenu)
                return;
            uiHost.SetPanel(
                Workspace,
                PanelKey,
                I18N_StartupStatus,
                new WorkspaceContent(() =>
                {
                    (string Label, string Value)[] settingsSnapshot;
                    BootstrapPhase[] phaseSnapshot;
                    BootstrapPluginRow[] pluginSnapshot;
                    BootstrapLogRow[] logSnapshot;
                    lock (gate)
                    {
                        settingsSnapshot = settings.ToArray();
                        phaseSnapshot = phases.Values.ToArray();
                        pluginSnapshot = plugins.ToArray();
                        logSnapshot = logs.ToArray();
                    }

                    return new BootstrapDashboardView(
                        settingsSnapshot,
                        phaseSnapshot,
                        pluginSnapshot,
                        logSnapshot);
                }),
                fullBleed: true,
                switchToWorkspace: false);
        }
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

    void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(disposed, this);

    internal static string SeverityLabel(UiSeverity severity) => severity switch
    {
        UiSeverity.Trace => UiText.Severity_Info,
        UiSeverity.Info => UiText.Severity_Info,
        UiSeverity.Success => UiText.Severity_Success,
        UiSeverity.Warning => UiText.Severity_Warning,
        UiSeverity.Error => UiText.Severity_Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
    };
}

internal sealed class BootstrapMenuView : View
{
    readonly TaskCompletionSource<string>? selection;
    readonly Menu menu;

    internal BootstrapMenuView(
        string title,
        IReadOnlyList<string> choices,
        TaskCompletionSource<string>? selection,
        Action requestShutdown)
    {
        this.selection = selection;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        var prompt = new Label
        {
            Text = selection is null ? I18N_PreparingStartup : title,
            Width = Dim.Fill(),
            Height = 3
        };
        var items = choices.Select(value => new MenuItem
        {
            Title = value,
            Action = () =>
            {
                if (selection?.TrySetResult(value) != true)
                    return;
                menu!.Enabled = false;
                SetFocus();
                prompt.Text = string.Format(I18N_Running, value);
            }
        }).ToArray();
        menu = new Menu(items)
        {
            Y = Pos.Bottom(prompt), Width = Dim.Fill(), Height = Dim.Fill(), Enabled = selection is not null
        };
        AddCommand(Command.Down, () =>
        {
            menu.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop);
            return true;
        });
        AddCommand(Command.Up, () =>
        {
            menu.AdvanceFocus(NavigationDirection.Backward, TabBehavior.TabStop);
            return true;
        });
        KeyBindings.Add(Key.CursorDown, Command.Down);
        KeyBindings.Add(Key.CursorUp, Command.Up);
        AddCommand(Command.Accept, () => true);
        Add(prompt, menu);
        Initialized += (_, _) =>
        {
            if (selection is null)
                SetFocus();
            else
                items[0].SetFocus();
        };
        AddCommand(Command.Quit, () =>
        {
            if (selection is null)
                requestShutdown();
            else
                selection.TrySetCanceled();
            return true;
        });
        KeyBindings.Add(Key.Esc, Command.Quit);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            selection?.TrySetCanceled();
        base.Dispose(disposing);
    }
}

internal sealed record BootstrapPhase(
    string Label,
    UiSeverity Severity,
    string Detail);

internal sealed record BootstrapPluginRow(
    string Name,
    string Version,
    string Status,
    string Result)
{
    public BootstrapPluginRow(
        PluginManager.PluginRuntimeStatus plugin,
        bool initialized,
        bool failed)
        : this(
            plugin.DisplayName == plugin.InternalName
                ? plugin.DisplayName
                : $"{plugin.DisplayName} ({plugin.InternalName})",
            plugin.Version?.ToString() ?? string.Empty,
            plugin.IsLoaded ? initialized ? UiText.Severity_Success : UiText.Severity_Info : failed || !plugin.IsAvailable ? UiText.Severity_Error : UiText.Severity_Info,
            plugin.IsLoaded
                ? initialized ? I18N_Initialized : I18N_Scanned
                : failed || !plugin.IsAvailable
                    ? initialized ? I18N_LoadOrInitializeFailed : I18N_LoadFailed
                    : I18N_Unloaded)
    {
    }
}

internal sealed record BootstrapLogRow(
    string Status,
    string Text,
    string? ExceptionDetails = null);

internal sealed class BootstrapDashboardView : View
{
    const int WideLayoutMinimumWidth = 96;

    readonly FrameView environmentFrame;
    readonly FrameView phaseFrame;
    readonly FrameView pluginFrame;
    readonly FrameView logFrame;
    readonly ListView logList;
    readonly IReadOnlyList<BootstrapLogRow> logs;
    PopoverMenu? logContextMenu;
    string? exceptionDetailsToCopy;
    bool? wideLayout;

    public BootstrapDashboardView(
        IReadOnlyList<(string Label, string Value)> settings,
        IReadOnlyList<BootstrapPhase> phases,
        IReadOnlyList<BootstrapPluginRow> plugins,
        IReadOnlyList<BootstrapLogRow> logs)
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        this.logs = logs;

        environmentFrame = CreateFrame(
            I18N_Environment,
            CreateTable(
                [I18N_Item, I18N_Value],
                settings.Select(x => new[] { x.Label, x.Value })));
        phaseFrame = CreateFrame(
            I18N_InitializationResults,
            CreateTable(
                [I18N_Status, I18N_Item, I18N_Result],
                phases.Select(x => new[]
                {
                    BootstrapWorkspace.SeverityLabel(x.Severity),
                    x.Label,
                    x.Detail
                })));
        pluginFrame = CreateFrame(
            I18N_PluginSummary,
            CreateTable(
                [I18N_PluginName, I18N_Version, I18N_Status, I18N_Result],
                plugins.Select(x => new[] { x.Name, x.Version, x.Status, x.Result })));

        logList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ShowMarks = false,
            MarkMultiple = false,
            KeystrokeNavigator = null,
            ViewportSettings = ViewportSettingsFlags.HasScrollBars
        };
        logList.Activating += LogListActivating;
        logList.CommandNotBound += LogListCommandNotBound;
        logList.MouseBindings.ReplaceCommands(
            MouseFlags.RightButtonClicked,
            Command.Activate,
            Command.Context);
        logList.SetSource(new ObservableCollection<string>(
            logs.Select(x => $"{x.Status} {x.Text}")));
        if (logs.Count > 0)
            logList.SelectedItem = logs.Count - 1;
        logFrame = CreateFrame(I18N_RecentLogs, logList);

        Add(environmentFrame, phaseFrame, pluginFrame, logFrame);
        ApplyLayout(useWideLayout: false);
    }

    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        var useWideLayout = Viewport.Width >= WideLayoutMinimumWidth;
        if (wideLayout != useWideLayout)
            ApplyLayout(useWideLayout);
        base.OnSubViewLayout(args);
        if (logs.Count > 0)
            logList.EnsureSelectedItemVisible();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            logList.Activating -= LogListActivating;
            logList.CommandNotBound -= LogListCommandNotBound;
            if (logContextMenu is { } contextMenu)
            {
                contextMenu.App?.Popovers?.Hide(contextMenu);
                contextMenu.App?.Popovers?.DeRegister(contextMenu);
                contextMenu.Dispose();
                logContextMenu = null;
                exceptionDetailsToCopy = null;
            }
        }
        base.Dispose(disposing);
    }

    void LogListActivating(object? sender, CommandEventArgs e)
    {
        if (e.Context is
            {
                Command: Command.Activate,
                Binding: MouseBinding { MouseEvent: { } mouse }
            } &&
            (mouse.Flags & MouseFlags.RightButtonClicked) != 0)
        {
            logList.SelectedItem = null;
        }
    }

    void LogListCommandNotBound(object? sender, CommandEventArgs e)
    {
        if (e.Context is not
            {
                Command: Command.Context,
                Binding: MouseBinding { MouseEvent: { } mouse }
            })
            return;

        e.Handled = true;
        ShowLogContextMenu(mouse.ScreenPosition);
    }

    void ShowLogContextMenu(Point screenPosition)
    {
        exceptionDetailsToCopy = null;
        if (logList.SelectedItem is not { } selected ||
            selected < 0 ||
            selected >= logs.Count ||
            logs[selected].ExceptionDetails is not { } details)
        {
            if (logContextMenu is { Visible: true } visibleMenu)
                visibleMenu.App?.Popovers?.Hide(visibleMenu);
            return;
        }

        exceptionDetailsToCopy = details;
        if (logContextMenu is null)
        {
            var app = App ?? throw new InvalidOperationException(
                I18N_DashboardNotAttached);
            logContextMenu = new PopoverMenu(new Menu(new MenuItem[]
            {
                new(I18N_CopyBacktrace, action: CopyExceptionDetails)
            }))
            {
                App = app
            };
            app.Popovers?.Register(logContextMenu);
        }

        logContextMenu.Target = new WeakReference<View>(logList);
        logContextMenu.MakeVisible(screenPosition);
    }

    void CopyExceptionDetails()
    {
        if (logContextMenu is { } contextMenu)
            contextMenu.Target = null;

        var details = exceptionDetailsToCopy;
        exceptionDetailsToCopy = null;
        if (details is null)
            return;

        try
        {
            if (logContextMenu?.App?.Clipboard?.TrySetClipboardData(details) == true)
                return;
        }
        catch (Exception exception)
        {
            TerminalUi.LogException("Clipboard", exception);
        }

        TerminalUi.Notify(
            "URA",
            I18N_CopyBacktraceFailed,
            UiSeverity.Error);
    }

    void ApplyLayout(bool useWideLayout)
    {
        wideLayout = useWideLayout;
        if (useWideLayout)
        {
            environmentFrame.X = 0;
            environmentFrame.Y = 0;
            environmentFrame.Width = Dim.Percent(50);
            environmentFrame.Height = Dim.Percent(36);

            phaseFrame.X = Pos.Right(environmentFrame);
            phaseFrame.Y = 0;
            phaseFrame.Width = Dim.Fill();
            phaseFrame.Height = Dim.Percent(36);

            pluginFrame.X = 0;
            pluginFrame.Y = Pos.Bottom(environmentFrame);
            pluginFrame.Width = Dim.Fill();
            pluginFrame.Height = Dim.Percent(34);
        }
        else
        {
            environmentFrame.X = 0;
            environmentFrame.Y = 0;
            environmentFrame.Width = Dim.Fill();
            environmentFrame.Height = Dim.Percent(25);

            phaseFrame.X = 0;
            phaseFrame.Y = Pos.Bottom(environmentFrame);
            phaseFrame.Width = Dim.Fill();
            phaseFrame.Height = Dim.Percent(25);

            pluginFrame.X = 0;
            pluginFrame.Y = Pos.Bottom(phaseFrame);
            pluginFrame.Width = Dim.Fill();
            pluginFrame.Height = Dim.Percent(25);
        }

        logFrame.X = 0;
        logFrame.Y = Pos.Bottom(pluginFrame);
        logFrame.Width = Dim.Fill();
        logFrame.Height = Dim.Fill();
    }

    static FrameView CreateFrame(string title, View content)
    {
        var frame = new FrameView
        {
            Title = title,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        frame.Add(content);
        return frame;
    }

    static TableView CreateTable(string[] columns, IEnumerable<string[]> rows)
    {
        var data = new DataTable();
        foreach (var column in columns)
            data.Columns.Add(column);
        foreach (var row in rows)
            data.Rows.Add(row.Cast<object>().ToArray());

        var table = new TableView(new DataTableSource(data))
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            MultiSelect = false,
            UseAllRowsForContentCalculation = true,
            MaxCellWidth = int.MaxValue,
            CollectionNavigator = null,
            ViewportSettings = ViewportSettingsFlags.HasScrollBars
        };
        table.Style.AlwaysShowHeaders = true;
        table.Style.ShowHorizontalHeaderOverline = false;
        table.RefreshContentSize();
        return table;
    }
}
