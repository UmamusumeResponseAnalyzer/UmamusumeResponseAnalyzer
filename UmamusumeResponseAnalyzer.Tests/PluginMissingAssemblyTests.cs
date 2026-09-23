using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Terminal.Gui.Input;
using Terminal.Gui.Drivers;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginRuntime")]
public sealed class PluginMissingAssemblyTests : IDisposable
{
    const string AccountIndexPath = "/umamusume/account/index";
    readonly string originalDirectory = Directory.GetCurrentDirectory();
    readonly string directory = Path.Combine(Path.GetTempPath(), $"ura-missing-assembly-{Guid.NewGuid():N}");
    readonly string dependency;
    readonly TerminalGuiTestApp terminal;

    public PluginMissingAssemblyTests(PluginRuntimeFixture runtime)
    {
        terminal = runtime.Terminal;
        PluginManager.ShutdownAsync().GetAwaiter().GetResult();
        HotkeyManager.OverlaySink = runtime.Host;
        Directory.CreateDirectory(Path.Combine(directory, "Plugins"));
        Directory.SetCurrentDirectory(directory);
        dependency = Path.Combine(directory, "AbsentLibrary.dll");
        PluginCompiler.Compile("public static class AbsentLibrary { public static void Touch() { } }",
            "AbsentLibrary", dependency);
        CreatePlugin("Healthy", """
            context.Events.OnStarted(token => { File.WriteAllText("healthy.started", "yes"); return ValueTask.CompletedTask; });
            context.Analyzers.Register<ReadOnlyMemory<byte>>(AnalyzerKind.Response,
                [EndpointPattern.Exact("/umamusume/account/index")], invocation =>
                { lock (typeof(Healthy)) File.AppendAllText("healthy.log", "packet\n"); return ValueTask.CompletedTask; });
            """, [], members: """
            public Task ConfigPromptAsync(Terminal.Gui.App.IApplication application, System.Threading.CancellationToken token)
            { File.WriteAllText("healthy.config", "opened"); return Task.CompletedTask; }
            """);
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("constructor")]
    public void StartupMissingAssemblyOnlyFailsResponsiblePlugin(string phase)
    {
        CreatePlugin("Faulty", phase == "initialize" ? "Touch();" : "",
            constructor: phase == "constructor" ? "public Faulty() => Touch();" : "");
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();

        Assert.Equal("Healthy", PluginManager.InternalName(Assert.Single(PluginManager.LoadedPlugins)));
        var failed = Assert.Single(PluginManager.SnapshotPluginStatuses(), status => status.InternalName == "Faulty");
        Assert.False(failed.IsLoaded);
        Assert.Contains("AbsentLibrary", failed.Error);
        Assert.Single(PluginManager.Contexts);
        if (phase == "initialize")
            Assert.Equal("disposed", File.ReadAllText("Faulty.disposed"));
    }

    [Theory]
    [InlineData("started")]
    [InlineData("background")]
    [InlineData("config")]
    [InlineData("hotkey")]
    public async Task DelayedMissingAssemblyDisablesOwnerAndSkipsRemainingCallbacks(string phase)
    {
        var initialize = phase switch
        {
            "started" => """
                context.Events.OnStarted(token => { Touch(); return ValueTask.CompletedTask; });
                context.Events.OnStarted(token => { File.WriteAllText("later", "called"); return ValueTask.CompletedTask; });
                """,
            "config" => "",
            "hotkey" => "HotkeyManager.Register(ConsoleKey.F12, \"missing assembly\", () => { Touch(); return Task.CompletedTask; });",
            _ => "context.RunBackground(async token => { await Task.Yield(); Touch(); });"
        };
        CreatePlugin("Faulty", initialize, members: phase == "config" ? "public Task ConfigPromptAsync(Terminal.Gui.App.IApplication application, System.Threading.CancellationToken token) { Touch(); return Task.CompletedTask; }" : "");
        PluginManager.Init();
        var context = Assert.Single(PluginManager.Contexts).Value;
        PluginManager.InitializeLoadedPlugins();
        if (phase == "started")
            await PluginManager.TriggerStartedAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (phase == "config")
            await PluginConfigPrompt.RunAsync(PluginManager.FindLoadedPlugin("Faulty")!).WaitAsync(TimeSpan.FromSeconds(5));
        if (phase == "hotkey")
            Assert.True(await HotkeyManager.HandleKeyAsync(new(KeyCode.F12)).WaitAsync(TimeSpan.FromSeconds(5)));
        await WithServerAsync(port => PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]));
        await WaitUntilAsync(() => File.Exists("Faulty.disposed") && PluginManager.FindLoadedPlugin("Faulty") is null);

        Assert.False(File.Exists("later"));
        Assert.Equal("packet", Assert.Single(File.ReadAllLines("healthy.log")));
        Assert.NotNull(PluginManager.FindLoadedPlugin("Healthy"));
        Assert.Same(context, Assert.Single(PluginManager.Contexts).Value);
        Assert.DoesNotContain(PluginManager.ResponseAnalyzerMethods,
            registration => PluginManager.InternalName(registration.Plugin) == "Faulty");
        Assert.Contains("AbsentLibrary", Assert.Single(PluginManager.SnapshotPluginStatuses(),
            status => status.InternalName == "Faulty").Error);
    }

    [Fact]
    public async Task ConfigurationMenuRemovesFaultedPluginAndAllowsHealthyStartup()
    {
        CreatePlugin("Faulty", "", members: """
            public Task ConfigPromptAsync(Terminal.Gui.App.IApplication application, System.Threading.CancellationToken token)
            { Touch(); return Task.CompletedTask; }
            """);
        PluginManager.Init();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var menu = Task.Run(() => Config.Plugin.PromptAsync(cancellation.Token), cancellation.Token);
        try
        {
            await terminal.WaitForAsync(async () => await terminal.InvokeAsync(() =>
                terminal.Application.TopRunnableView?.SubViews.OfType<Terminal.Gui.Views.Menu>().Any() == true));
            var previous = await terminal.InvokeAsync(() => terminal.Application.TopRunnableView);
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForAsync(async () => await terminal.InvokeAsync(() =>
                terminal.Application.TopRunnableView is { } current && !ReferenceEquals(current, previous) &&
                current.SubViews.OfType<Terminal.Gui.Views.Menu>().Any()));
            Assert.DoesNotContain("Faulty", await terminal.CaptureScreenAsync());

            previous = await terminal.InvokeAsync(() => terminal.Application.TopRunnableView);
            await terminal.InjectAsync(Key.Enter);
            await WaitUntilAsync(() => File.Exists("healthy.config"));
            await terminal.WaitForAsync(async () => await terminal.InvokeAsync(() =>
                terminal.Application.TopRunnableView is { } current && !ReferenceEquals(current, previous) &&
                current.SubViews.OfType<Terminal.Gui.Views.Menu>().Any()));
            await terminal.InjectAsync(Key.CursorDown);
            await terminal.InjectAsync(Key.Enter);
            await menu.WaitAsync(TimeSpan.FromSeconds(5));

            PluginManager.InitializeLoadedPlugins();
            await PluginManager.TriggerStartedAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(File.Exists("healthy.started"));
            Assert.Contains("AbsentLibrary", Assert.Single(PluginManager.SnapshotPluginStatuses(),
                status => status.InternalName == "Faulty").Error);
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await menu.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    }

    [Fact]
    public async Task AnalyzerFaultOnlyDisablesItsEndpointRegistration()
    {
        const string otherPath = "/umamusume/single_mode/check_event";
        CreatePlugin("Faulty", """
            context.Analyzers.Register<ReadOnlyMemory<byte>>(AnalyzerKind.Response,
                [EndpointPattern.Exact("/umamusume/account/index"), EndpointPattern.Exact("/umamusume/single_mode/check_event")], Analyze);
            context.Analyzers.Register<ReadOnlyMemory<byte>>(AnalyzerKind.Response,
                [EndpointPattern.Exact("/umamusume/account/index")], invocation =>
                { File.AppendAllText("sibling.log", "called\n"); return ValueTask.CompletedTask; }, 1);
            context.Events.OnStarted(token => { File.WriteAllText("Faulty.started", "yes"); return ValueTask.CompletedTask; });
            HotkeyManager.Register(ConsoleKey.F12, "healthy hotkey", () =>
            { File.WriteAllText("Faulty.hotkey", "yes"); return Task.CompletedTask; });
            context.RunBackground(async token =>
            {
                using var registration = token.Register(() => File.WriteAllText("background-cancelled", "yes"));
                File.WriteAllText("background-started", "yes");
                await Task.Delay(-1, token);
            });
            """, members: """
            static ValueTask Analyze(AnalyzerInvocation<ReadOnlyMemory<byte>> invocation)
            {
                File.AppendAllText("attempts.log", invocation.Endpoint.Path + "\n");
                if (invocation.Endpoint.Path == "/umamusume/account/index") Touch();
                return ValueTask.CompletedTask;
            }
            public Task ConfigPromptAsync(Terminal.Gui.App.IApplication application, System.Threading.CancellationToken token)
            { File.WriteAllText("Faulty.config", "yes"); return Task.CompletedTask; }
            """);
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        var faulty = PluginManager.FindLoadedPlugin("Faulty")!;
        var registrations = PluginManager.ResponseAnalyzerMethods.ToArray();
        await WaitUntilAsync(() => File.Exists("background-started"));

        await WithServerAsync(async port =>
        {
            await PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]);
            await PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]);
            await PostPacketAsync(port, AnalyzerKind.Response, otherPath, [0xC0]);
        });
        await PluginManager.TriggerStartedAsync();
        await PluginConfigPrompt.RunAsync(faulty);
        Assert.True(await HotkeyManager.HandleKeyAsync(new(KeyCode.F12)));

        Assert.Equal([AccountIndexPath, otherPath], File.ReadAllLines("attempts.log"));
        Assert.Equal(2, File.ReadAllLines("sibling.log").Length);
        Assert.Equal(2, File.ReadAllLines("healthy.log").Length);
        Assert.True(File.Exists("Faulty.started"));
        Assert.True(File.Exists("Faulty.config"));
        Assert.True(File.Exists("Faulty.hotkey"));
        Assert.False(File.Exists("background-cancelled"));
        Assert.False(File.Exists("Faulty.disposed"));
        Assert.Same(faulty, PluginManager.FindLoadedPlugin("Faulty"));
        Assert.False(PluginManager.IsPluginFaulted(faulty));
        Assert.Empty(PluginManager.FailedPlugins);
        Assert.Equal(registrations, PluginManager.ResponseAnalyzerMethods);
        Assert.Single(registrations, registration => registration.IsFaulted);
    }

    [Fact]
    public async Task ConcurrentAnalyzerFaultsReportOnceAndOtherEntryCleanupWaitsForRunningAnalyzer()
    {
        CreatePlugin("Faulty", """
            context.Analyzers.Register<ReadOnlyMemory<byte>>(AnalyzerKind.Response,
                [EndpointPattern.Exact("/umamusume/account/index")], async invocation =>
                {
                    if (invocation.Payload.Span[0] == 0xC0)
                    {
                        SlowEntered.TrySetResult();
                        await ReleaseSlow.Task;
                        File.WriteAllText("slow-completed", "yes");
                        return;
                    }
                    if (System.Threading.Interlocked.Increment(ref faultCalls) == 2) BothFaultsEntered.TrySetResult();
                    await ReleaseFaults.Task;
                    Touch();
                });
            context.RunBackground(async token =>
            {
                using var registration = token.Register(() => BackgroundCancelled.TrySetResult());
                BackgroundStarted.TrySetResult();
                await Task.Delay(-1, token);
            });
            """, members: """
            public readonly TaskCompletionSource SlowEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource ReleaseSlow = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource BothFaultsEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource ReleaseFaults = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource BackgroundStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource BackgroundCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int faultCalls;
            public Task ConfigPromptAsync(Terminal.Gui.App.IApplication application, System.Threading.CancellationToken token)
            { Touch(); return Task.CompletedTask; }
            """);
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        var faulty = PluginManager.FindLoadedPlugin("Faulty")!;
        TaskCompletionSource Signal(string name) => (TaskCompletionSource)faulty.GetType().GetField(name)!.GetValue(faulty)!;
        var registration = Assert.Single(PluginManager.ResponseAnalyzerMethods, item => ReferenceEquals(item.Plugin, faulty));
        var host = TerminalUi.RequireHost();
        var logs = new ConcurrentQueue<UiLogLine>();
        host.LogAdded += logs.Enqueue;
        try
        {
            await Signal("BackgroundStarted").Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WithServerAsync(async port =>
            {
                var slow = PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]);
                var faults = Task.CompletedTask;
                try
                {
                    await Signal("SlowEntered").Task.WaitAsync(TimeSpan.FromSeconds(5));
                    faults = Task.WhenAll(
                        PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC1]),
                        PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC1]));
                    await Signal("BothFaultsEntered").Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Signal("ReleaseFaults").TrySetResult();
                    await faults.WaitAsync(TimeSpan.FromSeconds(5));
                    await PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]).WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.False(slow.IsCompleted);
                    Assert.True(registration.IsFaulted);
                    Assert.False(PluginManager.IsPluginFaulted(faulty));
                    Assert.False(File.Exists("Faulty.disposed"));
                    Assert.False(Signal("BackgroundCancelled").Task.IsCompleted);
                    Assert.Contains(registration, PluginManager.ResponseAnalyzerMethods);
                    await host.FlushAsync();
                    var report = Assert.Single(logs, line => line.ExceptionDetails?.Contains("AbsentLibrary", StringComparison.Ordinal) == true);
                    Assert.Contains("Faulty", report.Text);
                    Assert.Contains(registration.Source, report.Text);
                    Assert.Contains(AccountIndexPath, report.Text);
                    Assert.Contains("Response analyzer", report.Text);
                    Assert.Contains("Touch", report.ExceptionDetails);

                    await PluginConfigPrompt.RunAsync(faulty).WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.True(PluginManager.IsPluginFaulted(faulty));
                    await Signal("BackgroundCancelled").Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.False(File.Exists("Faulty.disposed"));
                }
                finally
                {
                    Signal("ReleaseFaults").TrySetResult();
                    Signal("ReleaseSlow").TrySetResult();
                    await Task.WhenAll(slow, faults).WaitAsync(TimeSpan.FromSeconds(5));
                }
            });
            await WaitUntilAsync(() => PluginManager.FindLoadedPlugin("Faulty") is null);
            Assert.True(File.Exists("slow-completed"));
            Assert.True(File.Exists("Faulty.disposed"));
            Assert.NotNull(PluginManager.FindLoadedPlugin("Healthy"));
        }
        finally
        {
            host.LogAdded -= logs.Enqueue;
        }
    }

    [Fact]
    public async Task BackgroundFaultDrainsWithoutBlockingHealthyStartup()
    {
        CreatePlugin("Faulty", """
            context.RunBackground(async token =>
            {
                File.WriteAllText("blocked", "yes");
                while (!File.Exists("release")) await Task.Delay(10);
            });
            context.RunBackground(async token =>
            {
                while (!File.Exists("blocked")) await Task.Delay(10);
                Touch();
            });
            """);
        PluginManager.Init();
        var faulty = PluginManager.FindLoadedPlugin("Faulty")!;
        PluginManager.InitializeLoadedPlugins();
        try
        {
            await WaitUntilAsync(() => PluginManager.IsPluginFaulted(faulty));
            await PluginManager.TriggerStartedAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(File.Exists("healthy.started"));
            Assert.False(File.Exists("Faulty.disposed"));
        }
        finally
        {
            File.WriteAllText("release", "yes");
        }
        await WaitUntilAsync(() => File.Exists("Faulty.disposed") && PluginManager.FindLoadedPlugin("Faulty") is null);
    }

    [Fact]
    public async Task RepeatedMissingBindInSharedContextDisablesRegistrationsWithoutDisposingConsumers()
    {
        foreach (var name in new[] { "First", "Second" })
            CreatePlugin(name, """
                context.Analyzers.Register<ReadOnlyMemory<byte>>(AnalyzerKind.Response,
                    [EndpointPattern.Exact("/umamusume/account/index")], invocation =>
                    { Touch(); return ValueTask.CompletedTask; });
                """);
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        Assert.Single(PluginManager.Contexts);
        await WithServerAsync(port => PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]));
        Assert.False(File.Exists("First.disposed"));
        Assert.False(File.Exists("Second.disposed"));
        Assert.Equal(3, PluginManager.LoadedPlugins.Count);
        Assert.NotNull(PluginManager.FindLoadedPlugin("Healthy"));
        Assert.Equal("packet", Assert.Single(File.ReadAllLines("healthy.log")));
        Assert.Equal(2, PluginManager.ResponseAnalyzerMethods.Count(registration => registration.IsFaulted));
        Assert.All(PluginManager.SnapshotPluginStatuses(), status => Assert.Null(status.Error));
    }

    [Fact]
    public async Task AvailabilityGuardAvoidsMissingOptionalAssembly()
    {
        CreatePlugin("Optional", """
            context.Analyzers.Register<ReadOnlyMemory<byte>>(AnalyzerKind.Response,
                [EndpointPattern.Exact("/umamusume/account/index")], invocation =>
                { if (context.IsPluginAvailable("Missing")) Touch(); File.WriteAllText("guard", "continued"); return ValueTask.CompletedTask; });
            """, ["Healthy", "Missing"]);
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        await WithServerAsync(port => PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]));
        Assert.Equal("continued", File.ReadAllText("guard"));
        Assert.NotNull(PluginManager.FindLoadedPlugin("Optional"));
        Assert.Empty(PluginManager.FailedPlugins);
    }

    [Theory]
    [InlineData("analyzer")]
    [InlineData("business")]
    [InlineData("started")]
    [InlineData("background")]
    public async Task OrdinaryErrorsDoNotDisablePluginOrAnalyzer(string phase)
    {
        var body = phase == "business"
            ? "File.AppendAllText(\"ordinary.log\", \"called\\n\"); throw new InvalidOperationException(\"business failed\");"
            : "File.AppendAllText(\"ordinary.log\", \"called\\n\"); throw new FileNotFoundException(\"settings file missing\", \"settings.json\");";
        var initialize = phase switch
        {
            "analyzer" or "business" => $$"""
                context.Analyzers.Register<ReadOnlyMemory<byte>>(AnalyzerKind.Response,
                    [EndpointPattern.Exact("/umamusume/account/index")], invocation => { {{body}} });
                """,
            "started" => "context.Events.OnStarted(token => { " + body + " });",
            _ => "context.RunBackground(token => { File.WriteAllText(\"background-ran\", \"yes\"); " + body + " });"
        };
        CreatePlugin("Ordinary", initialize);
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        await PluginManager.TriggerStartedAsync();
        await WithServerAsync(async port =>
        {
            await PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]);
            await PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]);
        });
        if (phase == "background")
            await WaitUntilAsync(() => File.Exists("background-ran"));
        Assert.NotNull(PluginManager.FindLoadedPlugin("Ordinary"));
        Assert.Null(Assert.Single(PluginManager.SnapshotPluginStatuses(), status => status.InternalName == "Ordinary").Error);
        Assert.False(File.Exists("Ordinary.disposed"));
        Assert.DoesNotContain(PluginManager.ResponseAnalyzerMethods, registration => registration.IsFaulted);
        if (phase is "analyzer" or "business")
            Assert.Equal(2, File.ReadAllLines("ordinary.log").Length);
    }

    void CreatePlugin(string name, string initialize, string[]? dependencies = null, string constructor = "", string members = "")
        => PluginCompiler.CompilePackage($$"""
            using System;
            using System.IO;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using UmamusumeResponseAnalyzer.Plugin;
            using UmamusumeResponseAnalyzer.TerminalGui;
            public sealed class {{name}} : IPlugin
            {
                {{constructor}}
                {{members}}
                public void Initialize(IPluginContext context) { {{initialize}} }
                [MethodImpl(MethodImplOptions.NoInlining)]
                static void Touch() => AbsentLibrary.Touch();
                public void Dispose() => File.WriteAllText("{{name}}.disposed", "disposed");
            }
            """, name, Path.Combine("Plugins", name + ".zip"), dependencies ?? ["Healthy"],
            referencePaths: [dependency]);

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, cancellation.Token);
    }

    public void Dispose()
    {
        try { PluginManager.ShutdownAsync().GetAwaiter().GetResult(); }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(directory, recursive: true);
        }
    }

    async Task WithServerAsync(Func<int, Task> action)
    {
        var port = GetFreeTcpPort();
        Server.Instance = new(
            new WatsonWebserver.Core.WebserverSettings("127.0.0.1", port),
            context => context.Response.Send(string.Empty));
        Server.Start(TestContext.Current.CancellationToken);
        try
        {
            await action(port);
        }
        finally
        {
            await Server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Server.Instance = new(
                new WatsonWebserver.Core.WebserverSettings("127.0.0.1", GetFreeTcpPort()),
                context => context.Response.Send(string.Empty));
        }
    }

    static async Task PostPacketAsync(
        int port,
        AnalyzerKind kind,
        string canonicalUrl,
        byte[] payload)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{port}/notify/{kind.ToString().ToLowerInvariant()}");
        request.Headers.Add("X-Hachimi-Game-Url", canonicalUrl);
        request.Content = new ByteArrayContent(payload);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

}
