using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;
using UmamusumeResponseAnalyzer.LiveDisplay;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("KeyboardManager")]
    public sealed class LiveDisplayRenderTests : IDisposable
    {
        public LiveDisplayRenderTests() => ResetKeyboardManager();

        public void Dispose() => ResetKeyboardManager();

        static void ResetKeyboardManager()
        {
            using (KeyboardManager.SuspendInput())
            {
            }

            KeyboardManager.UnregisterAll();
            KeyboardManager.OverlaySink = null;
            KeyboardManager.PopupAutoCloseDelay = TimeSpan.FromSeconds(3);
            LiveDisplayConsole.UnbindForTests();
        }

        static LiveDisplayWorkspace Workspace(string id = "dynamic", string title = "动态") => LiveDisplayWorkspace.Create(id, title);

        [Fact]
        public void RenderSnapshot_InitialState_RendersEmpty()
        {
            var uiHost = new UiHost();

            var output = Render(uiHost);

            // 无 workspace 且无日志时，不渲染任何面板，输出应为空。
            Assert.DoesNotContain("Logs", output);
            Assert.DoesNotContain("运行状态", output);
        }

        [Fact]
        public void RenderSnapshot_FirstDynamicWorkspaceBecomesActive()
        {
            var uiHost = new UiHost();
            var workspace = Workspace();
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "DynamicPlugin",
                "main",
                "动态面板",
                new Panel("DynamicBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));

            var output = Render(uiHost);

            Assert.Contains("DynamicBody", output);
        }

        [Fact]
        public async Task PluginOutput_BindWorkspaceHotkey_AllowsF1AndSwitchesWorkspace()
        {
            var uiHost = new UiHost();
            var output = uiHost.ForPlugin("DynamicPlugin");
            var first = output.CreateWorkspace("first", "第一");
            var second = output.CreateWorkspace("second", "第二");
            output.SetPanel(first, "main", "第一面板", new Panel("FirstBody").Expand(), fullBleed: true);
            output.SetPanel(second, "main", "第二面板", new Panel("SecondBody").Expand(), fullBleed: true);
            output.BindWorkspaceHotkey(second, ConsoleKey.F1);

            await KeyboardManager.HandleKeyAsync(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: false, control: false));
            var rendered = Render(uiHost);

            Assert.Contains("SecondBody", rendered);
        }

        [Fact]
        public void PluginOutput_WorkspaceIdsAreCaseSensitive()
        {
            var uiHost = new UiHost();
            var output = uiHost.ForPlugin("DynamicPlugin");
            var upper = output.CreateWorkspace("Main", "大写");
            var lower = output.CreateWorkspace("main", "小写");
            output.SetPanel(upper, "main", "大写面板", new Panel("UpperBody").Expand(), fullBleed: true);
            output.SetPanel(lower, "main", "小写面板", new Panel("LowerBody").Expand(), fullBleed: true);

            output.SwitchWorkspace(upper);
            var upperOutput = Render(uiHost);
            output.SwitchWorkspace(lower);
            var lowerOutput = Render(uiHost);

            Assert.Contains("UpperBody", upperOutput);
            Assert.DoesNotContain("LowerBody", upperOutput);
            Assert.Contains("LowerBody", lowerOutput);
            Assert.DoesNotContain("UpperBody", lowerOutput);
        }

        [Fact]
        public void PluginOutput_WorkspaceIdsDoNotCollideWhenPluginIdContainsColon()
        {
            var uiHost = new UiHost();
            var firstOutput = uiHost.ForPlugin("a");
            var secondOutput = uiHost.ForPlugin("a:b");
            var first = firstOutput.CreateWorkspace("b:c", "第一");
            var second = secondOutput.CreateWorkspace("c", "第二");
            firstOutput.SetPanel(first, "main", "第一面板", new Panel("FirstColonBody").Expand(), fullBleed: true);
            secondOutput.SetPanel(second, "main", "第二面板", new Panel("SecondColonBody").Expand(), fullBleed: true);

            firstOutput.SwitchWorkspace(first);
            var firstRendered = Render(uiHost);
            secondOutput.SwitchWorkspace(second);
            var secondRendered = Render(uiHost);

            Assert.Contains("FirstColonBody", firstRendered);
            Assert.DoesNotContain("SecondColonBody", firstRendered);
            Assert.Contains("SecondColonBody", secondRendered);
            Assert.DoesNotContain("FirstColonBody", secondRendered);
        }

        [Fact]
        public void RenderSnapshot_RendersFullBleedWorkspaceBody()
        {
            var uiHost = new UiHost();
            uiHost.SetPanel(new LiveDisplayPanel(
                Workspace(),
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("FullBleedMarker").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));

            var output = Render(uiHost);

            Assert.Contains("FullBleedMarker", output);
            Assert.DoesNotContain("ScenarioAnalyzer - 整页布局", output);
        }

        [Fact]
        public void NotificationPopup_ShowsMoreThanTwoNotifications()
        {
            var uiHost = new UiHost();
            var workspace = Workspace();
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("NotificationBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));

            for (var i = 1; i <= 4; i++)
            {
                uiHost.Notify(new LiveDisplayNotification(
                    workspace,
                    "ScenarioAnalyzer",
                    $"消息 {i}",
                    LiveDisplaySeverity.Info,
                    DateTimeOffset.Now.AddSeconds(10 + i)));
            }

            var popup = string.Join(Environment.NewLine, uiHost.BuildNotificationPopupPreview(120));
            var output = Render(uiHost);

            Assert.Contains("NotificationBody", output);
            Assert.Contains("消息 1", output);
            Assert.Contains("消息 4", output);
            Assert.Contains("消息 1", popup);
            Assert.Contains("消息 4", popup);
        }

        [Fact]
        public void NotificationPopup_SummarizesNotificationOverflow()
        {
            var uiHost = new UiHost();
            var workspace = Workspace();
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("NotificationBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));

            for (var i = 1; i <= 7; i++)
            {
                uiHost.Notify(new LiveDisplayNotification(
                    workspace,
                    "ScenarioAnalyzer",
                    $"消息 {i}",
                    LiveDisplaySeverity.Info,
                    DateTimeOffset.Now.AddSeconds(10 + i)));
            }

            var popup = string.Join(Environment.NewLine, uiHost.BuildNotificationPopupPreview(120));
            var output = Render(uiHost);

            Assert.Contains("消息 7", output);
            Assert.Contains("消息 4", output);
            Assert.Contains("还有 3 条通知", output);
            Assert.Contains("还有 3 条通知", popup);
        }

        [Fact]
        public void NotificationPopup_IsSharedAcrossWorkspaces()
        {
            var uiHost = new UiHost();
            var first = Workspace("first", "第一");
            var second = Workspace("second", "第二");
            uiHost.BindWorkspaceHotkey(second, ConsoleKey.F2);
            uiHost.SetPanel(new LiveDisplayPanel(
                first,
                "FirstPlugin",
                "main",
                "第一",
                new Panel("FirstBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));
            uiHost.SetPanel(new LiveDisplayPanel(
                second,
                "SecondPlugin",
                "main",
                "第二",
                new Panel("SecondBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));
            uiHost.Notify(new LiveDisplayNotification(
                second,
                "Notifier",
                "跨 workspace 通知",
                LiveDisplaySeverity.Info,
                DateTimeOffset.Now.AddSeconds(10)));
            uiHost.SwitchWorkspace(first);

            var firstOutput = Render(uiHost);
            uiHost.SwitchWorkspace(second);
            var secondOutput = Render(uiHost);

            Assert.Contains("FirstBody", firstOutput);
            Assert.Contains("跨 workspace 通知", firstOutput);
            Assert.Contains("F2 第二", firstOutput);
            Assert.Contains("SecondBody", secondOutput);
            Assert.Contains("跨 workspace 通知", secondOutput);
        }

        [Fact]
        public void GlobalNotificationAndLog_RenderWithoutWorkspace()
        {
            var uiHost = new UiHost();
            uiHost.Notify(new LiveDisplayNotification(
                null,
                "URA",
                "GNOTICE",
                LiveDisplaySeverity.Warning,
                DateTimeOffset.Now.AddSeconds(10)));
            uiHost.Log(new LiveDisplayLogLine(
                null,
                "URA",
                "GLOG",
                LiveDisplaySeverity.Warning,
                IsMarkup: false,
                DateTimeOffset.Now));

            var output = Render(uiHost, width: 120, height: 35);

            Assert.Contains("GNOTICE", output);
            Assert.Contains("GLOG", output);
            Assert.Contains("GNOTICE", string.Join(Environment.NewLine, uiHost.BuildNotificationPopupPreview(120)));
        }

        [Fact]
        public void LiveDisplayConsole_GlobalHostStatus_RenderWithoutWorkspace()
        {
            var uiHost = new UiHost();
            LiveDisplayConsole.Bind(uiHost);
            try
            {
                LiveDisplayConsole.Log("URA", "插件 ScenarioAnalyzer v1.0.0 加载成功", LiveDisplaySeverity.Success);
                LiveDisplayConsole.Log("Plugin", "已加载 1 个插件。按 P 查看插件列表。", LiveDisplaySeverity.Success);
                LiveDisplayConsole.Log("Server", "监听 http://127.0.0.1:4693", LiveDisplaySeverity.Success);
                LiveDisplayConsole.Log("URA", "可尝试通过http://127.0.0.1:4693连接");
                LiveDisplayConsole.Notify("URA", "插件 BrokenPlugin 加载失败", LiveDisplaySeverity.Warning, TimeSpan.FromSeconds(10));

                var output = Render(uiHost, width: 120, height: 35);
                var popup = string.Join(Environment.NewLine, uiHost.BuildNotificationPopupPreview(120));

                Assert.Contains("已加载 1 个插件", output);
                Assert.Contains("监听 http://127.0.0.1:4693", output);
                Assert.Single(Regex.Matches(output, Regex.Escape("监听 http://127.0.0.1:4693")).Cast<Match>());
                Assert.Contains("ScenarioAnalyzer", output);
                Assert.Contains("127.0.0.1:4693", output);
                Assert.Contains("BrokenPlugin", output);
                Assert.Contains("BrokenPlugin", popup);
            }
            finally
            {
                LiveDisplayConsole.Unbind(uiHost);
            }
        }

        [Fact]
        public void LiveDisplayConsole_LogException_RendersMessagesWithoutStackTrace()
        {
            var uiHost = new UiHost();
            LiveDisplayConsole.Bind(uiHost);
            try
            {
                LiveDisplayConsole.LogException("Plugin", BuildNestedLogException());

                var output = Render(uiHost, width: 120, height: 35);

                Assert.Contains("outer failure", output);
                Assert.Contains("phase=初始化", output);
                Assert.Contains("inner failure", output);
                Assert.DoesNotContain("path=", output);
                Assert.DoesNotContain(@"C:\secret", output);
                Assert.DoesNotContain("One or more errors occurred", output);
                Assert.DoesNotContain("System.InvalidOperationException", output);
                Assert.DoesNotContain(nameof(ThrowLogExceptionInner), output);
                Assert.DoesNotContain("   at ", output);
            }
            finally
            {
                LiveDisplayConsole.Unbind(uiHost);
            }
        }

        [Fact]
        public void LiveDisplayConsole_LogException_SimplifiesPluginInitializationFailure()
        {
            var uiHost = new UiHost();
            LiveDisplayConsole.Bind(uiHost);
            try
            {
                var exception = BuildPluginInitializationException();
                var message = LiveDisplayConsole.FormatExceptionLogMessage(exception);
                Assert.Equal(
                    "WinSaddleAnalyzer初始化失败：SkillEffectPlugin 尚未配置 Race/RunningStyle，无法为 WinSaddleAnalyzer 计算技能期望收益。",
                    message);

                LiveDisplayConsole.LogException("Plugin", exception);
                var output = Render(uiHost, width: 120, height: 35);

                Assert.Contains("WinSaddleAnalyzer初始化失败：SkillEffectPlugin 尚未配置", output);
                Assert.Contains("Race/RunningStyle", output);
                Assert.Contains("无法为", output);
                Assert.Contains("计算", output);
                Assert.Contains("收益", output);
                Assert.DoesNotContain("插件初始化失败:", output);
                Assert.DoesNotContain("plugin=", output);
                Assert.DoesNotContain("配置文件", output);
                Assert.DoesNotContain("settings.json", output);
            }
            finally
            {
                LiveDisplayConsole.Unbind(uiHost);
            }
        }

        [Fact]
        public void RenderSnapshot_LogRowsOmitTimestamp()
        {
            var uiHost = new UiHost();
            var workspace = Workspace();
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("WorkspaceBody").Expand(),
                new DateTimeOffset(2026, 7, 2, 20, 9, 22, TimeSpan.Zero),
                FullBleed: false));
            uiHost.Log(new LiveDisplayLogLine(
                workspace,
                "Plugin",
                "FixedLog",
                LiveDisplaySeverity.Warning,
                IsMarkup: false,
                new DateTimeOffset(2026, 7, 2, 20, 9, 22, TimeSpan.Zero)));

            var output = Render(uiHost, width: 120, height: 35);

            Assert.Contains("WARN [Plugin] FixedLog", output);
            Assert.DoesNotContain("20:09:22", output);
        }

        [Fact]
        public void RenderSnapshot_LogRowsKeepPrefixWithMessageStartOnSameLine()
        {
            var uiHost = new UiHost();
            var workspace = Workspace();
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("WorkspaceBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: false));
            uiHost.Log(new LiveDisplayLogLine(
                workspace,
                "Plugin",
                "WinSaddleAnalyzer初始化失败：SkillEffectPlugin 尚未配置 Race/RunningStyle，无法为 WinSaddleAnalyzer 计算技能期望收益。",
                LiveDisplaySeverity.Error,
                IsMarkup: false,
                DateTimeOffset.Now));

            var output = Render(uiHost, width: 120, height: 35);
            var normalizedOutput = output.Replace('\u00A0', ' ');
            var lines = normalizedOutput.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(StripAnsi)
                .ToArray();

            Assert.Contains("ERR [Plugin] WinSaddleAnalyzer初始化失败", normalizedOutput);
            Assert.DoesNotContain(lines, x => Regex.IsMatch(x, @"ERR \[Plugin\]\s*│"));
            Assert.DoesNotContain(lines, x => Regex.IsMatch(x, @"\s{10,}illEffectPlugin"));
        }

        [Fact]
        public void BootstrapWorkspace_RendersSettingsPhasesAndDiagnostics()
        {
            var uiHost = new UiHost();
            var bootstrap = new BootstrapWorkspace(uiHost);
            LiveDisplayConsole.Bind(uiHost);
            LiveDisplayConsole.DefaultLogWorkspace = bootstrap.Workspace;
            try
            {
                bootstrap.SetSettings(
                [
                    ("工作目录", @"K:\ura"),
                    ("监听", "http://127.0.0.1:4693"),
                    ("服务器目标", "Cygames")
                ]);
                bootstrap.SetPhase("config", "配置", LiveDisplaySeverity.Success, "已读取 config.yaml");
                LiveDisplayConsole.MarkupLog("Database", "[yellow]names.br 不存在，请更新数据文件。[/]", LiveDisplaySeverity.Warning);

                var output = Render(uiHost, width: 120, height: 35);

                Assert.Contains("启动状态", output);
                Assert.Contains("工作目录", output);
                Assert.Contains(@"K:\ura", output);
                Assert.Contains("配置", output);
                Assert.Contains("已读取 config.yaml", output);
                Assert.Contains("names.br", output);
            }
            finally
            {
                LiveDisplayConsole.DefaultLogWorkspace = null;
                LiveDisplayConsole.Unbind(uiHost);
            }
        }

        [Fact]
        public async Task LiveDisplayConsole_RunAsync_BeforeUiHostRuns_KeepsConsoleInteractionOutput()
        {
            var uiHost = new UiHost();
            var bootstrap = new BootstrapWorkspace(uiHost);
            var recording = new StringWriter();
            var originalConsole = AnsiConsole.Console;
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new FixedSizeConsoleOutput(recording, 120, 35) });
            LiveDisplayConsole.Bind(uiHost);
            LiveDisplayConsole.DefaultLogWorkspace = bootstrap.Workspace;
            try
            {
                await LiveDisplayConsole.RunAsync(() =>
                {
                    LiveDisplayConsole.MarkupLog("Plugin", "[yellow]MENU_DIRECT[/]", LiveDisplaySeverity.Warning);
                    return Task.CompletedTask;
                });

                var consoleOutput = recording.ToString();
                var liveOutput = Render(uiHost, width: 120, height: 35);

                Assert.Contains("MENU_DIRECT", consoleOutput);
                Assert.DoesNotContain("MENU_DIRECT", liveOutput);
            }
            finally
            {
                AnsiConsole.Console = originalConsole;
                LiveDisplayConsole.DefaultLogWorkspace = null;
                LiveDisplayConsole.Unbind(uiHost);
            }
        }

        [Fact]
        public void LiveDisplayNotification_DefaultTtl_KeepsErrorsVisibleLonger()
        {
            Assert.Equal(TimeSpan.FromSeconds(5), LiveDisplayNotification.DefaultTtl(LiveDisplaySeverity.Info));
            Assert.Equal(TimeSpan.FromSeconds(5), LiveDisplayNotification.DefaultTtl(LiveDisplaySeverity.Success));
            Assert.Equal(TimeSpan.FromSeconds(10), LiveDisplayNotification.DefaultTtl(LiveDisplaySeverity.Warning));
            Assert.Equal(TimeSpan.FromSeconds(10), LiveDisplayNotification.DefaultTtl(LiveDisplaySeverity.Error));
        }

        [Fact]
        public void NotificationPopup_PreservesOuterPanelBorder()
        {
            var uiHost = new UiHost();
            var workspace = Workspace();
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("BorderBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));
            uiHost.Notify(new LiveDisplayNotification(
                workspace,
                "ScenarioAnalyzer",
                "边框保护",
                LiveDisplaySeverity.Info,
                DateTimeOffset.Now.AddSeconds(10)));

            var output = Render(uiHost, width: 120, height: 35);
            var lines = output.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(x => StripAnsi(x).TrimEnd())
                .ToArray();
            var popupLine = lines.First(x => x.Contains("边框保护"));

            Assert.StartsWith("┌", lines[0]);
            Assert.EndsWith("┐", lines[0]);
            Assert.EndsWith("│", popupLine);
        }

        [Fact]
        public void KeyboardPopupOverlay_RendersPopupWithoutLayoutPressure()
        {
            var popup = new KeyboardHandlerContext()
                .WriteLine("第一行")
                .WriteLine("第二行")
                .ToPopup();

            var output = Render(new KeyboardPopupOverlayRenderable(
                new Panel("PopupBase").Expand(),
                popup,
                80,
                12,
                bottomInset: 1), width: 80, height: 12);

            Assert.Contains("PopupBase", output);
            Assert.Contains("第一行", output);
            Assert.Contains("第二行", output);
        }

        [Fact]
        public void KeyboardPopupOverlay_RendersAutoCloseCountdown()
        {
            var popup = new KeyboardHandlerContext()
                .WriteLine("倒计时")
                .ToPopup() with { ExpiresAt = DateTimeOffset.Now.AddSeconds(5) };

            var output = Render(new KeyboardPopupOverlayRenderable(
                new Panel("PopupBase").Expand(),
                popup,
                80,
                12,
                bottomInset: 0), width: 80, height: 12);
            var lines = output.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(x => StripAnsi(x).TrimEnd())
                .ToArray();
            var countdownLine = Assert.Single(lines, x => Regex.IsMatch(x, @"\b\d+s\b"));

            Assert.Contains("倒计时", output);
            Assert.StartsWith("└", countdownLine);
        }

        [Fact]
        public void KeyboardPopupOverlay_RendersMarkupLineAsStyledText()
        {
            var popup = new KeyboardHandlerContext()
                .MarkupLine("[green]MarkupStyle[/]")
                .ToPopup();
            var renderable = new KeyboardPopupOverlayRenderable(
                new Text("PopupBase"),
                popup,
                80,
                8,
                bottomInset: 0);

            var output = Render(renderable, width: 80, height: 8);
            var segments = RenderSegments(renderable, width: 80, height: 8);
            var styledSegment = Assert.Single(segments, x => x.Text.Contains("MarkupStyle"));

            Assert.Contains("MarkupStyle", output);
            Assert.DoesNotContain("[green]", output);
            Assert.NotEqual(Style.Plain, styledSegment.Style);
        }

        [Fact]
        public void KeyboardPopupOverlay_FallsBackToPlainTextForInvalidMarkup()
        {
            var popup = new KeyboardHandlerContext()
                .MarkupLine("[not-a-style]BrokenMarkup[/]")
                .ToPopup();

            var output = Render(new KeyboardPopupOverlayRenderable(
                new Text("PopupBase"),
                popup,
                80,
                8,
                bottomInset: 0), width: 80, height: 8);

            Assert.Contains("[not-a-style]BrokenMarkup[/]", output);
        }

        [Fact]
        public void KeyboardPopupOverlay_DoesNotSplitEmojiWhenTrimmingText()
        {
            var trimmed = CellText.TrimToCellWidth("😀abcdef", 5);

            Assert.StartsWith("😀", trimmed);
            Assert.Equal("😀...", trimmed);
        }

        [Fact]
        public void UiHost_RendersKeyboardPopup()
        {
            var uiHost = new UiHost();
            uiHost.SetPanel(new LiveDisplayPanel(
                Workspace(),
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("KeyboardOverlayBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));
            ((IKeyboardOverlaySink)uiHost).ShowPopup(new KeyboardHandlerContext()
                .WriteLine("插件列表")
                .WriteLine("ScenarioAnalyzer v1.0.0")
                .ToPopup());

            var output = Render(uiHost, width: 80, height: 16);

            Assert.Contains("KeyboardOverlayBody", output);
            Assert.Contains("插件列表", output);
            Assert.Contains("ScenarioAnalyzer v1.0.0", output);
        }

        [Fact]
        public void UiHost_RendersKeyboardCommandInput()
        {
            var uiHost = new UiHost();
            uiHost.SetPanel(new LiveDisplayPanel(
                Workspace(),
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("CommandInputBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));
            ((IKeyboardOverlaySink)uiHost).ShowCommandInput(new KeyboardCommandInput("/status"));

            var output = Render(uiHost, width: 80, height: 16);

            Assert.Contains("CommandInputBody", output);
            Assert.Contains("> /status", output);
            Assert.Contains("ESC 取消", output);
        }

        [Fact]
        public async Task LiveDisplayConsole_RunAsync_ExecutesThroughRunningUiHost()
        {
            var uiHost = new UiHost();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            LiveDisplayConsole.Bind(uiHost);
            var runTask = uiHost.RunAsync(cts.Token);

            try
            {
                while (!uiHost.IsRunning)
                    await Task.Delay(10, cts.Token);

                var executed = false;
                await LiveDisplayConsole.RunAsync(() =>
                {
                    executed = true;
                    return Task.CompletedTask;
                });

                Assert.True(executed);
            }
            finally
            {
                uiHost.RequestShutdown();
                cts.Cancel();
                LiveDisplayConsole.Unbind(uiHost);
                await IgnoreCancellationAsync(runTask);
            }
        }

        [Fact]
        public void RenderSnapshot_RendersMultipleNonFullBleedPanelsWithLogs()
        {
            var uiHost = new UiHost();
            var workspace = Workspace("panel-lab", "Panel Lab");
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "PanelLabA",
                "summary",
                "摘要",
                new Markup("StableSummary"),
                DateTimeOffset.Now));
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "PanelLabB",
                "detail",
                "详情",
                new Markup("DynamicDetail"),
                DateTimeOffset.Now));
            uiHost.Log(new LiveDisplayLogLine(
                workspace,
                "PanelLabA",
                "PanelLabLog",
                LiveDisplaySeverity.Info,
                IsMarkup: false,
                DateTimeOffset.Now));

            var output = Render(uiHost, width: 120, height: 35);

            Assert.Contains("PanelLabA", output);
            Assert.Contains("PanelLabB", output);
            Assert.Contains("StableSummary", output);
            Assert.Contains("DynamicDetail", output);
            Assert.Contains("PanelLabLog", output);
        }

        [Fact]
        public void RenderSnapshot_FallsBackToPlainTextForInvalidMarkupLog()
        {
            var uiHost = new UiHost();
            var workspace = Workspace("panel-lab", "Panel Lab");
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "A",
                "summary",
                "摘要",
                new Markup("StableSummary"),
                DateTimeOffset.Now));
            uiHost.Log(new LiveDisplayLogLine(
                workspace,
                "A",
                "[InvalidStyle]B[/]",
                LiveDisplaySeverity.Info,
                IsMarkup: true,
                DateTimeOffset.Now));

            var output = Render(uiHost, width: 120, height: 35);

            Assert.Contains("[InvalidStyle]B[/]", output);
        }

        [Fact]
        public void RenderSnapshot_UsesConsoleProfileWidthForPopupAvailability()
        {
            var uiHost = new UiHost();
            var workspace = Workspace();
            uiHost.SetPanel(new LiveDisplayPanel(
                workspace,
                "ScenarioAnalyzer",
                "workspace",
                "整页布局",
                new Panel("NarrowBody").Expand(),
                DateTimeOffset.Now,
                FullBleed: true));
            uiHost.Notify(new LiveDisplayNotification(
                workspace,
                "ScenarioAnalyzer",
                "窄窗口不显示 popup",
                LiveDisplaySeverity.Info,
                DateTimeOffset.Now.AddSeconds(10)));

            var output = Render(uiHost, width: 60, height: 20);

            Assert.Contains("NarrowBody", output);
            Assert.DoesNotContain("窄窗口不显示 popup", output);
        }

        static string Render(UiHost uiHost, int width = 120, int height = 35)
        {
            var recording = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new FixedSizeConsoleOutput(recording, width, height) });
            uiHost.RenderSnapshot(console);
            return recording.ToString();
        }

        static string Render(IRenderable renderable, int width = 120, int height = 35, bool isTerminal = false)
        {
            var recording = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new FixedSizeConsoleOutput(recording, width, height, isTerminal),
                ColorSystem = isTerminal ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors
            });
            console.Write(renderable);
            return recording.ToString();
        }

        static Exception BuildNestedLogException()
        {
            try
            {
                ThrowLogExceptionInner();
                throw new InvalidOperationException("unreachable");
            }
            catch (Exception ex)
            {
                return new InvalidOperationException(
                    @"outer failure, path=C:\secret\Broken.zip|Broken.dll, phase=初始化",
                    new AggregateException(ex));
            }
        }

        static Exception BuildPluginInitializationException()
        {
            return new InvalidOperationException(
                "插件初始化失败: plugin=WinSaddleAnalyzer (WinSaddleAnalyzer)",
                new AggregateException(new InvalidOperationException(
                    "SkillEffectPlugin 尚未配置 Race/RunningStyle，无法为 WinSaddleAnalyzer 计算技能期望收益。配置文件: PluginData\\SkillEffectPlugin\\settings.json")));
        }

        static void ThrowLogExceptionInner()
        {
            throw new InvalidOperationException("inner failure");
        }

        static IReadOnlyList<Segment> RenderSegments(IRenderable renderable, int width, int height)
        {
            var recording = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new FixedSizeConsoleOutput(recording, width, height, isTerminal: true),
                ColorSystem = ColorSystemSupport.TrueColor
            });
            var options = RenderOptions.Create(console, console.Profile.Capabilities);
            return renderable.Render(options, width).ToArray();
        }

        static string StripAnsi(string value) => Regex.Replace(value, @"\x1B\[[0-9;]*[A-Za-z]", "");

        sealed class FixedSizeConsoleOutput(TextWriter writer, int width, int height, bool isTerminal = false) : IAnsiConsoleOutput
        {
            public TextWriter Writer { get; } = writer;
            public bool IsTerminal => isTerminal;
            public int Width { get; } = width;
            public int Height { get; } = height;

            public void SetEncoding(Encoding encoding)
            {
            }
        }

        static async Task IgnoreCancellationAsync(Task task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
    }
}
