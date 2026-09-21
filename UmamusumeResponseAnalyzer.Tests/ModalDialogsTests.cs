using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using Terminal.Gui.Input;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginReload")]
public sealed class ModalDialogsTests(PluginRuntimeFixture fixture)
{
    readonly TerminalGuiTestApp terminal = fixture.Terminal;

    [Fact]
    public async Task Select_ReturnsFocusedChoiceAndEscapeCancels()
    {
        var accepted = Task.Run(
            () => ModalDialogs.Select("选择项", ["first", "second"]),
            TestContext.Current.CancellationToken);
        await terminal.WaitForScreenAsync("second");
        await terminal.InjectAsync(Key.CursorDown);
        await terminal.InjectAsync(Key.Enter);
        Assert.Equal("second", await accepted);

        var cancelled = Task.Run(
            () => ModalDialogs.Select("取消选择", ["value"]),
            TestContext.Current.CancellationToken);
        await terminal.WaitForScreenAsync("取消选择");
        await terminal.InjectAsync(Key.Esc);
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);

        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(
            () => ModalDialogs.Select<string>("空选择", []),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Menu_ReturnsChoiceAndExternalCancellationClosesIt()
    {
        var accepted = Task.Run(
            () => ModalDialogs.Menu("原生菜单", ["开始", "设置"]),
            TestContext.Current.CancellationToken);
        await terminal.WaitForScreenAsync("设置");
        await terminal.InjectAsync(Key.CursorDown);
        await terminal.InjectAsync(Key.Enter);
        Assert.Equal("设置", await accepted);

        using var cancellation = new CancellationTokenSource();
        var cancelled = Task.Run(
            () => ModalDialogs.Menu(
                "Token 取消",
                ["开始"],
                cancellationToken: cancellation.Token),
            TestContext.Current.CancellationToken);
        await terminal.WaitForScreenAsync("Token 取消");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
    }

    [Fact]
    public async Task AskAndMultiSelect_ReturnValuesAndSurfaceCancellation()
    {
        var asked = Task.Run(
            () => ModalDialogs.Ask("输入", allowEmpty: false),
            TestContext.Current.CancellationToken);
        await terminal.WaitForScreenAsync("输入");
        await terminal.InjectAsync(Key.A);
        await terminal.InjectAsync(Key.Tab);
        await terminal.InjectAsync(Key.Enter);
        Assert.Equal("a", await asked);

        var selected = Task.Run(
            () => ModalDialogs.MultiSelect(
                "确认多选",
                ["a", "b", "c"],
                selected: ["a", "c"]),
            TestContext.Current.CancellationToken);
        await terminal.WaitForScreenAsync("确认多选");
        await terminal.InjectAsync(Key.Tab);
        await terminal.InjectAsync(Key.Enter);
        Assert.Equal(["a", "c"], await selected);

        using var cancellation = new CancellationTokenSource();
        var cancelled = Task.Run(
            () => ModalDialogs.Ask("取消输入", cancellationToken: cancellation.Token),
            TestContext.Current.CancellationToken);
        await terminal.WaitForScreenAsync("取消输入");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
    }

    [Theory]
    [InlineData("en-US", "Yes", "No", "Press Enter to return")]
    [InlineData("zh-CN", "是", "否", "按 Enter 返回")]
    [InlineData("ja-JP", "はい", "いいえ", "Enter キーで戻る")]
    public async Task ConfirmAndAcknowledge_ReturnVisibleUserDecisions(
        string culture, string yes, string no, string returnPrompt)
    {
        var originalCulture = i18n.Culture;
        i18n.Culture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            var confirm = Task.Run(
                () => ModalDialogs.Confirm("Confirm operation", defaultValue: true),
                TestContext.Current.CancellationToken);
            await terminal.WaitForScreenAsync("Confirm operation");
            var screen = await terminal.CaptureScreenAsync();
            Assert.Contains(yes, screen, StringComparison.Ordinal);
            Assert.Contains(no, screen, StringComparison.Ordinal);
            await terminal.InjectAsync(Key.Esc);
            Assert.False(await confirm);

            var acknowledge = Task.Run(
                () => ModalDialogs.Acknowledge(),
                TestContext.Current.CancellationToken);
            await terminal.WaitForScreenAsync(returnPrompt);
            await terminal.InjectAsync(Key.Enter);
            Assert.True(await acknowledge);
        }
        finally
        {
            i18n.Culture = originalCulture;
        }
    }

    [Fact]
    public async Task Progress_RendersRowsAndPropagatesFaultAndCancellation()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendered = ModalDialogs.RunProgressAsync(async (progress, cancellationToken) =>
        {
            progress.Report(new("first", "Download A", 25, 100));
            progress.Report(new("second", "Download B", 75, 100));
            await release.Task.WaitAsync(cancellationToken);
        }, TestContext.Current.CancellationToken);
        await terminal.WaitForScreenAsync("Download A");
        await terminal.WaitForScreenAsync("Download B");
        release.SetResult();
        await rendered;

        var faulted = ModalDialogs.RunProgressAsync(
            (_, _) => throw new InvalidOperationException("progress failed"),
            TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => faulted);
        Assert.Equal("progress failed", error.Message);

        using var cancellation = new CancellationTokenSource();
        var cancelled = ModalDialogs.RunProgressAsync(
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
            cancellation.Token);
        await terminal.WaitForScreenAsync(i18n.Dialog_Processing);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
    }
}
