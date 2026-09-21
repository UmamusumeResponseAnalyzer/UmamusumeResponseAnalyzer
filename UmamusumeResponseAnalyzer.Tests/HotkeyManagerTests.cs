using System.Collections;
using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[CollectionDefinition("HotkeyManager", DisableParallelization = true)]
public sealed class HotkeyManagerCollection;

[Collection("HotkeyManager")]
public sealed class HotkeyManagerTests : IDisposable
{
    public HotkeyManagerTests() => Reset();

    public void Dispose() => Reset();

    static Func<Task> NoopHandler => () => Task.CompletedTask;

    static void Reset()
    {
        HotkeyManager.UnregisterAll();
        HotkeyManager.OverlaySink = null;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.FromSeconds(3);
    }

    [Theory]
    [InlineData(ConsoleKey.K, ConsoleModifiers.Control | ConsoleModifiers.Alt | ConsoleModifiers.Shift, "Ctrl+Alt+Shift+K")]
    [InlineData(ConsoleKey.UpArrow, ConsoleModifiers.None, "↑")]
    [InlineData(ConsoleKey.DownArrow, ConsoleModifiers.Control, "Ctrl+↓")]
    [InlineData(ConsoleKey.Oem2, ConsoleModifiers.None, "/")]
    [InlineData(ConsoleKey.Spacebar, ConsoleModifiers.Alt, "Alt+Space")]
    public void FormatKeyCombo_UsesStableReadableNames(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        string expected)
        => Assert.Equal(expected, HotkeyManager.FormatKeyCombo(key, modifiers));

    [Theory]
    [InlineData(ConsoleKey.S)]
    [InlineData(ConsoleKey.Q)]
    [InlineData(ConsoleKey.Z)]
    public void Register_RejectsCtrlTerminalFlowControlKeys(ConsoleKey key)
    {
        Assert.Throws<InvalidOperationException>(() =>
            HotkeyManager.Register(key, ConsoleModifiers.Control, "reserved", NoopHandler));
        Assert.Empty(HotkeyManager.Hotkeys);
    }

    [Fact]
    public async Task FourRegisterOverloads_InvokeAndExposeTheirResults()
    {
        var sink = new RecordingOverlaySink();
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        var calls = new List<string>();

        HotkeyManager.Register(ConsoleKey.F1, ConsoleModifiers.Alt, "task+mods", () =>
        {
            calls.Add("task+mods");
            return Task.CompletedTask;
        });
        HotkeyManager.Register(ConsoleKey.F2, ConsoleModifiers.Control, "context+mods", context =>
        {
            calls.Add("context+mods");
            context.AddLine("context+mods output");
            return Task.CompletedTask;
        });
        HotkeyManager.Register(ConsoleKey.F3, "task", () =>
        {
            calls.Add("task");
            return Task.CompletedTask;
        });
        HotkeyManager.Register(ConsoleKey.F4, "context", context =>
        {
            calls.Add("context");
            context.AddLine("context output");
            return Task.CompletedTask;
        });

        Assert.True(await PressAsync(KeyCode.F1 | KeyCode.AltMask));
        Assert.True(await PressAsync(KeyCode.F2 | KeyCode.CtrlMask));
        Assert.Equal("context+mods output", Assert.Single(sink.Popup!.Lines).Text);
        Assert.True(await PressAsync(KeyCode.F3));
        Assert.Null(sink.Popup);
        Assert.True(await PressAsync(KeyCode.F4));

        Assert.Equal(["task+mods", "context+mods", "task", "context"], calls);
        Assert.Equal("context output", Assert.Single(sink.Popup!.Lines).Text);
        Assert.Equal(4, HotkeyManager.Hotkeys.Count);
    }

    [Fact]
    public void Hotkeys_IsImmutableSnapshotAndDuplicateRegistrationReplacesTheEntry()
    {
        HotkeyManager.Register(ConsoleKey.F1, "first", NoopHandler);
        var snapshot = HotkeyManager.Hotkeys;
        var first = snapshot[(ConsoleKey.F1, ConsoleModifiers.None)];
        var mutation = new HotkeyManager.HotkeyEntry("mutation", NoopHandler);

        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), HotkeyManager.HotkeyEntry>)snapshot)
                .Add((ConsoleKey.F12, ConsoleModifiers.None), mutation));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary)snapshot).Add((ConsoleKey.F12, ConsoleModifiers.None), mutation));

        HotkeyManager.Register(ConsoleKey.F1, "replacement", NoopHandler);
        HotkeyManager.Register(ConsoleKey.F2, "second", NoopHandler);

        Assert.Single(snapshot);
        Assert.Same(first, snapshot[(ConsoleKey.F1, ConsoleModifiers.None)]);
        Assert.Equal("replacement", HotkeyManager.Hotkeys[(ConsoleKey.F1, ConsoleModifiers.None)].Description);
        Assert.Equal(2, HotkeyManager.Hotkeys.Count);
    }

    [Fact]
    public void Unregister_AllAndByOwnerRemoveOnlyTheirRegistrations()
    {
        var owner = new object();
        var otherOwner = new object();
        using (HotkeyManager.RegisterScope(owner))
        {
            HotkeyManager.Register(ConsoleKey.F1, "owned", NoopHandler);
            HotkeyManager.Register(ConsoleKey.F2, "owned", NoopHandler);
        }
        using (HotkeyManager.RegisterScope(otherOwner))
            HotkeyManager.Register(ConsoleKey.F3, "other", NoopHandler);

        Assert.True(HotkeyManager.Unregister(ConsoleKey.F1));
        Assert.False(HotkeyManager.Unregister(ConsoleKey.F1));
        Assert.Equal(1, HotkeyManager.UnregisterByOwner(owner));
        Assert.Single(HotkeyManager.Hotkeys);

        HotkeyManager.UnregisterAll();
        Assert.Empty(HotkeyManager.Hotkeys);
    }

    [Fact]
    public async Task RegisterScope_FlowsAcrossAwaitAndRealAsyncCallerThenRestores()
    {
        var owner = new object();

        await RegisterFromAsyncCaller(owner);
        HotkeyManager.Register(ConsoleKey.F2, "unowned", NoopHandler);

        Assert.Same(owner, HotkeyManager.Hotkeys[(ConsoleKey.F1, ConsoleModifiers.None)].Owner);
        Assert.Null(HotkeyManager.Hotkeys[(ConsoleKey.F2, ConsoleModifiers.None)].Owner);
    }

    [Fact]
    public async Task OwnedCallback_PropagatesOwnerToNestedRegistration()
    {
        var owner = new object();
        var nestedCalls = 0;
        using (HotkeyManager.RegisterScope(owner))
        {
            HotkeyManager.Register(ConsoleKey.F1, "owner callback", () =>
            {
                HotkeyManager.Register(ConsoleKey.F2, "nested", () =>
                {
                    nestedCalls++;
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
            });
        }

        Assert.True(await PressAsync(KeyCode.F1));
        Assert.Equal(2, HotkeyManager.UnregisterByOwner(owner));
        Assert.False(await PressAsync(KeyCode.F1));
        Assert.False(await PressAsync(KeyCode.F2));
        Assert.Equal(0, nestedCalls);
    }

    [Fact]
    public async Task GlobalCallback_ClearsAmbientOwnerForNestedRegistration()
    {
        var ambientOwner = new object();
        var nestedCalls = 0;
        HotkeyManager.Register(ConsoleKey.F1, "global callback", () =>
        {
            HotkeyManager.Register(ConsoleKey.F2, "nested global", () =>
            {
                nestedCalls++;
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        });

        using (HotkeyManager.RegisterScope(ambientOwner))
            Assert.True(await PressAsync(KeyCode.F1));

        Assert.Null(HotkeyManager.Hotkeys[(ConsoleKey.F2, ConsoleModifiers.None)].Owner);
        Assert.Equal(0, HotkeyManager.UnregisterByOwner(ambientOwner));
        Assert.True(await PressAsync(KeyCode.F2));
        Assert.Equal(1, nestedCalls);
    }

    [Fact]
    public async Task OwnerCleanup_DoesNotAffectAnotherHandlerSharingTheSameWorkspace()
    {
        var sharedWorkspace = new Workspace("shared");
        var removedOwner = new object();
        var keptOwner = new object();
        Workspace? observed = null;
        using (HotkeyManager.RegisterScope(removedOwner))
            HotkeyManager.Register(ConsoleKey.F1, "removed", () => Task.CompletedTask);
        using (HotkeyManager.RegisterScope(keptOwner))
        {
            HotkeyManager.Register(ConsoleKey.F2, "kept", () =>
            {
                observed = sharedWorkspace;
                return Task.CompletedTask;
            });
        }

        Assert.Equal(1, HotkeyManager.UnregisterByOwner(removedOwner));
        Assert.False(await PressAsync(KeyCode.F1));
        Assert.True(await PressAsync(KeyCode.F2));
        Assert.Same(sharedWorkspace, observed);
        Assert.Equal("shared", sharedWorkspace.Title);
    }

    [Fact]
    public async Task NotificationShortcutRegistrationId_BindsRemovalTtlAndOwnerLifetime()
    {
        var removedOwner = new object();
        var keptOwner = new object();
        var calls = new List<string>();
        long removedId;
        long keptId;
        using (HotkeyManager.RegisterScope(removedOwner))
        {
            removedId = HotkeyManager.RegisterNotificationShortcuts(
                DateTimeOffset.Now.AddMinutes(1),
                [new UiShortcut(ConsoleKey.F8, () =>
                {
                    calls.Add("removed");
                    return Task.CompletedTask;
                })]);
        }
        using (HotkeyManager.RegisterScope(keptOwner))
        {
            keptId = HotkeyManager.RegisterNotificationShortcuts(
                DateTimeOffset.Now.AddMinutes(1),
                [new UiShortcut(ConsoleKey.F8, () =>
                {
                    calls.Add("kept");
                    return Task.CompletedTask;
                })]);
        }

        Assert.NotEqual(0, removedId);
        Assert.NotEqual(0, keptId);
        Assert.True(await PressAsync(KeyCode.F8));
        Assert.Equal(["kept"], calls);

        Assert.Equal(1, HotkeyManager.UnregisterByOwner(keptOwner));
        Assert.True(await PressAsync(KeyCode.F8));
        Assert.Equal(["kept", "removed"], calls);

        HotkeyManager.UnregisterNotificationShortcuts(removedId);
        Assert.False(await PressAsync(KeyCode.F8));

        HotkeyManager.RegisterNotificationShortcuts(
            DateTimeOffset.Now.AddMilliseconds(-1),
            [new UiShortcut(ConsoleKey.F9, NoopHandler)]);
        Assert.False(await PressAsync(KeyCode.F9));
    }

    [Fact]
    public async Task Dispatch_SerializesCallbacksInArrivalOrder()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var concurrentCallbacks = 0;
        var maxConcurrentCallbacks = 0;
        HotkeyManager.Register(ConsoleKey.F1, "first", async () =>
        {
            order.Add("first:start");
            maxConcurrentCallbacks = Math.Max(maxConcurrentCallbacks, Interlocked.Increment(ref concurrentCallbacks));
            firstEntered.SetResult();
            await releaseFirst.Task;
            Interlocked.Decrement(ref concurrentCallbacks);
            order.Add("first:end");
        });
        HotkeyManager.Register(ConsoleKey.F2, "second", () =>
        {
            order.Add("second");
            maxConcurrentCallbacks = Math.Max(maxConcurrentCallbacks, Interlocked.Increment(ref concurrentCallbacks));
            Interlocked.Decrement(ref concurrentCallbacks);
            return Task.CompletedTask;
        });

        var first = PressAsync(KeyCode.F1);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = PressAsync(KeyCode.F2);
        Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(30)));
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, maxConcurrentCallbacks);
        Assert.Equal(["first:start", "first:end", "second"], order);
    }

    [Fact]
    public async Task CallbackCanAwaitWorkThatReentersRegistrationState()
    {
        HotkeyManager.Register(ConsoleKey.F1, "reentrant", async () =>
        {
            await Task.Run(() => HotkeyManager.Register(ConsoleKey.F2, "nested", NoopHandler))
                .WaitAsync(TimeSpan.FromSeconds(2));
        });

        Assert.True(await PressAsync(KeyCode.F1));
        Assert.Contains((ConsoleKey.F2, ConsoleModifiers.None), HotkeyManager.Hotkeys.Keys);
    }

    [Fact]
    public async Task PopupRenderFailure_RollsBackStateAndAutoCloseBeforeRethrowing()
    {
        var showCalls = 0;
        var sink = new RecordingOverlaySink
        {
            ShowPopupCallout = () =>
            {
                if (Interlocked.Increment(ref showCalls) == 1)
                    throw new InvalidOperationException("render failed");
            }
        };
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.FromMilliseconds(20);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HotkeyManager.ShowPopup(new HotkeyContext().AddLine("failed")));

        Assert.Equal("render failed", exception.Message);
        Assert.False(HotkeyManager.HasPriorityPopup);
        Assert.Null(sink.Popup);

        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("next"));
        await Task.Delay(50);
        Assert.True(HotkeyManager.HasPriorityPopup);
        Assert.Equal("next", Assert.Single(sink.Popup!.Lines).Text);
    }

    [Fact]
    public async Task ClearingSinkClearsPopupAndCancelsTimer()
    {
        var sink = new RecordingOverlaySink();
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.FromMilliseconds(30);
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("visible"));

        HotkeyManager.OverlaySink = null;

        Assert.False(HotkeyManager.HasPriorityPopup);
        Assert.Null(sink.Popup);
        var callouts = sink.PopupCalloutCount;
        await Task.Delay(70);
        Assert.Equal(callouts, sink.PopupCalloutCount);
    }

    [Fact]
    public void SinkBinding_IsIdempotentRejectsReplacementAndAllowsFreshAttachAfterDetach()
    {
        var first = new RecordingOverlaySink();
        var second = new RecordingOverlaySink();
        HotkeyManager.OverlaySink = first;
        HotkeyManager.OverlaySink = first;

        Assert.Throws<InvalidOperationException>(() => HotkeyManager.OverlaySink = second);

        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("first"));
        HotkeyManager.OverlaySink = null;
        Assert.Null(first.Popup);
        Assert.False(HotkeyManager.HasPriorityPopup);

        HotkeyManager.OverlaySink = second;
        Assert.Null(second.Popup);
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("second"));
        Assert.Equal("second", Assert.Single(second.Popup!.Lines).Text);
    }

    [Fact]
    public void Detach_PropagatesSinkFailureAfterClearingManagerState()
    {
        var sink = new RecordingOverlaySink
        {
            HidePopupCallout = () => throw new InvalidOperationException("sink stopped")
        };
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("visible"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HotkeyManager.OverlaySink = null);

        Assert.Equal("sink stopped", exception.Message);
        Assert.False(HotkeyManager.HasPriorityPopup);
        Assert.Null(HotkeyManager.OverlaySink);
    }

    [Fact]
    public async Task Popup_NavigatesClampsSelectsAndClosesWithVisibleOutput()
    {
        var sink = new RecordingOverlaySink { PopupVisibleLineCount = 2 };
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        var confirmedLine = -1;
        HotkeyManager.ShowPopup(new HotkeyPopup(
            [new("title"), new("first"), new("second")],
            Selection: new([1, 2], 0, line =>
            {
                confirmedLine = line;
                return Task.CompletedTask;
            })));

        await PressAsync(KeyCode.CursorDown);
        await PressAsync(KeyCode.CursorDown);

        Assert.Equal(1, sink.Popup?.Selection?.SelectedIndex);
        Assert.Equal(1, sink.Popup?.ScrollOffset);

        await PressAsync(KeyCode.Enter);
        Assert.Equal(2, confirmedLine);
        Assert.Null(sink.Popup);
    }

    [Fact]
    public async Task PopupNavigationUsesTheSameKeysForScrollingAndSelection()
    {
        var sink = new RecordingOverlaySink { PopupVisibleLineCount = 3 };
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        foreach (var selectable in new[] { false, true })
        {
            HotkeyManager.ShowPopup(new HotkeyPopup(
                [.. Enumerable.Range(0, 20).Select(index => new HotkeyPopupLine($"line-{index}"))],
                Selection: selectable ? new([.. Enumerable.Range(0, 20)], 0, _ => Task.CompletedTask) : null));
            foreach (var (key, expected) in new[]
            {
                (KeyCode.CursorDown, 1), (KeyCode.PageDown, 6),
                (KeyCode.CursorUp, 5), (KeyCode.PageUp, 0),
                (KeyCode.End, selectable ? 19 : 17), (KeyCode.Home, 0)
            })
            {
                await PressAsync(key);
                Assert.Equal(expected, selectable ? sink.Popup!.Selection!.SelectedIndex : sink.Popup!.ScrollOffset);
            }
            await PressAsync(KeyCode.Esc);
            Assert.Null(sink.Popup);
        }
    }

    [Fact]
    public async Task PopupAutoClose_ClosesCurrentPopupAndCanceledTimerCannotCloseReplacement()
    {
        var sink = new RecordingOverlaySink();
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.FromMilliseconds(30);
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("auto-close"));

        await WaitUntilAsync(() => sink.Popup is null);

        HotkeyManager.PopupAutoCloseDelay = TimeSpan.FromMilliseconds(50);
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("old"));
        await Task.Delay(10);
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("replacement"));
        await Task.Delay(80);

        Assert.Equal("replacement", Assert.Single(sink.Popup!.Lines).Text);
    }

    [Fact]
    public async Task InputPriority_IsPopupNotificationWorkspaceThenPersistent()
    {
        var sink = new RecordingOverlaySink
        {
            HandleWorkspaceCommand = command => command == Command.Up
        };
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        var calls = new List<string>();
        HotkeyManager.Register(ConsoleKey.F8, "persistent", () =>
        {
            calls.Add("persistent");
            return Task.CompletedTask;
        });
        var notification = HotkeyManager.RegisterNotificationShortcuts(
            DateTimeOffset.Now.AddMinutes(1),
            [new UiShortcut(ConsoleKey.F8, () =>
            {
                calls.Add("notification");
                return Task.CompletedTask;
            })]);
        HotkeyManager.ShowPopup(new HotkeyContext()
            .AddLine("popup")
            .BindShortcut(new(ConsoleKey.F8, () =>
            {
                calls.Add("popup");
                return Task.CompletedTask;
            })));

        Assert.True(await PressAsync(KeyCode.F8));
        Assert.Equal(["popup"], calls);
        Assert.True(await PressAsync(KeyCode.Esc));
        Assert.True(await PressAsync(KeyCode.F8));
        Assert.Equal(["popup", "notification"], calls);

        HotkeyManager.UnregisterNotificationShortcuts(notification);
        Assert.True(await PressAsync(KeyCode.CursorUp));
        Assert.True(await PressAsync(KeyCode.F8));
        Assert.Equal(["popup", "notification", "persistent"], calls);
        Assert.Equal(Command.Up, Assert.Single(sink.WorkspaceCommands));
    }

    [Fact]
    public async Task PopupShortcut_ModifiersMatchExactlyAndDoNotChangePopupLifetime()
    {
        var sink = new RecordingOverlaySink();
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        var cases = new[]
        {
            (ConsoleModifiers.None, KeyCode.F8, KeyCode.F8 | KeyCode.CtrlMask),
            (ConsoleModifiers.Control, KeyCode.F8 | KeyCode.CtrlMask, KeyCode.F8),
            (ConsoleModifiers.Alt, KeyCode.F8 | KeyCode.AltMask, KeyCode.F8 | KeyCode.ShiftMask),
            (ConsoleModifiers.Shift, KeyCode.F8 | KeyCode.ShiftMask, KeyCode.F8 | KeyCode.AltMask),
            (ConsoleModifiers.Control | ConsoleModifiers.Alt,
                KeyCode.F8 | KeyCode.CtrlMask | KeyCode.AltMask,
                KeyCode.F8 | KeyCode.CtrlMask)
        };

        foreach (var (modifiers, matchingKey, wrongKey) in cases)
        {
            var calls = 0;
            HotkeyManager.ShowPopup(new HotkeyContext()
                .AddLine(modifiers.ToString())
                .BindShortcut(new(ConsoleKey.F8, () =>
                {
                    calls++;
                    return Task.CompletedTask;
                }, modifiers)));

            Assert.False(await PressAsync(wrongKey));
            Assert.Equal(0, calls);
            Assert.NotNull(sink.Popup);

            Assert.True(await PressAsync(matchingKey));
            Assert.Equal(1, calls);
            Assert.NotNull(sink.Popup);
            Assert.True(HotkeyManager.HasPriorityPopup);
            Assert.True(await PressAsync(KeyCode.Esc));
        }
    }

    [Fact]
    public async Task OwnerCleanup_RemovesActivePopupShortcutWithoutClosingPopup()
    {
        var sink = new RecordingOverlaySink();
        var owner = new object();
        var calls = 0;
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        using (HotkeyManager.RegisterScope(owner))
        {
            HotkeyManager.ShowPopup(new HotkeyContext()
                .AddLine("still visible")
                .BindShortcut(new(ConsoleKey.F8, () =>
                {
                    calls++;
                    return Task.CompletedTask;
                })));
        }

        Assert.Equal(1, HotkeyManager.UnregisterByOwner(owner));
        Assert.False(await PressAsync(KeyCode.F8));
        Assert.Equal(0, calls);
        Assert.True(HotkeyManager.HasPriorityPopup);
        Assert.Equal("still visible", Assert.Single(sink.Popup!.Lines).Text);
    }

    [Fact]
    public async Task MouseWheel_UsesTerminalGuiStepsAndSuppressesModifiedOrPopupInput()
    {
        var sink = new RecordingOverlaySink { HandleWorkspaceCommand = _ => true };
        HotkeyManager.OverlaySink = sink;

        await HotkeyManager.HandleMouseWheelAsync(2, hasModifiers: false);
        await HotkeyManager.HandleMouseWheelAsync(-2, hasModifiers: false);
        await HotkeyManager.HandleMouseWheelAsync(1, hasModifiers: true);
        await HotkeyManager.HandleMouseWheelAsync(1, hasModifiers: false, isHorizontal: true);
        Assert.Equal(
            [Command.Up, Command.Up, Command.Down, Command.Down],
            sink.WorkspaceCommands);

        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("popup"));
        await HotkeyManager.HandleMouseWheelAsync(1, hasModifiers: false);
        Assert.Equal(4, sink.WorkspaceCommands.Count);
    }

    [Fact]
    public async Task UnregisterAll_ClearsPersistentNotificationAndPopupState()
    {
        var sink = new RecordingOverlaySink();
        HotkeyManager.OverlaySink = sink;
        HotkeyManager.PopupAutoCloseDelay = TimeSpan.Zero;
        HotkeyManager.Register(ConsoleKey.F1, "persistent", NoopHandler);
        HotkeyManager.RegisterNotificationShortcuts(
            DateTimeOffset.Now.AddMinutes(1),
            [new UiShortcut(ConsoleKey.F2, NoopHandler)]);
        HotkeyManager.ShowPopup(new HotkeyContext().AddLine("popup"));

        HotkeyManager.UnregisterAll();

        Assert.Empty(HotkeyManager.Hotkeys);
        Assert.Null(sink.Popup);
        Assert.False(await PressAsync(KeyCode.F1));
        Assert.False(await PressAsync(KeyCode.F2));
    }

    static async Task RegisterFromAsyncCaller(object owner)
    {
        using var scope = HotkeyManager.RegisterScope(owner);
        await Task.Yield();
        await Task.Run(() => HotkeyManager.Register(ConsoleKey.F1, "owned", NoopHandler));
    }

    static Task<bool> PressAsync(KeyCode keyCode)
        => HotkeyManager.HandleKeyAsync(new(keyCode));

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "Timed out waiting for popup transition.");
            await Task.Delay(10);
        }
    }

    sealed class RecordingOverlaySink : IUiInputSink
    {
        readonly object gate = new();
        readonly List<Command> workspaceCommands = [];
        HotkeyPopup? popup;
        int showPopupCalls;
        int hidePopupCalls;

        public int PopupVisibleLineCount { get; init; } = 10;
        public Func<Command, bool>? HandleWorkspaceCommand { get; init; }
        public Action? ShowPopupCallout { get; init; }
        public Action? HidePopupCallout { get; init; }
        public IReadOnlyList<Command> WorkspaceCommands
        {
            get
            {
                lock (gate)
                    return workspaceCommands.ToArray();
            }
        }
        public HotkeyPopup? Popup
        {
            get
            {
                lock (gate)
                    return popup;
            }
        }
        public int PopupCalloutCount
        {
            get
            {
                lock (gate)
                    return showPopupCalls + hidePopupCalls;
            }
        }

        public Task<bool> TryHandleWorkspaceCommandAsync(Command command)
        {
            if (HandleWorkspaceCommand?.Invoke(command) != true)
                return Task.FromResult(false);
            lock (gate)
                workspaceCommands.Add(command);
            return Task.FromResult(true);
        }

        public void ShowPopup(HotkeyPopup value)
        {
            lock (gate)
                showPopupCalls++;
            ShowPopupCallout?.Invoke();
            lock (gate)
            {
                popup = value;
            }
        }

        public void HidePopup()
        {
            lock (gate)
                hidePopupCalls++;
            HidePopupCallout?.Invoke();
            lock (gate)
            {
                popup = null;
            }
        }
    }
}

[Collection("PluginReload")]
public sealed class HotkeyManagerErrorChannelTests(PluginRuntimeFixture runtime)
{
    [Fact]
    public async Task ThrowingHandler_ReportsVisibleErrorAndDoesNotBreakLaterDispatch()
    {
        const string scenario = "hotkey-error-channel";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            var host = runtime.Host;
            var bootstrap = host.Bootstrap;
            try
            {
                bootstrap.Workspace.SwitchTo();
                await host.FlushAsync();
                HotkeyManager.UnregisterAll();
                var laterCalls = 0;
                HotkeyManager.Register(ConsoleKey.F1, "throws", () =>
                    throw new InvalidOperationException("hotkey-handler-sentinel"));
                HotkeyManager.Register(ConsoleKey.F2, "later", () =>
                {
                    laterCalls++;
                    return Task.CompletedTask;
                });

                Assert.True(await HotkeyManager.HandleKeyAsync(new(KeyCode.F1)));
                await host.FlushAsync();
                await runtime.Terminal.WaitForScreenAsync(
                    string.Format(i18n.Hotkey_HandlerFailed, "hotkey-handler-sentinel"));
                Assert.True(await HotkeyManager.HandleKeyAsync(new(KeyCode.F2)));
                Assert.Equal(1, laterCalls);
            }
            finally
            {
                HotkeyManager.UnregisterAll();
                await host.FlushAsync();
            }

            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(HotkeyManagerErrorChannelTests),
                nameof(ThrowingHandler_ReportsVisibleErrorAndDoesNotBreakLaterDispatch)));
    }
}
