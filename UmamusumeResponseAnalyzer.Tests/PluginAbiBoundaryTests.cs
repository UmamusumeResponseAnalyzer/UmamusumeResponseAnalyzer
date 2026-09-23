using System.Reflection;
using System.Runtime.CompilerServices;
using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    public sealed class PluginAbiBoundaryTests
    {
        const BindingFlags PublicDeclared =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        [Fact]
        public void PluginContractsAndGallopModelsComeFromHostAssembly()
        {
            var hostAssembly = typeof(Server).Assembly;

            Assert.Same(hostAssembly, typeof(IPlugin).Assembly);
            Assert.Same(hostAssembly, typeof(AnalyzerAttribute).Assembly);
            Assert.Same(hostAssembly, typeof(Workspace).Assembly);
            Assert.Same(hostAssembly, typeof(WorkspaceContent).Assembly);
            Assert.Same(hostAssembly, typeof(UiShortcut).Assembly);
            Assert.Same(hostAssembly, typeof(UiSeverity).Assembly);
            Assert.Same(hostAssembly, typeof(TerminalUi).Assembly);
            Assert.Same(hostAssembly, typeof(HotkeyManager).Assembly);
            Assert.Same(hostAssembly, typeof(HotkeyContext).Assembly);
            Assert.Same(hostAssembly, typeof(IGameEndpoint).Assembly);
            Assert.Same(hostAssembly, typeof(DataLinkIndexResponse).Assembly);
            Assert.Null(hostAssembly.GetType("UmamusumeResponseAnalyzer.Game.TurnInfo.SingleModeTurnData"));
            Assert.Contains(hostAssembly.GetTypes(), type =>
                type.Namespace == "Gallop" ||
                type.Namespace?.StartsWith("Gallop.", StringComparison.Ordinal) == true);
        }

        [Fact]
        public void PluginLifecycleManagementIsHostInternal()
        {
            var manager = typeof(PluginManager);
            Assert.True(manager.IsNotPublic);
            foreach (var name in new[] { "LoadPluginsAsync", "UnloadPluginsAsync", "ReloadPluginsAsync" })
                Assert.Null(manager.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        }

        internal const string SyntheticFuturePluginSource = """
                using System;
                using System.Collections.Generic;
                using System.Threading;
                using System.Threading.Tasks;
                using Gallop;
                using Gallop.Endpoints;
                using Terminal.Gui.ViewBase;
                using UmamusumeResponseAnalyzer.Game.TurnInfo;
                using UmamusumeResponseAnalyzer.Plugin;
                using UmamusumeResponseAnalyzer.TerminalGui;

                public sealed record SyntheticExerciseResult(
                    Workspace Workspace, string WorkspaceTitle,
                    string PanelKey, string PanelTitle, string PanelText,
                    string LogText, string NotificationText,
                    bool CanonicalReference, bool CurrentReference,
                    bool SharedPanelRemoved, bool MissingPanelRemoved,
                    bool BackgroundPanelRemoved, bool RecreatedGeneration,
                    bool RecreatedCanonicalReference, bool RecreatedCurrentReference,
                    int TombstoneFailureCount)
                {
                    public string Format() => string.Join(
                        Environment.NewLine,
                        new[]
                        {
                            $"WorkspaceTitlePrefix={WorkspaceTitle.StartsWith("Synthetic Future Workspace ", StringComparison.Ordinal)}",
                            $"CanonicalReference={CanonicalReference}",
                            $"CurrentReference={CurrentReference}",
                            $"SharedPanelRemoved={SharedPanelRemoved}",
                            $"MissingPanelRemoved={MissingPanelRemoved}",
                            $"BackgroundPanelRemoved={BackgroundPanelRemoved}",
                            $"RecreatedGeneration={RecreatedGeneration}",
                            $"RecreatedCanonicalReference={RecreatedCanonicalReference}",
                            $"RecreatedCurrentReference={RecreatedCurrentReference}",
                            $"TombstoneFailureCount={TombstoneFailureCount}"
                        });
                }

                public sealed class SyntheticFuturePlugin : IPlugin
                {
                    public void Initialize(IPluginContext context)
                    {
                        _ = context.Application;
                        context.Analyzers.Register<SingleModeCheckEventResponse>(
                            AnalyzerKind.Response,
                            new[]
                            {
                                EndpointPattern.Exact("/umamusume/single_mode/check_event"),
                                EndpointPattern.Wildcard("/umamusume/single_mode/*/check_event"),
                                EndpointPattern.Regex("^/umamusume/single_mode/(?:arc|legend)/check_event$")
                            },
                            static invocation =>
                            {
                                _ = invocation.Endpoint;
                                _ = invocation.Payload;
                                _ = invocation.Headers;
                                return ValueTask.CompletedTask;
                            });
                        context.Analyzers.Register<ReadOnlyMemory<byte>>(
                            AnalyzerKind.Request,
                            new[] { EndpointPattern.Exact("/umamusume/single_mode/check_event") },
                            static invocation =>
                            {
                                _ = invocation.Endpoint;
                                _ = invocation.Payload;
                                _ = invocation.Headers;
                                return ValueTask.CompletedTask;
                            },
                            priority: -1);
                    }

                    public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

                    [ResponseAnalyzer<GameApi.SingleMode.CheckEvent>]
                    static ValueTask AnalyzeCheckEvent(SingleModeCheckEventResponse payload)
                    {
                        _ = payload;
                        return ValueTask.CompletedTask;
                    }

                    public static async Task<SyntheticExerciseResult> ExerciseAsync()
                    {
                        var chara = new SingleModeChara
                        {
                            support_card_array = [new SingleModeSupportCard { position = 1, support_card_id = 30001 }],
                            evaluation_info_array = [new EvaluationInfo { target_id = 1, evaluation = 80 }]
                        };
                        var turn = new TurnInfo(new SingleModeCheckEventResponse.CommonResponse { chara_info = chara });
                        IReadOnlyDictionary<int, int> supports = turn.SupportCards;
                        IReadOnlyDictionary<int, EvaluationInfo> evaluations = turn.Evaluations;
                        Require(supports[1] == 30001, "TurnInfo support cards must be readable by a compiled plugin.");
                        Require(evaluations[1].evaluation == 80, "TurnInfo evaluations must be readable by a compiled plugin.");

                        const string panelKey = "shared";
                        const string finalPanelTitle = "Second caller final";
                        const string finalPanelText = "second caller final content";
                        const string sharedLogText = "synthetic shared log";
                        const string sharedNotificationText = "synthetic shared notification";
                        var workspaceTitle = $"Synthetic Future Workspace {Guid.NewGuid():N}";
                        var workspace = Workspace.Create(workspaceTitle);
                        var canonical = Workspace.Create(workspaceTitle.ToUpperInvariant());
                        var canonicalReference = ReferenceEquals(workspace, canonical);

                        workspace.SwitchTo();
                        var currentReference = ReferenceEquals(Workspace.Current, workspace);

                        SyntheticPanelWriterA.Write(workspace, panelKey, "First caller", "first caller content");
                        SyntheticPanelWriterB.Write(canonical, panelKey, "Second caller", "second caller content");
                        var removal = SyntheticPanelRemover.Remove(canonical, panelKey);

                        var shortcut = new UiShortcut(ConsoleKey.F19, () => Task.CompletedTask, ConsoleModifiers.Alt);
                        var (shortcutKey, shortcutHandler, shortcutModifiers) = shortcut;
                        Require(shortcutKey == shortcut.Key, "UiShortcut deconstruction must preserve Key.");
                        Require(ReferenceEquals(shortcutHandler, shortcut.Handler), "UiShortcut deconstruction must preserve Handler.");
                        Require(shortcutModifiers == shortcut.Modifiers, "UiShortcut deconstruction must preserve Modifiers.");
                        var equivalentShortcut = new UiShortcut(shortcut.Key, shortcut.Handler, shortcut.Modifiers);
                        Require(shortcut == equivalentShortcut && shortcut.Equals(equivalentShortcut), "UiShortcut record equality must use positional values.");
                        var modifiedShortcut = shortcut with { Modifiers = ConsoleModifiers.Shift };
                        Require(modifiedShortcut.Modifiers == ConsoleModifiers.Shift, "UiShortcut with expression must update positional values.");

                        var directContent = new WorkspaceContent(() => WorkspaceContent.Text("direct").CreateView());
                        Func<View> createView = directContent.CreateView;
                        _ = createView;

                        TerminalUi.Log("Synthetic", "live", UiSeverity.Success);
                        workspace.Notify("live", UiSeverity.Info, TimeSpan.Zero, shortcut);
                        workspace.BindHotkey(ConsoleKey.F20, description: "Synthetic workspace");

                        var backgroundPanelRemoved = await Task.Run(() =>
                        {
                            workspace.SetPanel("background", "Background", WorkspaceContent.Text("background"), fullBleed: true, switchToWorkspace: false);
                            var removed = workspace.RemovePanel("background");
                            TerminalUi.Log("Synthetic", "background", UiSeverity.Trace);
                            workspace.Notify("background", UiSeverity.Warning, TimeSpan.Zero, Array.Empty<UiShortcut>());
                            workspace.SwitchTo();
                            workspace.BindHotkey(ConsoleKey.F21, ConsoleModifiers.Shift, "Synthetic background workspace");
                            return removed;
                        });

                        workspace.Remove();
                        workspace.Remove();

                        var recreated = Workspace.Create(workspaceTitle.ToLowerInvariant());
                        var recreatedGeneration = !ReferenceEquals(workspace, recreated);
                        var recreatedCanonicalReference = ReferenceEquals(recreated, Workspace.Create(workspaceTitle.ToUpperInvariant()));
                        recreated.SwitchTo();
                        var recreatedCurrentReference = ReferenceEquals(Workspace.Current, recreated);
                        var tombstoneFailureCount = VerifyTombstone(workspace);

                        ExerciseHotkeySurface(shortcut);

                        SyntheticPanelWriterA.Write(recreated, panelKey, "First caller final", "first caller final content");
                        SyntheticPanelWriterB.Write(recreated, panelKey, finalPanelTitle, finalPanelText);
                        TerminalUi.Log("Synthetic", sharedLogText, UiSeverity.Success);
                        recreated.Notify(sharedNotificationText, UiSeverity.Info, TimeSpan.FromMinutes(1), shortcut);

                        return new(
                            recreated, workspaceTitle,
                            panelKey, finalPanelTitle, finalPanelText,
                            sharedLogText, sharedNotificationText,
                            canonicalReference, currentReference,
                            removal.Existing, removal.Missing,
                            backgroundPanelRemoved, recreatedGeneration,
                            recreatedCanonicalReference, recreatedCurrentReference,
                            tombstoneFailureCount);
                    }

                    public static void Cleanup(SyntheticExerciseResult result) => result.Workspace.Remove();

                    public static void UnregisterAllInIsolatedChildProcess() => HotkeyManager.UnregisterAll();

                    static void ExerciseHotkeySurface(UiShortcut shortcut)
                    {
                        var owner = new object();
                        var previousDelay = HotkeyManager.PopupAutoCloseDelay;
                        try
                        {
                            Require(
                                !HotkeyManager.Hotkeys.ContainsKey((ConsoleKey.F13, ConsoleModifiers.Alt)) &&
                                !HotkeyManager.Hotkeys.ContainsKey((ConsoleKey.F14, ConsoleModifiers.Alt)) &&
                                !HotkeyManager.Hotkeys.ContainsKey((ConsoleKey.F15, (ConsoleModifiers)0)) &&
                                !HotkeyManager.Hotkeys.ContainsKey((ConsoleKey.F16, (ConsoleModifiers)0)),
                                "Synthetic hotkey keys must be unused.");

                            HotkeyManager.PopupAutoCloseDelay = TimeSpan.FromMilliseconds(10);
                            Func<Task> handler = () => Task.CompletedTask;
                            Func<HotkeyContext, Task> contextHandler = context =>
                            {
                                context.AddLine("handled").BindShortcut(shortcut);
                                return Task.CompletedTask;
                            };

                            using (HotkeyManager.RegisterScope(owner))
                            {
                                HotkeyManager.Register(ConsoleKey.F13, ConsoleModifiers.Alt, "with modifiers", handler);
                                HotkeyManager.Register(ConsoleKey.F14, ConsoleModifiers.Alt, "context with modifiers", contextHandler);
                                HotkeyManager.Register(ConsoleKey.F15, "without modifiers", handler);
                                HotkeyManager.Register(ConsoleKey.F16, "context without modifiers", contextHandler);
                            }

                            var namedTupleObserved = false;
                            foreach (var combo in HotkeyManager.Hotkeys.Keys)
                            {
                                if (combo.Key == ConsoleKey.F13 && combo.Modifiers == ConsoleModifiers.Alt)
                                {
                                    namedTupleObserved = true;
                                    break;
                                }
                            }
                            Require(namedTupleObserved, "Hotkeys keys must expose Key and Modifiers tuple names.");
                            Require(HotkeyManager.FormatKeyCombo(ConsoleKey.F14, ConsoleModifiers.Alt).Length > 0, "FormatKeyCombo must return display text.");

                            var entry = new HotkeyManager.HotkeyEntry("entry", handler, owner);
                            Require(entry.Description == "entry", "HotkeyEntry Description must round-trip.");
                            Require(ReferenceEquals(entry.Handler, handler), "HotkeyEntry Handler must round-trip.");
                            Require(ReferenceEquals(entry.Owner, owner), "HotkeyEntry Owner must round-trip.");

                            var context = new HotkeyContext();
                            context.AddLine().BindShortcut(shortcut);
                            Require(context.LineCount == 1, "HotkeyContext must expose its line count.");
                            Require(HotkeyManager.Unregister(ConsoleKey.F13, ConsoleModifiers.Alt), "Unregister with modifiers must remove its registration.");
                            Require(HotkeyManager.Unregister(ConsoleKey.F15), "Unregister with default modifiers must remove its registration.");
                            Require(HotkeyManager.UnregisterByOwner(owner) == 2, "UnregisterByOwner must remove the remaining scoped registrations.");
                        }
                        finally
                        {
                            HotkeyManager.UnregisterByOwner(owner);
                            HotkeyManager.PopupAutoCloseDelay = previousDelay;
                        }
                    }

                    static int VerifyTombstone(Workspace removed)
                    {
                        var calls = new (string Name, Action Invoke)[]
                        {
                            ("RemovePanel", () => { removed.RemovePanel("missing"); }),
                            ("SetPanel", () => removed.SetPanel("removed", "Removed", WorkspaceContent.Text("removed"))),
                            ("Notify", () => removed.Notify("removed")),
                            ("SwitchTo", removed.SwitchTo),
                            ("BindHotkey", () => removed.BindHotkey(ConsoleKey.F22))
                        };
                        foreach (var call in calls)
                            ExpectRemoved(call.Name, call.Invoke, removed.Title);
                        return calls.Length;
                    }

                    static void ExpectRemoved(string operation, Action action, string title)
                    {
                        try
                        {
                            action();
                        }
                        catch (InvalidOperationException exception)
                            when (exception.Message.Contains(title, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        throw new InvalidOperationException($"{operation} on removed workspace '{title}' did not fail with the required tombstone message.");
                    }

                    static void Require(bool condition, string message)
                    {
                        if (!condition)
                            throw new InvalidOperationException(message);
                    }
                }

                static class SyntheticPanelWriterA
                {
                    public static void Write(Workspace workspace, string key, string title, string text)
                        => workspace.SetPanel(key, title, WorkspaceContent.Text(text), switchToWorkspace: false);
                }

                static class SyntheticPanelWriterB
                {
                    public static void Write(Workspace workspace, string key, string title, string text)
                        => workspace.SetPanel(key, title, WorkspaceContent.Text(text), fullBleed: true, switchToWorkspace: false);
                }

                static class SyntheticPanelRemover
                {
                    public static (bool Existing, bool Missing) Remove(Workspace workspace, string key)
                        => (workspace.RemovePanel(key), workspace.RemovePanel(key));
                }
                """;

        [Fact]
        public async Task SyntheticFuturePluginLoadsAndExercisesTargetAbi()
        {
            const string scenario = "synthetic-abi";
            if (TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                TerminalUiLifecycleChildProcess.WriteResult(RunSyntheticFuturePlugin());
                return;
            }

            var formatted = await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(PluginAbiBoundaryTests),
                nameof(SyntheticFuturePluginLoadsAndExercisesTargetAbi));
            Assert.Equal(
                string.Join(
                    Environment.NewLine,
                    [
                        "WorkspaceTitlePrefix=True",
                        "CanonicalReference=True",
                        "CurrentReference=True",
                        "SharedPanelRemoved=True",
                        "MissingPanelRemoved=False",
                        "BackgroundPanelRemoved=True",
                        "RecreatedGeneration=True",
                        "RecreatedCanonicalReference=True",
                        "RecreatedCurrentReference=True",
                        "TombstoneFailureCount=5",
                        "RuntimePanelObserved=True",
                        "RuntimeLogObserved=True",
                        "RuntimeNotificationObserved=True"
                    ]),
                formatted);
        }

        internal static string RunSyntheticFuturePlugin()
        {
            var dllPath = Path.Combine(
                Path.GetTempPath(),
                $"ura-synthetic-future-plugin-{Guid.NewGuid():N}.dll");
            using var terminal = new TerminalGuiTestApp();
            var host = TerminalUiLifecycleChildProcess.InitializeHost(
                terminal,
                CancellationToken.None);
            var run = terminal.StartAsync(host).GetAwaiter().GetResult();
            var baseline = Workspace.Create("M6 synthetic baseline");
            baseline.SwitchTo();
            host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var originalCurrent = Workspace.Current;
            Assert.Same(baseline, originalCurrent);

            Assembly? assembly = null;
            object? result = null;
            Workspace? exercisedWorkspace = null;
            string? formatted = null;
            List<UiLogLine> logs = [];
            void ObserveLog(UiLogLine line) => logs.Add(line);
            host.LogAdded += ObserveLog;
            try
            {
                PluginCompiler.Compile(
                    SyntheticFuturePluginSource,
                    "SyntheticFuturePlugin",
                    dllPath);
                assembly = Assembly.Load(File.ReadAllBytes(dllPath));
                var pluginType = assembly.GetType("SyntheticFuturePlugin", throwOnError: true)!;
                var exercise = pluginType.GetMethod(
                    "ExerciseAsync",
                    BindingFlags.Public | BindingFlags.Static)!;
                var task = Assert.IsAssignableFrom<Task>(exercise.Invoke(null, null));

                task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                result = exercise.ReturnType.GetProperty("Result")!.GetValue(task);
                Assert.NotNull(result);
                host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                exercisedWorkspace = Assert.IsType<Workspace>(
                    result.GetType().GetProperty("Workspace")!.GetValue(result));
                formatted = Assert.IsType<string>(
                    result.GetType().GetMethod("Format", PublicDeclared)!.Invoke(result, null));
                var panelText = Assert.IsType<string>(
                    result.GetType().GetProperty("PanelText")!.GetValue(result));
                var logText = Assert.IsType<string>(
                    result.GetType().GetProperty("LogText")!.GetValue(result));
                var notificationText = Assert.IsType<string>(
                    result.GetType().GetProperty("NotificationText")!.GetValue(result));

                var logObserved = logs.Any(line =>
                    line.Text == $"[Synthetic] {logText}" &&
                    line.Severity == UiSeverity.Success);

                exercisedWorkspace.SwitchTo();
                host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                terminal.RedrawAsync().GetAwaiter().GetResult();
                terminal.WaitForScreenAsync(notificationText).GetAwaiter().GetResult();
                var screen = terminal.CaptureScreenAsync().GetAwaiter().GetResult();
                var notificationObserved = screen.Contains(notificationText, StringComparison.Ordinal);
                var panelObserved =
                    screen.Contains(panelText, StringComparison.Ordinal) &&
                    !screen.Contains("first caller final content", StringComparison.Ordinal);

                Assert.True(panelObserved);
                Assert.True(logObserved);
                Assert.True(notificationObserved);
                formatted = string.Join(
                    Environment.NewLine,
                    [
                        formatted,
                        $"RuntimePanelObserved={panelObserved}",
                        $"RuntimeLogObserved={logObserved}",
                        $"RuntimeNotificationObserved={notificationObserved}"
                    ]);
            }
            finally
            {
                host.LogAdded -= ObserveLog;
                try
                {
                    if (assembly is not null && result is not null)
                    {
                        assembly.GetType("SyntheticFuturePlugin", throwOnError: true)!
                            .GetMethod("Cleanup", BindingFlags.Public | BindingFlags.Static)!
                            .Invoke(null, [result]);
                        if (!ReferenceEquals(originalCurrent, exercisedWorkspace))
                        {
                            originalCurrent.SwitchTo();
                        }
                        host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                        Assert.Same(originalCurrent, Workspace.Current);
                    }
                }
                finally
                {
                    try
                    {
                        File.Delete(dllPath);
                    }
                    finally
                    {
                        terminal.StopAsync(host, run).GetAwaiter().GetResult();
                    }
                }
            }

            Assert.NotNull(formatted);
            return formatted;
        }

        [Fact]
        public void GeneratedGallopSourcesAreSourceOnlyAtOutputRoot()
        {
            var repositoryRoot = FindRepositoryRoot();
            var gallopRoot = Path.Combine(repositoryRoot.FullName, "UmamusumeResponseAnalyzer", "Gallop");

            Assert.True(Directory.Exists(gallopRoot), $"找不到 Gallop generated source directory: {gallopRoot}");
            Assert.False(File.Exists(Path.Combine(gallopRoot, "_generated.txt")), "Gallop generated sources should not include _generated.txt.");
            Assert.False(Directory.Exists(Path.Combine(gallopRoot, "Gallop")), "Gallop generated sources should be directly under the Gallop output directory.");
            Assert.True(File.Exists(Path.Combine(gallopRoot, "RequestBase.cs")), "Gallop DTO sources should be directly under the Gallop output directory.");
            Assert.True(File.Exists(Path.Combine(gallopRoot, "Endpoints", "GameApi.g.cs")), "Gallop endpoint sources should be directly under Gallop/Endpoints.");
        }

        static DirectoryInfo FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
        {
            var repositoryRoot = new FileInfo(sourceFilePath).Directory?.Parent;
            if (repositoryRoot is not null &&
                File.Exists(Path.Combine(repositoryRoot.FullName, "UmamusumeResponseAnalyzer.sln")))
                return repositoryRoot;

            throw new InvalidOperationException(
                $"编译期 source anchor 不在 UmamusumeResponseAnalyzer repository 中：{sourceFilePath}");
        }
    }
}
