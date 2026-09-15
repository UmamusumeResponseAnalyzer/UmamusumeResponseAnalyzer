using static HistoryConfigDialog;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Gallop.Endpoints;
using MessagePack;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

sealed record PanelProbe(
    Func<object> CreateResponse,
    string WorkspaceTitle,
    string Key,
    string Title,
    bool FullBleed,
    bool RequiresEventLogger = false,
    int NotificationCount = 0,
    UiSeverity? NotificationSeverity = null,
    string? VisibleText = null,
    Func<object>? CreateUpdatedResponse = null,
    string? InitialUpdateText = null,
    string? UpdatedVisibleText = null,
    bool RemovesWorkspaceOnDispose = false,
    ScenarioHistoryProbe? History = null);

sealed record ScenarioHistoryProbe(
    Func<int, int, int, object> CreateResponse);

sealed record PluginCase(
    string Id,
    Func<IPlugin> Create,
    PanelProbe? Panel = null,
    string? ExpectedBootstrapLogOnInitialize = null)
{
    public void Run(WorkspaceSmokeSession ui)
    {
        using var currentDirectory = new TempCurrentDirectory(Id);
        using var context = new RuntimePluginContext(ui.Application);
        using var eventLoggerContext = Panel?.RequiresEventLogger == true
            ? new RuntimePluginContext(ui.Application)
            : null;
        IPlugin? eventLogger = eventLoggerContext is null
            ? null
            : new EventLoggerPlugin.EventLoggerPlugin();
        var plugin = Create();
        Workspace? publishedWorkspace = null;
        Exception? failure = null;

        if (Id == "DMMPlugin")
            PrepareDmmFixture(currentDirectory.Path);
        if (Panel?.History is not null)
            WriteHistoryLimit(currentDirectory.Path, 3);
        ui.Bootstrap.SwitchTo();
        try
        {
            eventLogger?.Initialize(eventLoggerContext!);
            plugin.Initialize(context);

            ui.Flush();
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
                throw new InvalidOperationException($"{Id}: Initialize changed the active bootstrap workspace.");
            if (Id == "DMMPlugin")
                VerifyDmmRuntime(context, ui, currentDirectory.Path);

            if (ExpectedBootstrapLogOnInitialize is { } expectedLog
                && !ui.CaptureScreen().Contains(expectedLog, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{Id}: the real Host framebuffer did not expose bootstrap log '{expectedLog}'.");
            }

            if (Panel is not null)
            {
                publishedWorkspace = Workspace.Create(Panel.WorkspaceTitle);
                VerifyPanelRuntime(plugin, context, ui, publishedWorkspace);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            plugin.Dispose();
            eventLogger?.Dispose();

            if (publishedWorkspace is not null)
            {
                var recreatedWorkspace = Workspace.Create(Panel!.WorkspaceTitle);
                try
                {
                    if (Panel.RemovesWorkspaceOnDispose)
                    {
                        if (ReferenceEquals(recreatedWorkspace, publishedWorkspace))
                        {
                            throw new InvalidOperationException(
                                $"{Id}: Dispose did not remove its owned workspace generation.");
                        }

                        var removedHandleRejected = false;
                        try
                        {
                            publishedWorkspace.SwitchTo();
                        }
                        catch (InvalidOperationException ex) when (
                            ex.Message.Contains("removed", StringComparison.OrdinalIgnoreCase))
                        {
                            removedHandleRejected = true;
                        }
                        if (!removedHandleRejected)
                            throw new InvalidOperationException(
                                $"{Id}: the removed workspace generation still accepted public input.");
                    }
                    else if (!ReferenceEquals(recreatedWorkspace, publishedWorkspace))
                    {
                        throw new InvalidOperationException(
                            $"{Id}: Dispose removed the shared workspace generation.");
                    }

                    recreatedWorkspace.SwitchTo();
                    var disposedScreen = ui.CaptureScreen();
                    var panelMarker = Panel.VisibleText ?? Panel.Title;
                    if (disposedScreen.Contains(panelMarker, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"{Id}: Dispose left panel '{Panel.Key}' visible.");
                    }
                }
                finally
                {
                    recreatedWorkspace.Remove();
                }
            }

        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    void VerifyPanelRuntime(
        IPlugin plugin,
        RuntimePluginContext context,
        WorkspaceSmokeSession ui,
        Workspace expectedWorkspace)
    {
        void Dispatch(object response)
        {
            var registration = context.AnalyzerRegistry.Registrations
                .SingleOrDefault(candidate => candidate.PayloadType == response.GetType());
            var dispatch = registration is null
                ? DispatchAttributedAnalyzer(plugin, response)
                : registration.Invoke(response);
            dispatch.GetAwaiter().GetResult();
        }

        var directOutput = CaptureConsole(() => Dispatch(Panel!.CreateResponse()));
        if (!string.IsNullOrEmpty(directOutput))
            throw new InvalidOperationException($"{Id}: analyzer wrote directly to the process console: {directOutput}");

        ui.Flush();
        if (!ReferenceEquals(Workspace.Current, expectedWorkspace))
            throw new InvalidOperationException($"{Id}: analyzer did not switch to its workspace.");

        var framebuffer = Panel!.Key == "training"
            ? CaptureScrollablePanel(ui)
            : ui.CaptureScreen();
        if (string.IsNullOrWhiteSpace(framebuffer))
            throw new InvalidOperationException($"{Id}: panel rendered an empty framebuffer.");
        if (Panel.VisibleText is { } visibleText
            && !framebuffer.Contains(visibleText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Id}: framebuffer is missing '{visibleText}'.");
        }
        if (Panel.InitialUpdateText is { } initialUpdateText
            && !framebuffer.Contains(initialUpdateText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Id}: initial framebuffer is missing '{initialUpdateText}'.");
        }
        if (!Panel.FullBleed && !framebuffer.Contains(Panel.Title, StringComparison.Ordinal))
            throw new InvalidOperationException($"{Id}: framebuffer is missing panel title '{Panel.Title}'.");

        if (Panel.NotificationSeverity is { } severity)
        {
            var visibleSeverity = severity switch
            {
                UiSeverity.Trace => "TRACE ",
                UiSeverity.Info => "INFO ",
                UiSeverity.Success => "OK ",
                UiSeverity.Warning => "WARN ",
                UiSeverity.Error => "ERR ",
                _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
            };
            var actual = framebuffer.Split(visibleSeverity, StringSplitOptions.None).Length - 1;
            if (actual != Panel.NotificationCount)
            {
                throw new InvalidOperationException(
                    $"{Id}: expected {Panel.NotificationCount} visible {severity} notifications, actual={actual}.");
            }
        }

        if (Panel.Key == "training")
        {
            ui.Bootstrap.SwitchTo();
            directOutput = CaptureConsole(() => Dispatch(
                Panel.CreateUpdatedResponse?.Invoke()
                ?? throw new InvalidOperationException($"{Id}: training probe has no updated response.")));
            ui.Flush();
            if (!string.IsNullOrEmpty(directOutput))
                throw new InvalidOperationException($"{Id}: later analyzer dispatch wrote to the process console: {directOutput}");
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
                throw new InvalidOperationException($"{Id}: later training update stole workspace focus.");

            expectedWorkspace.SwitchTo();
            var refreshed = CaptureScrollablePanel(ui);
            if (Panel.InitialUpdateText is { } oldText
                && refreshed.Contains(oldText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{Id}: later training dispatch left stale panel text '{oldText}'.");
            }
            if (Panel.UpdatedVisibleText is { } updatedVisibleText
                && !refreshed.Contains(updatedVisibleText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{Id}: later training dispatch did not update its existing panel.");
            }

            if (Panel.History is { } history)
                VerifyScenarioHistory(plugin, Dispatch, history, ui, expectedWorkspace);
        }
    }

    void VerifyScenarioHistory(
        IPlugin plugin,
        Action<object> dispatch,
        ScenarioHistoryProbe probe,
        WorkspaceSmokeSession ui,
        Workspace expectedWorkspace)
    {
        const int firstChara = 1001;
        const int secondChara = 2002;
        const int thirdChara = 3003;

        dispatch(probe.CreateResponse(66, firstChara, 2));
        dispatch(probe.CreateResponse(61, secondChara, 2));
        ui.Flush();
        RequirePanelText(ui, "61/100", "latest record");

        NavigateAndRequire(ui, Key.CursorUp, "66/100", "up selects the older record");
        NavigateAndRequire(ui, Key.CursorLeft, "73/100", "left selects the oldest record");
        NavigateAndRequire(ui, Key.CursorRight, "61/100", "right selects the newest record");
        NavigateAndRequire(ui, Key.CursorUp, "66/100", "up returns to the middle record");
        NavigateAndRequire(ui, Key.CursorDown, "61/100", "down selects the newer record");
        NavigateAndRequire(ui, Key.CursorUp, "66/100", "up prepares an old selection");
        if (ui.WasHandledByApplicationKeyDown(Key.CursorUp.WithCtrl))
            throw new InvalidOperationException($"{Id}: a modified arrow key was consumed by history.");
        RequirePanelText(ui, "66/100", "modified arrow keys do not navigate history");

        ui.Bootstrap.SwitchTo();
        if (ui.WasHandledByApplicationKeyDown(Key.CursorRight))
            throw new InvalidOperationException($"{Id}: history consumed an arrow key in another workspace.");
        expectedWorkspace.SwitchTo();
        RequirePanelText(ui, "66/100", "another workspace does not receive history keys");

        ui.Bootstrap.SwitchTo();
        dispatch(probe.CreateResponse(55, secondChara, 3));
        ui.Flush();
        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
            throw new InvalidOperationException($"{Id}: a new history record stole workspace focus.");

        expectedWorkspace.SwitchTo();
        RequirePanelText(ui, "66/100", "new record preserves an old selection");

        dispatch(probe.CreateResponse(64, firstChara, 2));
        ui.Flush();
        RequirePanelText(ui, "64/100", "same-key update refreshes the selected record");

        dispatch(probe.CreateResponse(49, thirdChara, 3));
        ui.Flush();
        RequirePanelText(ui, "49/100", "evicting the selected record jumps to latest");
        NavigateAndRequire(ui, Key.CursorLeft, "61/100", "capacity eviction removes the oldest records");
        if (!ui.WasHandledByApplicationKeyDown(Key.CursorLeft))
            throw new InvalidOperationException($"{Id}: an enabled history boundary key was not consumed.");
        RequirePanelText(ui, "61/100", "oldest boundary stays on the oldest record");
        NavigateAndRequire(ui, Key.CursorRight, "49/100", "right returns to latest after eviction");

        NavigateAndRequire(ui, Key.CursorLeft, "61/100", "left prepares modal key isolation");
        AssertCanceledConfigLeavesFileUnchanged(plugin, ui, "取消");
        AssertCanceledConfigLeavesFileUnchanged(plugin, ui, "Esc");
        AssertCanceledConfigLeavesFileUnchanged(plugin, ui, "Close");
        NavigateAndRequire(ui, Key.CursorRight, "49/100", "right returns to latest after config cancellation");

        SaveHistoryLimit(plugin, ui, 0);
        AssertSavedHistoryLimit(0);
        dispatch(probe.CreateResponse(44, 4004, 4));
        ui.Flush();
        RequirePanelText(ui, "44/100", "limit zero keeps the current output");
        if (ui.WasHandledByApplicationKeyDown(Key.CursorLeft))
            throw new InvalidOperationException($"{Id}: historyLimit=0 consumed a direction key.");
        RequirePanelText(ui, "44/100", "limit zero disables navigation");
        dispatch(probe.CreateResponse(38, 5005, 5));
        ui.Flush();
        RequirePanelText(ui, "38/100", "limit zero replaces the current output");

        SaveHistoryLimit(plugin, ui, 3);
        AssertSavedHistoryLimit(3);
        if (ui.WasHandledByApplicationKeyDown(Key.CursorLeft))
            throw new InvalidOperationException($"{Id}: raising historyLimit back to 3 backfilled disabled history.");
        RequirePanelText(ui, "38/100", "raising the limit does not backfill disabled history");
        dispatch(probe.CreateResponse(32, 6006, 6));
        ui.Flush();
        NavigateAndRequire(ui, Key.CursorLeft, "32/100", "history resumes with the next output");

        void AssertSavedHistoryLimit(int expected)
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(HistorySettingsPath()));
            var root = settings.RootElement;
            if (root.EnumerateObject().Count() != 1
                || root.GetProperty("historyLimit").GetInt32() != expected)
            {
                throw new InvalidOperationException(
                    $"{Id}: expected strict persisted historyLimit={expected}.");
            }
        }

        string HistorySettingsPath()
            => Path.Combine("PluginData", Id, "settings.json");
    }

    void NavigateAndRequire(
        WorkspaceSmokeSession ui,
        Key key,
        string expected,
        string operation)
    {
        ui.SendKey(key);
        ui.Flush();
        RequirePanelText(ui, expected, operation);
    }

    void RequirePanelText(WorkspaceSmokeSession ui, string expected, string operation)
    {
        ui.SendKey(Key.Tab);
        var screen = CaptureScrollablePanel(ui);
        if (!screen.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{Id}: {operation} did not render '{expected}'.");
    }

    void SaveHistoryLimit(IPlugin plugin, WorkspaceSmokeSession ui, int value)
    {
        ui.RunDialogAsync(
                () => plugin.ConfigPromptAsync(ui.Application),
                dialog =>
                {
                    SetHistoryLimit(dialog, value);
                    AcceptButton(ui.Application, dialog, "保存");
                })
            .GetAwaiter().GetResult();
    }

    void AssertCanceledConfigLeavesFileUnchanged(
        IPlugin plugin,
        WorkspaceSmokeSession ui,
        string action)
    {
        var settingsPath = Path.Combine("PluginData", Id, "settings.json");
        var baseline = File.ReadAllBytes(settingsPath);
        var task = ui.RunDialogAsync(
            () => plugin.ConfigPromptAsync(ui.Application),
            dialog =>
            {
                SetHistoryLimit(dialog, 2);
                ui.Application.Keyboard.RaiseKeyDownEvent(Key.CursorRight);
                switch (action)
                {
                    case "取消":
                        AcceptButton(ui.Application, dialog, "取消");
                        break;
                    case "Esc":
                        ui.Application.Keyboard.RaiseKeyDownEvent(Key.Esc);
                        break;
                    case "Close":
                        ui.Application.RequestStop(dialog);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(action), action, null);
                }
            });

        try
        {
            task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            throw new InvalidOperationException($"{Id}: config {action} did not cancel.");
        }
        catch (OperationCanceledException)
        {
        }

        if (!baseline.SequenceEqual(File.ReadAllBytes(settingsPath)))
            throw new InvalidOperationException($"{Id}: config {action} changed settings.json.");
        RequirePanelText(ui, "61/100", $"config {action} isolates modal arrow keys");
    }

    static string CaptureScrollablePanel(WorkspaceSmokeSession ui)
    {
        ui.SendKey(Key.Home);
        var pages = new List<string>();
        for (var page = 0; page < 32; page++)
        {
            var framebuffer = ui.CaptureScreen();
            if (pages.Contains(framebuffer, StringComparer.Ordinal))
                break;
            pages.Add(framebuffer);
            ui.SendKey(Key.PageDown);
        }
        return string.Join(Environment.NewLine, pages);
    }

    static void PrepareDmmFixture(string currentDirectory)
    {
        var settingsPath = Path.Combine(currentDirectory, "PluginData", "DMM插件", "settings.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(
            settingsPath,
            """
            enable: true
            accounts:
            - name: runtime-smoke
              account: ''
              password: ''
              access-token: ''
            """);
    }

    void WriteHistoryLimit(string currentDirectory, int value)
    {
        var settingsPath = Path.Combine(currentDirectory, "PluginData", Id, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, $$"""
            {
              "historyLimit": {{value}}
            }
            """);
    }

    void VerifyDmmRuntime(RuntimePluginContext context, WorkspaceSmokeSession ui, string currentDirectory)
    {
        const string tokenSemantic = "access token";
        var settingsPath = Path.Combine(currentDirectory, "PluginData", "DMM插件", "settings.yaml");
        var settingsBefore = File.ReadAllText(settingsPath);
        var linesBefore = ui.CaptureScreen().ReplaceLineEndings("\n").Split('\n');
        var errorLogsBefore = CountDmmAccessTokenLogs(linesBefore, "ERR", tokenSemantic);
        var infoLogsBefore = CountDmmAccessTokenLogs(linesBefore, "INFO", tokenSemantic);
        var cardsBefore = CountDmmErrorCards(linesBefore);
        var started = context.HostEvents.Subscriptions.Single();
        var ignoreExistingProcess = typeof(DMMPlugin.DMMPlugin).Assembly
            .GetType("DMMPlugin.DMM", throwOnError: true)!
            .GetField("IgnoreExistProcess")
            ?? throw new InvalidOperationException("DMMPlugin IgnoreExistProcess test hook is unavailable.");
        var previousIgnoreExistingProcess = (bool)ignoreExistingProcess.GetValue(null)!;
        try
        {
            ignoreExistingProcess.SetValue(null, true);
            started.Handler(CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            ignoreExistingProcess.SetValue(null, previousIgnoreExistingProcess);
        }

        string[] lines = [];
        for (var attempt = 0; attempt < 50; attempt++)
        {
            ui.Flush();
            lines = ui.CaptureScreen().ReplaceLineEndings("\n").Split('\n');
            if (CountDmmAccessTokenLogs(lines, "ERR", tokenSemantic) == errorLogsBefore + 1
                && CountDmmAccessTokenLogs(lines, "INFO", tokenSemantic) == infoLogsBefore + 1
                && CountDmmErrorCards(lines) == cardsBefore + 1)
            {
                break;
            }
            Thread.Sleep(20);
        }

        var errorLogsAfter = CountDmmAccessTokenLogs(lines, "ERR", tokenSemantic);
        var infoLogsAfter = CountDmmAccessTokenLogs(lines, "INFO", tokenSemantic);
        var cardsAfter = CountDmmErrorCards(lines);
        if (errorLogsAfter != errorLogsBefore + 1
            || infoLogsAfter != infoLogsBefore + 1
            || cardsAfter != cardsBefore + 1)
        {
            var visibleDiagnostics = string.Join(
                " | ",
                lines.Where(line => line.Contains("DMMPlugin", StringComparison.Ordinal)
                    || line.Contains(tokenSemantic, StringComparison.OrdinalIgnoreCase)
                    || line.Contains("ERROR", StringComparison.Ordinal)));
            throw new InvalidOperationException(
                $"{Id}: OnStarted auth failure did not add exactly one access-token ERR log, "
                + $"one access-token INFO log, and one DMM ERROR card "
                + $"(ERR {errorLogsBefore}->{errorLogsAfter}, INFO {infoLogsBefore}->{infoLogsAfter}, "
                + $"cards {cardsBefore}->{cardsAfter}). Visible: {visibleDiagnostics}");
        }
        if (!string.Equals(settingsBefore, File.ReadAllText(settingsPath), StringComparison.Ordinal))
            throw new InvalidOperationException($"{Id}: OnStarted failure changed settings.yaml.");
    }

    static int CountDmmAccessTokenLogs(string[] lines, string severity, string tokenSemantic)
    {
        var overlayColumn = lines
            .Select(line => line.IndexOf("│ ERROR", StringComparison.Ordinal))
            .Where(column => column >= 0)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        return lines.Count(line =>
        {
            var logArea = line[..Math.Min(line.Length, overlayColumn)];
            return logArea.Contains($"{severity} [DMMPlugin]", StringComparison.Ordinal)
                && logArea.Contains(tokenSemantic, StringComparison.OrdinalIgnoreCase);
        });
    }

    static int CountDmmErrorCards(string[] lines)
    {
        var count = 0;
        for (var headerRow = 0; headerRow < lines.Length; headerRow++)
        {
            var cardColumn = lines[headerRow].IndexOf("│ ERROR", StringComparison.Ordinal);
            if (cardColumn < 0)
                continue;

            for (var row = headerRow + 1; row < lines.Length; row++)
            {
                if (lines[row].Length <= cardColumn)
                    continue;
                if (lines[row][cardColumn] == '└')
                    break;
                if (lines[row][cardColumn..].Contains("[DMMPlugin]", StringComparison.Ordinal))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }

    static string CaptureConsole(Action action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Console.SetError(writer);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    static ValueTask DispatchAttributedAnalyzer(IPlugin plugin, object response)
    {
        var responseType = response.GetType();
        var plan = PluginManager.CreateRegistrationPlan(plugin);
        var registration = plan.Analyzers.Single(candidate =>
            candidate.Kind == AnalyzerKind.Response
            && GameEndpointCatalog.ByEndpointType[candidate.EndpointType].ResponseType == responseType);
        var endpoint = GameEndpointCatalog.ByEndpointType[registration.EndpointType];
        var payload = MessagePackSerializer.Serialize(responseType, response);
        return registration.Handler(new(
            endpoint,
            payload,
            new(null, null, null, null, null, null)));
    }

}
