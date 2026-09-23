using System.Reflection;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.TerminalGui;
using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;

namespace UmamusumeResponseAnalyzer.Plugin;

internal static partial class PluginManager
{
    internal static bool IsPluginFaulted(IPlugin plugin)
        => Runtime.Lifecycles.TryGetValue(plugin, out var lifecycle) && lifecycle.IsFaulted;

    internal static void ReportBackgroundFailure(IPlugin plugin, Exception error)
    {
        if (TryDisableForMissingAssembly(plugin, error, "Background"))
            return;

        var failure = new InvalidOperationException(
            string.Format(i18n.BackgroundOperationFailed, InternalName(plugin), DescribeException(error)));
        ReportPluginFailure("Plugin", failure, error.ToString());
    }

    internal static bool TryDisableAnalyzerForMissingAssembly(AnalyzerRegistration registration, Exception error, string phase)
    {
        if (FindMissingPluginAssembly(error) is not { } missing)
            return false;

        if (registration.TryMarkFaulted())
        {
            var failure = string.Format(
                i18n.AnalyzerDisabledMissingAssembly,
                InternalName(registration.Plugin), registration.Source,
                GameEndpointCatalog.ByEndpointType[registration.EndpointType].Path,
                phase, missing.AssemblyName.FullName, DescribeException(error));
            ReportPluginFailure("Plugin", new InvalidOperationException(failure), error.ToString());
        }
        return true;
    }

    internal static bool TryDisableForMissingAssembly(IPlugin plugin, Exception error, string phase)
    {
        if (FindMissingPluginAssembly(error) is not { } missing)
            return false;

        var failure = string.Format(
            i18n.PluginDisabledMissingAssembly,
            InternalName(plugin), phase, missing.AssemblyName.FullName, DescribeException(error));
        var lifecycle = LifecycleFor(plugin);
        if (!lifecycle.TryMarkFaulted(failure))
            return true;

        _ = lifecycle.Close();
        ReportPluginFailure("Plugin", new InvalidOperationException(failure), error.ToString());

        // A fault can be reported while Initialize or StartAsync still holds its callback lease.
        _ = Task.Run(async () =>
        {
            await lifecycle.Close();
            Task cleanup;
            await Runtime.LifecycleGate.WaitAsync();
            try
            {
                if (lifecycle.CleanupTask is not null)
                    return;

                var name = InternalName(plugin);
                var failedPackage = Runtime.Metadatas.TryGetValue(name, out var metadata)
                    ? metadata.PackagePath
                    : name;
                if (!Runtime.FailedPlugins.Contains(failedPackage))
                    Runtime.FailedPlugins.Add(failedPackage);

                cleanup = CleanupPluginResourcesAsync(plugin, flush: true);
            }
            finally
            {
                try { Runtime.Publish(); }
                finally { Runtime.LifecycleGate.Release(); }
            }

            try
            {
                await cleanup;
            }
            catch (Exception cleanupError)
            {
                ReportPluginFailure(
                    "Plugin",
                    new InvalidOperationException(i18n.CleanupFailed),
                    cleanupError.ToString());
            }
            finally
            {
                await Runtime.LifecycleGate.WaitAsync();
                try
                {
                    RemoveAnalyzerMethods(plugin);
                    Runtime.LoadedPlugins.RemoveAll(candidate => ReferenceEquals(candidate, plugin));
                    Runtime.Publish();
                }
                finally { Runtime.LifecycleGate.Release(); }
            }
        });
        return true;
    }

    static MissingPluginAssemblyException? FindMissingPluginAssembly(Exception error)
    {
        if (error is MissingPluginAssemblyException missing)
            return missing;

        var nested = error switch
        {
            AggregateException aggregate => aggregate.InnerExceptions,
            ReflectionTypeLoadException reflection => reflection.LoaderExceptions.OfType<Exception>(),
            _ => [],
        };
        foreach (var exception in nested)
            if (FindMissingPluginAssembly(exception) is { } found)
                return found;

        return error.InnerException is { } inner ? FindMissingPluginAssembly(inner) : null;
    }

    internal static async Task CleanupPluginAsync(IPlugin plugin, bool flush = false)
    {
        try
        {
            await CleanupPluginResourcesAsync(plugin, flush);
        }
        finally
        {
            RemoveAnalyzerMethods(plugin);
            Runtime.LoadedPlugins.RemoveAll(candidate => ReferenceEquals(candidate, plugin));
        }
    }

    static Task CleanupPluginResourcesAsync(IPlugin plugin, bool flush)
    {
        var lifecycle = LifecycleFor(plugin);
        return lifecycle.CleanupTask ??= CompletePluginCleanupAsync(plugin, lifecycle, flush);
    }

    static async Task CompletePluginCleanupAsync(IPlugin plugin, PluginLifecycle lifecycle, bool flush)
    {
        var name = InternalName(plugin);
        var failures = new List<Exception>();
        await lifecycle.Close();

        using (HotkeyManager.RegisterScope(plugin))
        {
            try
            {
                await plugin.DisposeAsync();
            }
            catch (Exception error)
            {
                failures.Add(new InvalidOperationException(string.Format(i18n.CleanupPhaseFailed, name, "DisposeAsync"), error));
            }
        }

        try
        {
            HotkeyManager.UnregisterByOwner(plugin);
        }
        catch (Exception error)
        {
            failures.Add(new InvalidOperationException(string.Format(i18n.CleanupPhaseFailed, name, "UnregisterHotkeys"), error));
        }

        if (flush)
        {
            try
            {
                await TerminalUi.RequireHost().FlushAsync();
            }
            catch (Exception error)
            {
                failures.Add(new InvalidOperationException(string.Format(i18n.CleanupPhaseFailed, name, "Flush"), error));
            }
        }

        if (failures.Count != 0)
            throw new AggregateException(i18n.CleanupFailed, failures);
    }
}
