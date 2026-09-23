using System.Reflection;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.TerminalGui;
using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;

namespace UmamusumeResponseAnalyzer.Plugin;

internal static partial class PluginManager
{
    internal static bool IsPluginFaulted(IPlugin plugin)
        => PluginGenerations.TryGetValue(plugin, out var generation) && generation.IsFaulted;

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
        var generation = GenerationFor(plugin);
        if (!generation.TryMarkFaulted(failure))
            return true;

        _ = generation.Close();
        ReportPluginFailure("Plugin", new InvalidOperationException(failure), error.ToString());

        // The current callback or background operation must release its own lease before cleanup can finish.
        _ = Task.Run(async () =>
        {
            await generation.Close().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await Runtime.LifecycleGate.WaitAsync();
            try
            {
                if (generation.CleanupTask is not null)
                    return;

                var name = InternalName(plugin);
                Lifecycle.Failures[name] = failure;
                var failedPackage = LifecycleMetadatas.TryGetValue(name, out var metadata)
                    ? metadata.PackagePath
                    : name;
                if (!LifecycleFailedPlugins.Contains(failedPackage))
                    LifecycleFailedPlugins.Add(failedPackage);

                await CleanupPluginAsync(plugin, flush: true);
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
                try { Runtime.Publish(); }
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

    internal static Task CleanupPluginAsync(IPlugin plugin, bool flush = false)
    {
        var generation = GenerationFor(plugin);
        return generation.CleanupTask ??= CompletePluginCleanupAsync(plugin, generation, flush);
    }

    static async Task CompletePluginCleanupAsync(IPlugin plugin, PluginGeneration generation, bool flush)
    {
        var name = InternalName(plugin);
        var failures = new List<Exception>();
        try
        {
            await generation.Close();
        }
        catch (Exception error)
        {
            failures.Add(new InvalidOperationException(string.Format(i18n.CleanupPhaseFailed, name, "Close"), error));
        }

        using (HotkeyManager.RegisterScope(plugin))
        {
            try
            {
                plugin.Dispose();
            }
            catch (Exception error)
            {
                failures.Add(new InvalidOperationException(string.Format(i18n.CleanupPhaseFailed, name, "Dispose"), error));
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

        RemoveAnalyzerMethods(plugin);
        DisposeHostEventSubscriptions(plugin);
        LifecycleLoadedPlugins.RemoveAll(candidate => ReferenceEquals(candidate, plugin));

        if (failures.Count != 0)
            throw new AggregateException(i18n.CleanupFailed, failures);
    }
}
