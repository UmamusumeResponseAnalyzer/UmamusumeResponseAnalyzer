using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;
using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;

namespace UmamusumeResponseAnalyzer.Tests;

public sealed class TerminalUiLifecycleProcessTests
{
    [Fact]
    public Task UninitializedHostIsRejected()
        => AssertLifecycleScenarioAsync(
            "uninitialized",
            nameof(i18n.Host_NotInitialized),
            TerminalUiLifecycleChildProcess.Uninitialized,
            nameof(UninitializedHostIsRejected));

    [Fact]
    public Task DuplicateInitializeIsRejected()
        => AssertLifecycleScenarioAsync(
            "duplicate-initialize",
            nameof(i18n.Host_AlreadyInitialized),
            TerminalUiLifecycleChildProcess.DuplicateInitialize,
            nameof(DuplicateInitializeIsRejected));

    [Fact]
    public Task StoppingHostIsRejected()
        => AssertLifecycleScenarioAsync(
            "stopping",
            nameof(i18n.Host_Stopping),
            TerminalUiLifecycleChildProcess.Stopping,
            nameof(StoppingHostIsRejected));

    [Fact]
    public Task StoppedHostIsRejected()
        => AssertLifecycleScenarioAsync(
            "stopped",
            nameof(i18n.Host_Stopped),
            TerminalUiLifecycleChildProcess.Stopped,
            nameof(StoppedHostIsRejected));

    private static async Task AssertLifecycleScenarioAsync(
        string scenario,
        string resourceKey,
        Func<string> childAction,
        string methodName)
    {
        var culture = CultureInfo.GetCultureInfo("ja-JP");
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            i18n.Culture = culture;
            TerminalUiLifecycleChildProcess.WriteResult(childAction());
            return;
        }

        Assert.Equal(
            i18n.ResourceManager.GetString(resourceKey, culture),
            await RunChildAsync(
                scenario,
                typeof(TerminalUiLifecycleProcessTests),
                methodName));
    }

    internal static async Task<string> RunChildAsync(
        string scenario,
        Type testClass,
        string methodName)
    {
        var resultPath = Path.Combine(
            Path.GetTempPath(),
            $"ura-terminal-ui-child-{Guid.NewGuid():N}.result");
        try
        {
            var testClassName = testClass.FullName
                ?? throw new InvalidOperationException("Child test class must have a full name.");
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(typeof(TerminalUiLifecycleProcessTests).Assembly.Location);
            startInfo.ArgumentList.Add("-method");
            startInfo.ArgumentList.Add($"{testClassName}.{methodName}");
            startInfo.Environment[TerminalUiLifecycleChildProcess.ScenarioEnvironmentVariable] = scenario;
            startInfo.Environment[TerminalUiLifecycleChildProcess.ResultPathEnvironmentVariable] = resultPath;

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 TerminalUi lifecycle child process。");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var diagnostics =
                $"TerminalUi lifecycle child process failed ({scenario}), exit={process.ExitCode}:{Environment.NewLine}{stderr}{stdout}";
            Assert.True(process.ExitCode == 0, diagnostics);
            Assert.True(File.Exists(resultPath), diagnostics);
            return await File.ReadAllTextAsync(resultPath, Encoding.UTF8);
        }
        finally
        {
            File.Delete(resultPath);
        }
    }
}

internal static class TerminalUiLifecycleChildProcess
{
    internal const string ScenarioEnvironmentVariable = "URA_TERMINAL_UI_LIFECYCLE_SCENARIO";
    internal const string ResultPathEnvironmentVariable = "URA_TERMINAL_UI_LIFECYCLE_RESULT";
    static readonly PropertyInfo CurrentConfig =
        typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static bool IsChild(string expectedScenario)
    {
        var scenario = Environment.GetEnvironmentVariable(ScenarioEnvironmentVariable);
        var resultPath = Environment.GetEnvironmentVariable(ResultPathEnvironmentVariable);
        if (scenario is null && resultPath is null)
            return false;

        if (!string.Equals(scenario, expectedScenario, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(resultPath))
        {
            throw new InvalidOperationException(
                $"TerminalUi child protocol mismatch: expected={expectedScenario}, actual={scenario ?? "<null>"}.");
        }

        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return true;
    }

    internal static void WriteResult(string result)
    {
        var resultPath = Environment.GetEnvironmentVariable(ResultPathEnvironmentVariable)
            ?? throw new InvalidOperationException("TerminalUi child result path is missing.");
        File.WriteAllText(
            resultPath,
            result,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static string Uninitialized()
        => CaptureFailure(() => TerminalUi.Log("lifecycle-test", "uninitialized"));

    internal static string DuplicateInitialize()
    {
        using var terminal = new TerminalGuiTestApp();
        _ = InitializeHost(terminal, CancellationToken.None);
        UiHost? replacement = null;
        terminal.RunOnOwnerThread(() =>
            replacement = new UiHost(
                terminal.Application,
                SynchronizationContext.Current!,
                CancellationToken.None));
        return CaptureFailure(() => terminal.RunOnOwnerThread(() => TerminalUi.Initialize(replacement!)));
    }

    internal static string Stopping()
    {
        using var terminal = new TerminalGuiTestApp();
        using var lifetime = new CancellationTokenSource();
        _ = InitializeHost(terminal, lifetime.Token);
        lifetime.Cancel();
        return CaptureFailure(() => TerminalUi.Log("lifecycle-test", "stopping"));
    }

    internal static string Stopped()
    {
        using var terminal = new TerminalGuiTestApp();
        var host = InitializeHost(terminal, CancellationToken.None);
        var run = terminal.StartAsync(host).GetAwaiter().GetResult();
        terminal.StopAsync(host, run).GetAwaiter().GetResult();
        return CaptureFailure(() => TerminalUi.Log("lifecycle-test", "stopped"));
    }

    internal static UiHost InitializeHost(TerminalGuiTestApp terminal, CancellationToken lifetimeToken)
    {
        Assert.Null(CurrentConfig.GetValue(null));
        CurrentConfig.SetValue(null, new YamlConfig());
        UiHost? host = null;
        terminal.RunOnOwnerThread(() =>
        {
            host = new UiHost(
                terminal.Application,
                SynchronizationContext.Current!,
                lifetimeToken);
            TerminalUi.Initialize(host);
        });
        return host!;
    }

    static string CaptureFailure(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        throw new InvalidOperationException("TerminalUi lifecycle scenario did not reject the operation.");
    }
}
