using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Runtime.CompilerServices;
using UmamusumeResponseAnalyzer.Commands;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("HotkeyManager")]
public sealed class HostCommandsTests
{
    [Theory]
    [InlineData("en-US", "Startup", "Usage:")]
    [InlineData("zh-CN", "启动", "用法:")]
    [InlineData("ja-JP", "起動", "使用方法:")]
    public async Task LocalizedWorkspaceNamesPreserveCommandIdentity(
        string culture, string displayTitle, string usagePrefix)
    {
        var originalCulture = i18n.Culture;
        i18n.Culture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            var registry = new WorkspaceRegistry();
            var (ordinary, _) = registry.Create("Startup");
            var snapshot = new HostCommands.Snapshot(
                registry.SnapshotRegistrationOrder().Select(workspace => new HostCommands.WorkspaceItem(workspace)).ToArray(),
                registry.Bootstrap,
                []);

            Assert.Equal("启动", registry.Bootstrap.Title);
            Assert.Equal(displayTitle, registry.Bootstrap.DisplayTitle);
            Assert.Same(registry.Bootstrap, registry.Create("启动").Workspace);
            Assert.NotSame(registry.Bootstrap, ordinary);
            Assert.Equal("Startup", ordinary.DisplayTitle);

            var list = Assert.IsType<HostCommands.Result>(await HostCommands.ExecuteAsync("/workspace list", snapshot));
            var expected = displayTitle == "启动" ? "* 启动" : $"* {displayTitle} (启动)";
            Assert.Equal([expected, "  Startup"], list.Display!.Items.Select(item => item.Text));
            Assert.Contains("/workspace switch 启动", HostCommands.Complete("/workspace switch ", snapshot));
            Assert.Same(registry.Bootstrap,
                (await HostCommands.ExecuteAsync("/workspace switch 启动", snapshot))!.SwitchWorkspace);
            Assert.Same(ordinary,
                (await HostCommands.ExecuteAsync("/workspace switch Startup", snapshot))!.SwitchWorkspace);

            var workspaceHelp = Assert.IsType<HostCommands.Result>(
                await HostCommands.ExecuteAsync("/workspace unknown", snapshot));
            var pluginHelp = Assert.IsType<HostCommands.Result>(
                await HostCommands.ExecuteAsync("/plugin reload", snapshot));
            Assert.Equal((i18n.Command_WorkspaceUsage, UiSeverity.Warning),
                (workspaceHelp.Message, workspaceHelp.Severity));
            Assert.Equal((i18n.Command_PluginUsage, UiSeverity.Warning),
                (pluginHelp.Message, pluginHelp.Severity));
            Assert.StartsWith(usagePrefix, workspaceHelp.Message!);
            Assert.StartsWith(usagePrefix, pluginHelp.Message!);
            var parseError = Assert.Throws<FormatException>(() =>
                HostCommands.ParseWorkspaceTitle("\"title\\n\""));
            Assert.Equal(string.Format(i18n.Command_QuotedTitleUnsupportedEscape, "n"), parseError.Message);
            Assert.Contains("\\n", parseError.Message, StringComparison.Ordinal);
        }
        finally
        {
            i18n.Culture = originalCulture;
        }
    }

    static HostCommands.Snapshot BootstrapOnlySnapshot()
    {
        var registry = new WorkspaceRegistry();
        return new([new(registry.Bootstrap)], registry.Bootstrap, []);
    }

    [Fact]
    public void Tokenize_PreservesTheUnquotedRemainderAndUsesLiteralSpace()
    {
        var (token, remainder) = HostCommands.Tokenize("  switch  First  Workspace  ");
        var (tabbedToken, tabbedRemainder) = HostCommands.Tokenize("switch\tFirst");

        Assert.Equal("switch", token);
        Assert.Equal("First  Workspace", remainder);
        Assert.Equal("switch\tFirst", tabbedToken);
        Assert.Equal(string.Empty, tabbedRemainder);
    }

    [Fact]
    public void ParseWorkspaceTitle_PreservesTextAndSupportedEscapes()
    {
        Assert.Equal("First  Workspace", HostCommands.ParseWorkspaceTitle("First  Workspace"));
        Assert.Equal(
            "Second \"Workspace\" \\ logs",
            HostCommands.ParseWorkspaceTitle("\"Second \\\"Workspace\\\" \\\\ logs\""));
    }

    [Theory]
    [InlineData("\"title\" trailing", nameof(i18n.Command_QuotedTitleTrailingContent))]
    [InlineData("\"   \"", nameof(i18n.Command_QuotedTitleEmpty))]
    [InlineData("\"title\\", nameof(i18n.Command_QuotedTitleTrailingSlash))]
    [InlineData("\"title\\n\"", nameof(i18n.Command_QuotedTitleUnsupportedEscape))]
    [InlineData("\"unterminated", nameof(i18n.Command_QuotedTitleUnclosed))]
    public void ParseWorkspaceTitle_RejectsMalformedQuotes(string value, string resourceKey)
    {
        var error = Assert.Throws<FormatException>(() => HostCommands.ParseWorkspaceTitle(value));

        Assert.Equal(string.Format(i18n.ResourceManager.GetString(resourceKey, i18n.Culture)!, "n"), error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesEmptyUnknownAndUsageResults()
    {
        Assert.Null(await HostCommands.ExecuteAsync("not a command", BootstrapOnlySnapshot()));

        var empty = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/   ", BootstrapOnlySnapshot()));
        var unknown = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/missing", BootstrapOnlySnapshot()));
        var workspaceUsage = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/workspace list extra", BootstrapOnlySnapshot()));
        var pluginUsage = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/plugin reload", BootstrapOnlySnapshot()));

        Assert.Equal((i18n.Command_Empty, UiSeverity.Warning), (empty.Message, empty.Severity));
        Assert.Equal((string.Format(i18n.Command_Unknown, "missing"), UiSeverity.Warning), (unknown.Message, unknown.Severity));
        Assert.Equal((i18n.Command_WorkspaceUsage, UiSeverity.Warning), (workspaceUsage.Message, workspaceUsage.Severity));
        Assert.Equal((i18n.Command_PluginUsage, UiSeverity.Warning), (pluginUsage.Message, pluginUsage.Severity));
    }

    [Fact]
    public async Task WorkspaceCommands_UseCanonicalHandlesAndPreserveDisplayOrder()
    {
        var registry = new WorkspaceRegistry();
        var (first, _) = registry.Create("First");
        var (second, _) = registry.Create("Second \"Workspace\"");
        var (alias, created) = registry.Create("SECOND \"WORKSPACE\"");
        var snapshot = new HostCommands.Snapshot(
            registry.SnapshotRegistrationOrder()
                .Select(workspace => new HostCommands.WorkspaceItem(
                    workspace,
                    ReferenceEquals(workspace, first) ? "Ctrl+1" : null))
                .ToArray(),
            second,
            []);

        var list = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/workspace list", snapshot));
        var selector = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/workspace", snapshot));
        var switched = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync(
                "/workspace switch \"Second \\\"Workspace\\\"\"",
                snapshot));
        var missing = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/workspace switch Missing", snapshot));
        var empty = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/workspace", BootstrapOnlySnapshot()));

        Assert.False(created);
        Assert.Same(second, alias);
        var listDisplay = Assert.IsType<HostCommands.Display>(list.Display);
        var selectorDisplay = Assert.IsType<HostCommands.Display>(selector.Display);
        Assert.Equal(i18n.Command_WorkspacesTitle, listDisplay.Title);
        Assert.Null(listDisplay.SelectedIndex);
        var bootstrapLabel = registry.Bootstrap.DisplayTitle == Workspace.BootstrapTitle
            ? registry.Bootstrap.DisplayTitle
            : $"{registry.Bootstrap.DisplayTitle} ({Workspace.BootstrapTitle})";
        Assert.Equal([$"  {bootstrapLabel}", "  First [Ctrl+1]", "* Second \"Workspace\""],
            listDisplay.Items.Select(item => item.Text).ToArray());
        Assert.Same(registry.Bootstrap, listDisplay.Items[0].Workspace);
        Assert.Same(first, listDisplay.Items[1].Workspace);
        Assert.Same(second, listDisplay.Items[2].Workspace);
        Assert.Equal(2, selectorDisplay.SelectedIndex);
        Assert.Same(second, switched.SwitchWorkspace);
        Assert.Null(switched.Message);
        var bootstrapOnly = Assert.IsType<HostCommands.Display>(empty.Display);
        Assert.Equal([$"* {bootstrapLabel}"], bootstrapOnly.Items.Select(item => item.Text).ToArray());
        Assert.Equal(0, bootstrapOnly.SelectedIndex);
        Assert.Equal((string.Format(i18n.Command_WorkspaceNotFound, "Missing"), UiSeverity.Warning),
            (missing.Message, missing.Severity));
    }

    [Fact]
    public void RemovedWorkspaceIsNotRetainedByRegistry()
    {
        var registry = new WorkspaceRegistry();
        var removed = CreateAndRemoveWorkspace(registry);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(removed.IsAlive);
        GC.KeepAlive(registry);
    }

    [Fact]
    public void RegistryOwnsCanonicalProtectedBootstrapAndPreservesNormalTombstones()
    {
        var registry = new WorkspaceRegistry();
        var bootstrap = registry.Bootstrap;

        Assert.Equal(Workspace.BootstrapTitle, bootstrap.Title);
        Assert.Same(bootstrap, registry.Current);
        Assert.Equal([bootstrap], registry.SnapshotRegistrationOrder());
        var (bootstrapAlias, bootstrapCreated) = registry.Create("启动");
        Assert.Same(bootstrap, bootstrapAlias);
        Assert.False(bootstrapCreated);

        var error = Assert.Throws<InvalidOperationException>(() =>
            registry.Remove(bootstrap, out _));
        Assert.Equal(string.Format(i18n.Workspace_BootstrapCannotRemove, Workspace.BootstrapTitle), error.Message);
        Assert.Same(bootstrap, registry.Current);
        Assert.Equal([bootstrap], registry.SnapshotRegistrationOrder());

        var (workspace, created) = registry.Create("Ordinary");
        Assert.True(created);
        registry.SwitchTo(workspace);
        Assert.True(registry.Remove(workspace, out var replacement));
        Assert.Same(bootstrap, replacement);
        Assert.Same(bootstrap, registry.Current);
        Assert.True(workspace.IsRemoved);
        Assert.False(registry.Remove(workspace, out replacement));
        Assert.Same(bootstrap, replacement);
        var (nextGeneration, recreated) = registry.Create("Ordinary");
        Assert.True(recreated);
        Assert.NotSame(workspace, nextGeneration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CreateAndRemoveWorkspace(WorkspaceRegistry registry)
    {
        var (workspace, _) = registry.Create("Removed");
        Assert.True(registry.Remove(workspace, out _));
        Assert.False(registry.Remove(workspace, out _));
        return new(workspace);
    }

    [Fact]
    public async Task WorkspaceCommand_PreservesUnquotedInternalSpacesAndQuoteErrors()
    {
        var registry = new WorkspaceRegistry();
        var (workspace, _) = registry.Create("Two  Spaces");
        var snapshot = new HostCommands.Snapshot([new(workspace)], workspace, []);

        var switched = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/workspace switch Two  Spaces", snapshot));
        var malformed = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/workspace switch \"unterminated", snapshot));

        Assert.Same(workspace, switched.SwitchWorkspace);
        Assert.Equal(
            (i18n.Command_QuotedTitleUnclosed, UiSeverity.Error),
            (malformed.Message, malformed.Severity));
    }

    [Fact]
    public async Task PluginListAndNotFound_UseSnapshotDisplayModel()
    {
        var plugins = new PluginManager.PluginRuntimeStatus[]
        {
            new("Internal", "显示名", "Author", new(1, 2, 3), true, true),
            new("Plain", "Plain", string.Empty, null, false, true),
            new("Broken", "Broken", string.Empty, null, false, false)
        };
        var snapshot = BootstrapOnlySnapshot() with
        {
            Plugins = plugins
        };

        var list = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/plugin", snapshot));
        var missing = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync("/plugin reload Missing", snapshot));

        var display = Assert.IsType<HostCommands.Display>(list.Display);
        Assert.Equal(i18n.Command_PluginsTitle, display.Title);
        Assert.Equal(
            [
                $"{i18n.Command_PluginStateLoaded} Internal (显示名) v1.2.3 {string.Format(i18n.Command_PluginAuthor, "Author")}",
                $"{i18n.Command_PluginStateUnloaded} Plain",
                $"{i18n.Command_PluginStateFailed} Broken"
            ],
            display.Items.Select(item => item.Text).ToArray());
        Assert.Equal((string.Format(i18n.Command_PluginNotFound, "Missing"), UiSeverity.Warning),
            (missing.Message, missing.Severity));
    }

    [Fact]
    public void Completion_IsPureDeterministicAndQuotesWorkspaceTitles()
    {
        var registry = new WorkspaceRegistry();
        var (alpha, _) = registry.Create("Alpha");
        var (quoted, _) = registry.Create("Second \"Workspace\"");
        var (slash, _) = registry.Create("Back\\Slash");
        var snapshot = new HostCommands.Snapshot(
            registry.SnapshotRegistrationOrder()
                .Select(workspace => new HostCommands.WorkspaceItem(workspace))
                .ToArray(),
            alpha,
            [
                new PluginManager.PluginRuntimeStatus("Zulu", "Zulu", string.Empty, null, false, true),
                new PluginManager.PluginRuntimeStatus(
                    "alpha-plugin",
                    "alpha-plugin",
                    string.Empty,
                    null,
                    true,
                    true)
            ]);

        Assert.Equal(["/plugin", "/workspace"], HostCommands.Complete("/", snapshot));
        Assert.Equal(["/workspace switch"], HostCommands.Complete("/workspace s", snapshot));
        Assert.Equal(
            ["/workspace switch \"Second \\\"Workspace\\\"\""],
            HostCommands.Complete("/workspace switch Sec", snapshot));
        Assert.Equal(
            ["/workspace switch \"Back\\\\Slash\""],
            HostCommands.Complete("/workspace switch B", snapshot));
        Assert.Equal(
            ["/plugin reload alpha-plugin", "/plugin reload Zulu"],
            HostCommands.Complete("/plugin reload ", snapshot));

        var first = HostCommands.Complete("/workspace switch ", snapshot);
        var second = HostCommands.Complete("/workspace switch ", snapshot);
        Assert.Equal(first.ToArray(), second.ToArray());
    }
}
