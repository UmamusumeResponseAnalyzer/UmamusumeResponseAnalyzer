using System.Globalization;
using System.Reflection;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    // 这个 collection 改 CWD（进程级）并驱动 PluginManager 全局静态状态，必须与所有其它测试串行隔离
    [CollectionDefinition("PluginRuntime", DisableParallelization = true)]
    public class PluginRuntimeCollection : ICollectionFixture<PluginRuntimeFixture> { }

    public sealed class PluginRuntimeFixture : IDisposable
    {
        readonly string configDirectory;
        readonly string originalCwd;
        readonly PropertyInfo configCurrent;
        readonly object? originalConfig;
        readonly CultureInfo originalCulture;
        readonly CultureInfo originalUiCulture;
        readonly (FieldInfo Field, object? Value)[] originalResourceCultures;
        readonly Task run;

        public PluginRuntimeFixture()
        {
            originalCwd = Directory.GetCurrentDirectory();
            configCurrent = typeof(Config).GetProperty(
                "Current",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            originalConfig = configCurrent.GetValue(null);
            originalCulture = Thread.CurrentThread.CurrentCulture;
            originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            originalResourceCultures = typeof(Config).Assembly.GetTypes()
                .Where(type => type.Namespace?.StartsWith(
                    "UmamusumeResponseAnalyzer.Localization",
                    StringComparison.Ordinal) == true)
                .Select(type => type.GetField(
                    "resourceCulture",
                    BindingFlags.NonPublic | BindingFlags.Static))
                .OfType<FieldInfo>()
                .Select(field => (field, field.GetValue(null)))
                .ToArray();

            configDirectory = Path.Combine(
                Path.GetTempPath(),
                $"ura-plugin-runtime-{Guid.NewGuid():N}");
            Directory.CreateDirectory(configDirectory);
            Directory.SetCurrentDirectory(configDirectory);

            TerminalGuiTestApp? terminal = null;
            UiHost? host = null;
            Task? startedRun = null;
            try
            {
                Config.Initialize();

                terminal = new TerminalGuiTestApp();
                terminal.RunOnOwnerThread(() =>
                {
                    var createdHost = new UiHost(
                        terminal.Application,
                        SynchronizationContext.Current!,
                        CancellationToken.None);
                    TerminalUi.Initialize(createdHost);
                    host = createdHost;
                });
                var initializedHost = host
                    ?? throw new InvalidOperationException("UiHost fixture initialization did not complete.");
                HotkeyManager.OverlaySink = initializedHost;
                var activeRun = terminal.StartAsync(initializedHost).GetAwaiter().GetResult();
                startedRun = activeRun;

                Terminal = terminal;
                Host = initializedHost;
                run = activeRun;
            }
            catch
            {
                try
                {
                    if (terminal is not null)
                    {
                        try
                        {
                            if (host is not null && startedRun is not null)
                                terminal.StopAsync(host, startedRun).GetAwaiter().GetResult();
                        }
                        finally
                        {
                            terminal.Dispose();
                        }
                    }
                }
                finally
                {
                    RestoreConfigState();
                    Directory.SetCurrentDirectory(originalCwd);
                    Directory.Delete(configDirectory, recursive: true);
                }
                throw;
            }
        }

        internal TerminalGuiTestApp Terminal { get; }
        internal UiHost Host { get; }
        internal IApplication Application => Terminal.Application;

        public void Dispose()
        {
            try
            {
                try
                {
                    Terminal.StopAsync(Host, run).GetAwaiter().GetResult();
                }
                finally
                {
                    Terminal.Dispose();
                }
            }
            finally
            {
                RestoreConfigState();
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(configDirectory, recursive: true);
            }
        }

        void RestoreConfigState()
        {
            configCurrent.SetValue(null, originalConfig);
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            foreach (var (field, value) in originalResourceCultures)
                field.SetValue(null, value);
        }

    }

}
