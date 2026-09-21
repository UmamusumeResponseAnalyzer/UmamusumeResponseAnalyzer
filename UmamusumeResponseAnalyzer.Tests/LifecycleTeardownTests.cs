using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

public sealed class LifecycleTeardownTests
{
    [Fact]
    public async Task RunCleanupAsync_AttemptsEveryActionAndPreservesWorkflowFailure()
    {
        var workflowFailure = new InvalidOperationException("workflow");
        var calls = new List<int>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UmamusumeResponseAnalyzer.RunCleanupAsync(
                ExceptionDispatchInfo.Capture(workflowFailure),
                [
                    () =>
                    {
                        calls.Add(1);
                        throw new IOException("cleanup-1");
                    },
                    () =>
                    {
                        calls.Add(2);
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        calls.Add(3);
                        throw new IOException("cleanup-3");
                    },
                ]));

        Assert.Same(workflowFailure, thrown);
        Assert.Equal([1, 2, 3], calls);
    }

    [Fact]
    public async Task RunCleanupAsync_WithoutWorkflowFailureAggregatesCleanupFailures()
    {
        var thrown = await Assert.ThrowsAsync<AggregateException>(() =>
            UmamusumeResponseAnalyzer.RunCleanupAsync(
                null,
                [
                    () => throw new IOException("cleanup-1"),
                    () => throw new InvalidOperationException("cleanup-2"),
                ]));

        Assert.Collection(
            thrown.InnerExceptions,
            ex => Assert.Equal("cleanup-1", ex.Message),
            ex => Assert.Equal("cleanup-2", ex.Message));
    }

    [Fact]
    public async Task StopAsync_BeforeConfigInitializationDoesNotCreateServer()
    {
        const string scenario = "server-stop-before-config";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            await Server.StopAsync();
            Assert.False(Server.IsRunning);
            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(LifecycleTeardownTests),
                nameof(StopAsync_BeforeConfigInitializationDoesNotCreateServer)));
    }

    [Fact]
    public async Task StopAsync_ReleasesListeningPort()
    {
        const string scenario = "server-stop-releases-port";
        if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            Assert.Equal("ok", await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario, typeof(LifecycleTeardownTests), nameof(StopAsync_ReleasesListeningPort)));
            return;
        }

        using var lifetime = new CancellationTokenSource();
        using var terminal = new TerminalGuiTestApp();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, lifetime.Token);
        var run = await terminal.StartAsync(host);
        var port = GetFreePort();
        try
        {
            Server.Instance = new(
                new WebserverSettings("127.0.0.1", port),
                ctx => ctx.Response.Send(string.Empty));
            Server.Start(lifetime.Token);
            using var client = new HttpClient();
            Assert.Equal("pong", await client.GetStringAsync(
                $"http://127.0.0.1:{port}/notify/ping", TestContext.Current.CancellationToken));

            lifetime.Cancel();
            await Server.StopAsync();

            Assert.False(Server.IsRunning);
            using var rebound = new TcpListener(IPAddress.Loopback, port);
            rebound.Start();
        }
        finally
        {
            lifetime.Cancel();
            await Server.StopAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        TerminalUiLifecycleChildProcess.WriteResult("ok");
    }

    static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
