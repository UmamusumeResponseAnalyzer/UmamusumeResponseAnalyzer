using System.Runtime.CompilerServices;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal static partial class PluginManager
    {
        // ── 热重载 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 批量应用插件重载，逐项返回成功或失败。
        /// 新安装的共享上下文成员会先把已加载的同组插件排入重载顺序，避免共享锚点被加载进两个 ALC。
        /// </summary>
        internal static async Task<IReadOnlyList<PluginLifecycleResult>> ReloadPluginsAsync(params string[] pluginNames)
        {
            using var transaction = EnterReloadTransaction("load/reload/unload");
            RequireOperationalLifecyclePhase();
            var requested = pluginNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (requested.Count == 0) return [];

            var startedPluginBatches = new List<IPlugin[]>();
            var scanned = ScanPluginMetadata();
            List<string> ordered;
            requested = requested
                .Select(name => ResolvePluginName(
                    name,
                    scanned.Keys,
                    LifecycleMetadatas.Keys,
                    LifecycleLoadedPlugins.Select(InternalName)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ordered = BuildReloadOrder(requested, scanned);

            var outcomes = new Dictionary<string, PluginLifecycleOutcome>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ordered)
            {
                try
                {
                    outcomes[name] = await ReloadPluginAsync(name, scanned, outcomes, startedPluginBatches);
                }
                catch (Exception ex)
                {
                    outcomes[name] = PluginLifecycleOutcome.Failed;
                    ReportPluginDiagnostic(ex);
#if DEBUG
                    throw;
#endif
                }
            }

            foreach (var plugins in startedPluginBatches)
            {
                try
                {
                    await TriggerStartedForPluginsAsync(plugins);
                }
                catch (Exception ex)
                {
                    ReportPluginDiagnostic(ex);
#if DEBUG
                    throw;
#endif
                }
            }

            return requested
                .Select(name => new PluginLifecycleResult(name, outcomes[name]))
                .ToList();
        }

        internal static async Task<IReadOnlyList<PluginLifecycleResult>> LoadPluginsAsync(params string[] pluginNames)
        {
            using var transaction = EnterReloadTransaction("load/reload/unload");
            RequireOperationalLifecyclePhase();
            var requested = pluginNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (requested.Count == 0) return [];

            var startedPluginBatches = new List<IPlugin[]>();
            var scanned = ScanPluginMetadata();
            List<(string Raw, string Name, bool Available, bool Loaded)> resolved;
            List<string> ordered;
            resolved = requested.Select(raw =>
            {
                var name = ResolvePluginName(
                    raw,
                    scanned.Keys,
                    LifecycleMetadatas.Keys,
                    LifecycleLoadedPlugins.Select(InternalName));
                return (
                    raw,
                    name,
                    scanned.ContainsKey(name) || LifecycleMetadatas.ContainsKey(name),
                    LifecycleLoadedPlugins.Any(plugin =>
                        string.Equals(InternalName(plugin), name, StringComparison.OrdinalIgnoreCase)));
            }).ToList();
            ordered = BuildReloadOrder(
                resolved.Where(item => item.Available && !item.Loaded).Select(item => item.Name).ToList(),
                scanned);

            var outcomes = new Dictionary<string, PluginLifecycleOutcome>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ordered)
                outcomes[name] = await ReloadPluginAsync(name, scanned, outcomes, startedPluginBatches);

            foreach (var item in resolved)
            {
                if (outcomes.ContainsKey(item.Name))
                    continue;
                if (item.Loaded)
                {
                    outcomes[item.Name] = PluginLifecycleOutcome.Succeeded;
                    continue;
                }
                if (!item.Available)
                {
                    ReportPluginDiagnostic(
                        $"插件 {item.Raw} 不存在，无法加载。",
                        UiSeverity.Warning);
                    outcomes[item.Name] = PluginLifecycleOutcome.Failed;
                    continue;
                }
                outcomes[item.Name] = IsPluginLoaded(item.Name)
                    ? PluginLifecycleOutcome.Succeeded
                    : PluginLifecycleOutcome.Failed;
            }

            foreach (var plugins in startedPluginBatches)
                await TriggerStartedForPluginsAsync(plugins);

            return resolved
                .Select(item => new PluginLifecycleResult(item.Raw, outcomes[item.Name]))
                .ToList();
        }

        internal static async Task<IReadOnlyList<PluginLifecycleResult>> UnloadPluginsAsync(params string[] pluginNames)
        {
            using var transaction = EnterReloadTransaction("load/reload/unload");
            RequireOperationalLifecyclePhase();
            var requested = pluginNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (requested.Count == 0) return [];

            var outcomes = new Dictionary<string, PluginLifecycleOutcome>(StringComparer.OrdinalIgnoreCase);
            var startedPluginBatches = new List<IPlugin[]>();
            List<Exception> failures = [];
            foreach (var rawName in requested)
            {
                var name = ResolvePluginName(
                    rawName,
                    LifecycleMetadatas.Keys,
                    LifecycleLoadedPlugins.Select(InternalName),
                    LifecycleContextGroups.SelectMany(group => group));

                try
                {
                    outcomes[rawName] = await UnloadPluginAsync(name, outcomes, startedPluginBatches);
                }
                catch (Exception ex)
                {
                    outcomes[rawName] = PluginLifecycleOutcome.Failed;
                    failures.Add(new InvalidOperationException($"插件卸载失败: plugin={name}", ex));
                }
            }

            if (failures.Count != 0)
                throw new AggregateException("插件批量卸载失败。", failures);

            foreach (var plugins in startedPluginBatches)
                await TriggerStartedForPluginsAsync(plugins);

            return requested
                .Select(name => new PluginLifecycleResult(name, outcomes[name]))
                .ToList();
        }

        static IDisposable EnterReloadTransaction(string operation)
        {
            RejectCallbackLifecycleReentry();
            if (Runtime.ShutdownRequested)
                throw LifecyclePhaseFailure(operation);
            if (!Runtime.LifecycleGate.Wait(0))
            {
                if (Runtime.ShutdownRequested)
                    throw LifecyclePhaseFailure(operation);
                throw new InvalidOperationException(
                    "已有插件 lifecycle 事务正在运行，拒绝并发或重入操作。");
            }
            if (Runtime.ShutdownRequested)
            {
                Runtime.LifecycleGate.Release();
                throw LifecyclePhaseFailure(operation);
            }

            return new LifecycleTransaction();
        }

        sealed class LifecycleTransaction : IDisposable
        {
            int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                    return;

                try
                {
                    Runtime.Publish();
                }
                finally
                {
                    Runtime.LifecycleGate.Release();
                }
            }
        }

        static List<string> BuildReloadOrder(IReadOnlyList<string> requested, IReadOnlyDictionary<string, PluginMetadata> scanned)
        {
            _ = TopologicalOrder(scanned, scanned.Keys);
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in requested)
            {
                var existingGroup = LifecycleContextGroups.FirstOrDefault(group =>
                    group.Contains(name, StringComparer.OrdinalIgnoreCase));
                if (existingGroup is not null)
                    foreach (var member in existingGroup.Order(StringComparer.OrdinalIgnoreCase))
                        if (seen.Add(member))
                            ordered.Add(member);

                foreach (var member in DependencyComponent(name, scanned))
                    if (seen.Add(member))
                        ordered.Add(member);
                if (seen.Add(name)) ordered.Add(name);
            }
            return ordered;
        }

        static IReadOnlyList<string> DependencyComponent(
            string pluginName,
            IReadOnlyDictionary<string, PluginMetadata> source)
        {
            if (!source.ContainsKey(pluginName))
                return [];

            var component = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pluginName };
            Queue<string> pending = new([pluginName]);
            while (pending.TryDequeue(out var name))
            {
                if (!source.TryGetValue(name, out var metadata))
                    throw new InvalidDataException($"插件依赖图包含未安装插件: {name}");

                foreach (var candidate in source.Values)
                    if (candidate.Dependencies.Contains(name, StringComparer.OrdinalIgnoreCase) &&
                        component.Add(candidate.PluginName))
                        pending.Enqueue(candidate.PluginName);

                foreach (var dependency in metadata.Dependencies)
                {
                    if (!source.ContainsKey(dependency))
                        continue;
                    if (component.Add(dependency))
                        pending.Enqueue(dependency);
                }
            }
            return source.Keys.Where(component.Contains).ToArray();
        }

        static async Task<PluginLifecycleOutcome> ReloadPluginAsync(
            string pluginName,
            IReadOnlyDictionary<string, PluginMetadata> scanned,
            Dictionary<string, PluginLifecycleOutcome> outcomes,
            List<IPlugin[]> startedPluginBatches)
        {
            if (outcomes.TryGetValue(pluginName, out var prior)) return prior;

            var affectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pluginName };
            List<PendingPluginUnload> unloads = [];
            var existing = LifecycleMetadatas.GetValueOrDefault(pluginName);
            affectedNames.UnionWith(DependencyComponent(pluginName, scanned));
            if (existing is not null)
            {
                var group = LifecycleContextGroups.FirstOrDefault(candidate =>
                    candidate.Contains(pluginName, StringComparer.OrdinalIgnoreCase));
                if (group is not null)
                    affectedNames.UnionWith(group);
            }

            var affectedGroups = LifecycleContextGroups
                .Where(group => group.Any(affectedNames.Contains))
                .Select(group => group.ToHashSet(StringComparer.OrdinalIgnoreCase))
                .ToList();
            foreach (var group in affectedGroups)
            {
                if (LifecycleContexts.ContainsKey(GroupKey(group)))
                {
                    unloads.Add(PrepareUnloadGroup(group));
                    continue;
                }

                foreach (var name in group)
                    LifecycleMetadatas.Remove(name);
                LifecycleContextGroups.RemoveAll(candidate => candidate.SetEquals(group));
            }

            if (unloads.Count != 0)
            {
                await CompletePendingUnloadsAsync(unloads, clearAll: false);
                unloads.Clear();
            }

            MergeUnloadedMetadatas(scanned);
            var missing = !LifecycleMetadatas.ContainsKey(pluginName);
            BuildGroups();

            if (missing)
            {
                TerminalUi.Log("Plugin", $"插件 {pluginName} 的文件已不存在，已卸载。", UiSeverity.Warning);
                await LoadAffectedGroupsAsync(affectedNames, outcomes, startedPluginBatches);
                return outcomes[pluginName] = PluginLifecycleOutcome.Succeeded;
            }

            await LoadAffectedGroupsAsync(affectedNames, outcomes, startedPluginBatches);

            var loaded = IsPluginLoaded(pluginName);
            if (loaded)
                TerminalUi.Log("Plugin", $"插件 {pluginName} 已重载。", UiSeverity.Success);
            return outcomes[pluginName] = loaded
                ? PluginLifecycleOutcome.Succeeded
                : PluginLifecycleOutcome.Failed;
        }

        static async Task<PluginLifecycleOutcome> UnloadPluginAsync(
            string pluginName,
            Dictionary<string, PluginLifecycleOutcome> outcomes,
            List<IPlugin[]> startedPluginBatches)
        {
            if (outcomes.TryGetValue(pluginName, out var prior)) return prior;

            PendingPluginUnload? unload = null;
            HashSet<string>? group = null;
            Dictionary<string, PluginMetadata>? survivorMetadatas = null;
            var notLoaded = false;
            var loadedPlugin = LifecycleLoadedPlugins.FirstOrDefault(plugin =>
                string.Equals(InternalName(plugin), pluginName, StringComparison.OrdinalIgnoreCase));
            var existing = LifecycleMetadatas.GetValueOrDefault(pluginName);
            group = LifecycleContextGroups.FirstOrDefault(candidate =>
                candidate.Contains(pluginName, StringComparer.OrdinalIgnoreCase));
            if (group is null)
            {
                if (loadedPlugin is not null)
                    throw new InvalidOperationException($"已加载插件缺少 context group: {pluginName}");
                if (existing is not null)
                    LifecycleMetadatas.Remove(existing.PluginName);
                notLoaded = true;
            }
            else
            {
                var unloadGroup = group;
                survivorMetadatas = unloadGroup
                    .Where(name => !string.Equals(name, pluginName, StringComparison.OrdinalIgnoreCase))
                    .Where(LifecycleMetadatas.ContainsKey)
                    .ToDictionary(
                        name => name,
                        name => LifecycleMetadatas[name],
                        StringComparer.OrdinalIgnoreCase);
                if (LifecycleContexts.ContainsKey(GroupKey(unloadGroup)))
                {
                    unload = PrepareUnloadGroup(unloadGroup);
                }
                else
                {
                    foreach (var name in unloadGroup)
                        LifecycleMetadatas.Remove(name);
                    LifecycleContextGroups.RemoveAll(candidate => candidate.SetEquals(unloadGroup));
                }
            }

            if (notLoaded)
            {
                TerminalUi.Log("Plugin", $"插件 {pluginName} 未加载。", UiSeverity.Warning);
                return outcomes[pluginName] = PluginLifecycleOutcome.Succeeded;
            }

            if (unload is not null)
            {
                await CompletePendingUnloadsAsync([unload], clearAll: false);
                unload = null;
            }

            foreach (var (name, metadata) in survivorMetadatas!)
                LifecycleMetadatas[name] = metadata;
            BuildGroups();

            var survivorOutcomes = new Dictionary<string, PluginLifecycleOutcome>(StringComparer.OrdinalIgnoreCase);
            await LoadAffectedGroupsAsync(survivorMetadatas.Keys, survivorOutcomes, startedPluginBatches);
            var failedSurvivors = survivorOutcomes
                .Where(result => result.Value == PluginLifecycleOutcome.Failed)
                .Select(result => result.Key)
                .ToArray();
            if (failedSurvivors.Length != 0)
                throw new InvalidOperationException(
                    $"插件 {pluginName} 已卸载，但关联插件重新加载失败: {string.Join("、", failedSurvivors)}");

            TerminalUi.Log("Plugin", $"插件 {pluginName} 已卸载。", UiSeverity.Success);
            return outcomes[pluginName] = PluginLifecycleOutcome.Succeeded;
        }

        /// <summary>加载受本轮重载影响且尚无 ALC 的上下文组，对新实例调用 Initialize，并补发一次启动事件。</summary>
        static async Task LoadAffectedGroupsAsync(
            IEnumerable<string> affectedNames,
            Dictionary<string, PluginLifecycleOutcome> outcomes,
            List<IPlugin[]> startedPluginBatches)
        {
            var initialize = Lifecycle.Phase is
                PluginLifecyclePhase.Initialized or PluginLifecyclePhase.Started;
            var deliverStarted = Lifecycle.Phase == PluginLifecyclePhase.Started;

            var affected = affectedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pendingGroups = LifecycleContextGroups
                .Where(group => group.Any(affected.Contains) &&
                                !LifecycleContexts.ContainsKey(GroupKey(group)))
                .Select(group => group.ToHashSet(StringComparer.OrdinalIgnoreCase))
                .ToList();
            foreach (var group in pendingGroups)
                foreach (var name in group)
                    if (LifecycleMetadatas.TryGetValue(name, out var metadata))
                        LifecycleFailedPlugins.Remove(metadata.FilePath);

            foreach (var group in pendingGroups)
            {
                var staged = await StageGroupLoadAsync(group);
                if (staged is null)
                {
                    foreach (var name in group.Where(LifecycleMetadatas.ContainsKey))
                        outcomes[name] = PluginLifecycleOutcome.Failed;
                    continue;
                }

                if (initialize)
                {
                    if (InitializeStagedPlugins(staged) is { } initializationFailure)
                    {
                        foreach (var name in group.Where(LifecycleMetadatas.ContainsKey))
                            outcomes[name] = PluginLifecycleOutcome.Failed;
                        try
                        {
                            await DisposeStagedGroupAsync(staged, flush: true);
                        }
                        catch (Exception cleanupFailure)
                        {
                            throw new AggregateException(
                                "插件组初始化及清理失败。",
                                initializationFailure,
                                cleanupFailure);
                        }
                        continue;
                    }
                }

                try
                {
                    await CommitStagedGroupLoadAsync(staged, initialize);
                }
                catch
                {
                    foreach (var name in group.Where(LifecycleMetadatas.ContainsKey))
                        outcomes[name] = PluginLifecycleOutcome.Failed;
                    throw;
                }

                if (initialize && deliverStarted && staged.Plugins.Count != 0)
                    startedPluginBatches.Add([.. staged.Plugins]);

                foreach (var name in group.Where(LifecycleMetadatas.ContainsKey))
                    outcomes[name] = IsPluginLoaded(name)
                        ? PluginLifecycleOutcome.Succeeded
                        : PluginLifecycleOutcome.Failed;
            }
        }

        static async Task<StagedGroupLoad?> StageGroupLoadAsync(HashSet<string> group)
        {
            var ordered = TopologicalOrder(group);
            var key = GroupKey(group);
            var ctx = new PluginLoadContext(key, ordered.Select(name => LifecycleMetadatas[name]));
            var staged = new StagedGroupLoad(group, key, ctx, []);

            foreach (var name in ordered)
            {
                var metadata = LifecycleMetadatas[name];
                var failure = await LoadIntoContextAsync(staged.Context, metadata, staged.Plugins);
                if (failure is null)
                    continue;

                try
                {
                    await DisposeStagedGroupAsync(staged, flush: false);
                }
                catch (Exception cleanupFailure)
                {
                    failure = new AggregateException(
                        "插件组加载及清理失败。",
                        failure,
                        cleanupFailure);
                }
                ReportPluginDiagnostic(failure);
                if (!LifecycleFailedPlugins.Contains(metadata.FilePath))
                    LifecycleFailedPlugins.Add(metadata.FilePath);
                return null;
            }

            return staged;
        }

        static Exception? InitializeStagedPlugins(StagedGroupLoad staged)
        {
            var availableGroupMembers = staged.Plugins
                .Select(InternalName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in staged.Plugins)
                if (!TryInitializePlugin(
                        plugin,
                        committed: false,
                        out var failure,
                        availableGroupMembers))
                    return failure;

            return null;
        }

        static async Task CommitStagedGroupLoadAsync(StagedGroupLoad staged, bool initialize)
        {
            try
            {
                LifecycleLoadedPlugins.AddRange(staged.Plugins);
                LifecycleContexts.Add(staged.Key, staged.Context);
                if (initialize)
                    CommitPendingRegistrations(staged.Plugins);
            }
            catch (Exception commitError)
            {
                try
                {
                    await DisposeStagedGroupAsync(staged, flush: initialize);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(
                        "插件 staged group 提交及清理失败。",
                        commitError,
                        cleanupError);
                }
                throw;
            }
        }

        static async Task DisposeStagedGroupAsync(StagedGroupLoad staged, bool flush)
        {
            var generations = staged.Plugins
                .AsEnumerable()
                .Reverse()
                .Select(RequireGeneration)
                .ToList();
            foreach (var generation in generations)
                _ = generation.Close();
            await CompletePendingUnloadsAsync(
                [new(staged.Names, staged.Key, staged.Context, generations, false)],
                clearAll: false,
                flush: flush);
        }

        static void MergeUnloadedMetadatas(IReadOnlyDictionary<string, PluginMetadata> scanned)
        {
            foreach (var (name, m) in scanned)
            {
                if (LifecycleMetadatas.ContainsKey(name)) continue;
                LifecycleMetadatas[name] = m;
            }
        }

        static bool IsPluginLoaded(string pluginName)
            => LifecycleLoadedPlugins.Any(plugin =>
                string.Equals(InternalName(plugin), pluginName, StringComparison.OrdinalIgnoreCase));

        static PendingPluginUnload PrepareUnloadGroup(HashSet<string> group)
        {
            var key = GroupKey(group);
            var loadedByName = LifecycleLoadedPlugins
                .Where(plugin => group.Contains(InternalName(plugin)))
                .ToDictionary(InternalName, StringComparer.OrdinalIgnoreCase);
            var generations = TopologicalOrder(group)
                .Reverse()
                .Where(loadedByName.ContainsKey)
                .Select(name => RequireGeneration(loadedByName[name]))
                .ToList();
            foreach (var generation in generations)
                _ = generation.Close();
            return new(
                group.ToHashSet(StringComparer.OrdinalIgnoreCase),
                key,
                LifecycleContexts[key],
                generations,
                true);
        }

        static Task CompleteFailedPluginLoadAsync(IPlugin plugin, bool flush)
        {
            var generation = GenerationFor(plugin);
            _ = generation.Close();
            return CompletePendingUnloadsAsync(
                [new([InternalName(plugin)], null, null, [generation], false)],
                clearAll: false,
                flush: flush);
        }

        static async Task CompletePendingUnloadsAsync(
            IReadOnlyList<PendingPluginUnload> pendingUnloads,
            bool clearAll,
            bool flush = true)
        {
            var generations = pendingUnloads
                .SelectMany(unload => unload.Generations)
                .Distinct()
                .ToList();
            List<Exception> failures = [];
            var closeTasks = generations.Select(generation => generation.Close()).ToArray();
            for (var i = 0; i < closeTasks.Length; i++)
            {
                try
                {
                    await closeTasks[i];
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException(
                        $"插件清理失败: plugin={InternalName(generations[i].Plugin)}, phase=Close",
                        ex));
                }
            }

            var plugins = generations
                .Select(generation => generation.Plugin)
                .ToList();
            for (var i = 0; i < plugins.Count; i++)
            {
                var pluginName = InternalName(plugins[i]);
                DisposePluginForUnload(plugins[i], pluginName, failures);

                if (flush)
                {
                    try
                    {
                        await TerminalUi.RequireHost().FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new InvalidOperationException(
                            $"插件清理失败: plugin={pluginName}, phase=Flush",
                            ex));
                    }
                }
            }

            FinishPendingUnloads(pendingUnloads, clearAll, generations, plugins, failures);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void DisposePluginForUnload(
            IPlugin plugin,
            string pluginName,
            List<Exception> failures)
        {
            using (HotkeyManager.RegisterScope(plugin))
            {
                try
                {
                    plugin.Dispose();
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException(
                        $"插件清理失败: plugin={pluginName}, phase=Dispose, " +
                        $"{DescribeException(ex)}{Environment.NewLine}{ex}"));
                }
            }

            try
            {
                HotkeyManager.UnregisterByOwner(plugin);
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException(
                    $"插件清理失败: plugin={pluginName}, phase=UnregisterHotkeys",
                    ex));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void FinishPendingUnloads(
            IReadOnlyList<PendingPluginUnload> pendingUnloads,
            bool clearAll,
            List<PluginGeneration> generations,
            List<IPlugin> plugins,
            List<Exception> failures)
        {
            try
            {
                foreach (var plugin in plugins)
                {
                    var index = LifecycleLoadedPlugins.FindIndex(candidate => ReferenceEquals(candidate, plugin));
                    if (index >= 0)
                        LifecycleLoadedPlugins.RemoveAt(index);
                    PluginGenerations.Remove(plugin);
                }
                RemoveAnalyzerMethods(plugins);

                foreach (var unload in pendingUnloads)
                {
                    if (unload.Key is not null)
                        LifecycleContexts.Remove(unload.Key);
                    if (!unload.RemoveGroupState)
                        continue;
                    foreach (var name in unload.Names)
                        LifecycleMetadatas.Remove(name);
                    LifecycleContextGroups.RemoveAll(group => group.SetEquals(unload.Names));
                }

                if (clearAll)
                {
                    LifecycleLoadedPlugins.Clear();
                    LifecycleMetadatas.Clear();
                    LifecycleFailedPlugins.Clear();
                    LifecycleContextGroups.Clear();
                    LifecycleContexts.Clear();
                    ClearAnalyzerRegistrations();
                }
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException("插件 registration/context 清理失败。", ex));
            }

            foreach (var plugin in plugins)
            {
                try { DisposeHostEventSubscriptions(plugin); }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException(
                        $"插件清理失败: plugin={InternalName(plugin)}, phase=HostEvents",
                        ex));
                }
            }
            if (clearAll)
            {
                try { ClearHostEventSubscriptions(); }
                catch (Exception ex) { failures.Add(new InvalidOperationException("插件 HostEvents 清理失败。", ex)); }
            }

            var contexts = pendingUnloads
                .Select(unload => unload.Context)
                .Where(context => context is not null)
                .Distinct()
                .ToList();
            foreach (var context in contexts)
            {
                try { context!.Unload(); }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException(
                        $"插件 ALC 清理失败: context={context!.Name}",
                        ex));
                }
            }

            foreach (var unload in pendingUnloads)
                unload.Release();
            contexts.Clear();
            plugins.Clear();
            generations.Clear();

            if (failures.Count != 0)
                throw new AggregateException("插件清理失败。", failures);
        }

    }
}
