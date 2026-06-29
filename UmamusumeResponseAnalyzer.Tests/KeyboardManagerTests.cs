using UmamusumeResponseAnalyzer.LiveDisplay;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [CollectionDefinition("KeyboardManager")]
    public sealed class KeyboardManagerCollection
    {
    }

    /// <summary>
    /// 单元测试 <see cref="UmamusumeResponseAnalyzer.KeyboardManager"/> 的纯逻辑：
    /// 组合键格式化、保留键校验、注册/反注册字典增删、popup sink 路由。
    /// 不触碰 RunAsync / Console 轮询等需要真实终端的部分。
    ///
    /// KeyboardManager 是 static，被测方法直接读写全局 hotkeys 字典。
    /// xUnit 对同一测试类的方法串行执行、且每个测试方法新建一个实例；
    /// 会改动 hotkeys 字典的测试放在同一个 collection 中串行执行，
    /// 并在每个测试前后调用 UnregisterAll() 清空全局状态、避免互相污染。
    /// </summary>
    [Collection("KeyboardManager")]
    public class KeyboardManagerTests : IDisposable
    {
        // 构造函数在每个测试方法前运行：先洗干净全局字典，确保测试从空状态起步
        public KeyboardManagerTests() => ResetKeyboardManager();

        // 测试结束再洗一次，不给后续测试留残留
        public void Dispose() => ResetKeyboardManager();

        static Func<Task> NoopHandler => () => Task.CompletedTask;

        static void ResetKeyboardManager()
        {
            using (KeyboardManager.SuspendInput())
            {
            }

            KeyboardManager.UnregisterAll();
            KeyboardManager.OverlaySink = null;
            KeyboardManager.PopupAutoCloseDelay = TimeSpan.FromSeconds(3);
            LiveDisplayConsole.UnbindForTests();
        }

        // ── ① FormatKeyCombo ─────────────────────────────────────────────────

        [Fact]
        public void FormatKeyCombo_NoModifiers_ReturnsBareKeyName()
        {
            // 无修饰键时直接是 key.ToString()
            Assert.Equal("K", KeyboardManager.FormatKeyCombo(ConsoleKey.K, 0));
            Assert.Equal("F1", KeyboardManager.FormatKeyCombo(ConsoleKey.F1, 0));
        }

        [Fact]
        public void FormatKeyCombo_SingleModifier_PrependsPrefix()
        {
            Assert.Equal("Ctrl+K", KeyboardManager.FormatKeyCombo(ConsoleKey.K, ConsoleModifiers.Control));
            Assert.Equal("Alt+K", KeyboardManager.FormatKeyCombo(ConsoleKey.K, ConsoleModifiers.Alt));
            Assert.Equal("Shift+K", KeyboardManager.FormatKeyCombo(ConsoleKey.K, ConsoleModifiers.Shift));
        }

        [Fact]
        public void FormatKeyCombo_MultipleModifiers_OrderedCtrlAltShift()
        {
            // 拼接顺序固定为 Ctrl → Alt → Shift，与传入 flag 的顺序无关
            Assert.Equal("Ctrl+Alt+A",
                KeyboardManager.FormatKeyCombo(ConsoleKey.A, ConsoleModifiers.Control | ConsoleModifiers.Alt));
            Assert.Equal("Ctrl+Shift+A",
                KeyboardManager.FormatKeyCombo(ConsoleKey.A, ConsoleModifiers.Control | ConsoleModifiers.Shift));
            Assert.Equal("Alt+Shift+A",
                KeyboardManager.FormatKeyCombo(ConsoleKey.A, ConsoleModifiers.Alt | ConsoleModifiers.Shift));
            Assert.Equal("Ctrl+Alt+Shift+A",
                KeyboardManager.FormatKeyCombo(ConsoleKey.A,
                    ConsoleModifiers.Control | ConsoleModifiers.Alt | ConsoleModifiers.Shift));
            // flag 顺序反过来传，输出仍是固定的 Ctrl+Alt+Shift
            Assert.Equal("Ctrl+Alt+Shift+A",
                KeyboardManager.FormatKeyCombo(ConsoleKey.A,
                    ConsoleModifiers.Shift | ConsoleModifiers.Alt | ConsoleModifiers.Control));
        }

        [Theory]
        [InlineData(ConsoleKey.UpArrow, "↑")]
        [InlineData(ConsoleKey.DownArrow, "↓")]
        [InlineData(ConsoleKey.LeftArrow, "←")]
        [InlineData(ConsoleKey.RightArrow, "→")]
        public void FormatKeyCombo_ArrowKeys_MapToUnicodeGlyphs(ConsoleKey key, string glyph)
        {
            Assert.Equal(glyph, KeyboardManager.FormatKeyCombo(key, 0));
            // 带修饰键时前缀照拼，方向键仍替换为符号
            Assert.Equal("Ctrl+" + glyph, KeyboardManager.FormatKeyCombo(key, ConsoleModifiers.Control));
        }

        // ── ② 保留键校验 ─────────────────────────────────────────────────────

        [Theory]
        [InlineData(ConsoleKey.S)]
        [InlineData(ConsoleKey.Q)]
        [InlineData(ConsoleKey.Z)]
        public void Register_CtrlReservedKey_Throws(ConsoleKey key)
        {
            // Ctrl+S/Q/Z 被终端保留（XOFF/XON/Suspend），注册应抛 InvalidOperationException
            Assert.Throws<InvalidOperationException>(() =>
                KeyboardManager.Register(key, ConsoleModifiers.Control, "x", NoopHandler));
            // 抛异常后不应写入字典
            Assert.False(KeyboardManager.Hotkeys.ContainsKey((key, ConsoleModifiers.Control)));
        }

        [Fact]
        public void Register_CtrlReservedKey_WithExtraModifier_StillThrows()
        {
            // 校验用的是 HasFlag(Control)，叠加 Shift 仍命中保留键判定
            Assert.Throws<InvalidOperationException>(() =>
                KeyboardManager.Register(ConsoleKey.S, ConsoleModifiers.Control | ConsoleModifiers.Shift, "x", NoopHandler));
        }

        [Fact]
        public void Register_NonReservedCombos_Succeed()
        {
            // Ctrl+ 其它键、保留键但无 Ctrl、保留键配其它修饰键，都应正常注册
            KeyboardManager.Register(ConsoleKey.A, ConsoleModifiers.Control, "ctrl-a", NoopHandler);
            KeyboardManager.Register(ConsoleKey.S, 0, "bare-s", NoopHandler);          // 无 Ctrl 的 S
            KeyboardManager.Register(ConsoleKey.Q, ConsoleModifiers.Alt, "alt-q", NoopHandler); // Alt+Q 不受限

            Assert.True(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.A, ConsoleModifiers.Control)));
            Assert.True(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.S, 0)));
            Assert.True(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.Q, ConsoleModifiers.Alt)));
        }

        // ── ③ Register / Unregister / UnregisterAll 字典增删 ─────────────────

        [Fact]
        public void Register_NoModifierOverload_DefaultsToZeroModifiers()
        {
            KeyboardManager.Register(ConsoleKey.F5, "refresh", NoopHandler);
            // 无修饰键重载等价于 modifiers = 0
            Assert.True(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.F5, 0)));
            Assert.Equal("refresh", KeyboardManager.Hotkeys[(ConsoleKey.F5, 0)].Description);
        }

        [Fact]
        public void Register_F1WithoutModifier_Succeeds()
        {
            KeyboardManager.Register(ConsoleKey.F1, "默认帮助", NoopHandler);

            var entry = KeyboardManager.Hotkeys[(ConsoleKey.F1, 0)];
            Assert.Equal("默认帮助", entry.Description);
        }

        [Fact]
        public void Register_StoresEntryFields()
        {
            KeyboardManager.Register(ConsoleKey.F1, ConsoleModifiers.Control, "帮助", NoopHandler);
            var entry = KeyboardManager.Hotkeys[(ConsoleKey.F1, ConsoleModifiers.Control)];
            Assert.Equal("帮助", entry.Description);
            Assert.Same(NoopHandler, entry.Handler);
        }

        [Fact]
        public async Task Register_ContextHandler_RoutesPopupToOverlaySink()
        {
            var sink = new RecordingOverlaySink();
            KeyboardManager.OverlaySink = sink;
            KeyboardManager.Register(ConsoleKey.P, "popup", ctx =>
            {
                ctx.WriteLine("纯文本", ConsoleColor.Yellow)
                    .MarkupLine("[green]Markup[/]");
                return Task.CompletedTask;
            });

            await PressAsync(ConsoleKey.P);

            Assert.NotNull(sink.Popup);
            Assert.NotNull(sink.Popup.ExpiresAt);
            Assert.Equal(["纯文本", "[green]Markup[/]"], sink.Popup.Lines.Select(x => x.Text).ToArray());
            Assert.Equal([false, true], sink.Popup.Lines.Select(x => x.IsMarkup).ToArray());
        }

        [Fact]
        public void PopupAutoCloseDelay_DefaultsToThreeSeconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(3), KeyboardManager.PopupAutoCloseDelay);
        }

        [Fact]
        public async Task Register_ContextHandler_AutoClosesPopupAfterDelay()
        {
            var sink = new RecordingOverlaySink();
            KeyboardManager.OverlaySink = sink;
            KeyboardManager.PopupAutoCloseDelay = TimeSpan.FromMilliseconds(50);
            KeyboardManager.Register(ConsoleKey.P, "popup", ctx =>
            {
                ctx.WriteLine("自动关闭");
                return Task.CompletedTask;
            });

            await PressAsync(ConsoleKey.P);

            Assert.NotNull(sink.Popup);
            await sink.WaitForHiddenAsync();
        }

        [Fact]
        public async Task Register_ContextHandler_WithoutOverlaySink_DoesNotEscapeKeyboardLoop()
        {
            KeyboardManager.Register(ConsoleKey.P, "popup", ctx =>
            {
                ctx.WriteLine("不会静默丢失");
                return Task.CompletedTask;
            });

            await KeyboardManager.HandleKeyAsync(new ConsoleKeyInfo('p', ConsoleKey.P, shift: false, alt: false, control: false));
        }

        [Fact]
        public async Task HandleKey_PopupDoesNotConsumeModifierHotkey()
        {
            var sink = new RecordingOverlaySink();
            KeyboardManager.OverlaySink = sink;
            KeyboardManager.Register(ConsoleKey.P, "popup", ctx =>
            {
                ctx.WriteLine("popup");
                return Task.CompletedTask;
            });
            await PressAsync(ConsoleKey.P);
            var triggered = false;
            KeyboardManager.Register(
                ConsoleKey.Enter,
                ConsoleModifiers.Control,
                "ctrl-enter",
                () =>
                {
                    triggered = true;
                    return Task.CompletedTask;
                });

            await KeyboardManager.HandleKeyAsync(new ConsoleKeyInfo('\n', ConsoleKey.Enter, shift: false, alt: false, control: true));

            Assert.True(triggered);
            Assert.Null(sink.Popup);
        }

        [Fact]
        public async Task HandleKey_PopupNavigationScrollsAndCloses()
        {
            var sink = new RecordingOverlaySink();
            KeyboardManager.OverlaySink = sink;
            KeyboardManager.PopupAutoCloseDelay = TimeSpan.Zero;
            KeyboardManager.Register(ConsoleKey.P, "popup", ctx =>
            {
                for (var i = 0; i < 10; i++)
                    ctx.WriteLine($"line {i}");
                return Task.CompletedTask;
            });

            await PressAsync(ConsoleKey.P);
            Assert.Equal(0, sink.Popup?.ScrollOffset);

            await PressAsync(ConsoleKey.DownArrow);
            Assert.Equal(1, sink.Popup?.ScrollOffset);

            await PressAsync(ConsoleKey.PageDown);
            Assert.Equal(6, sink.Popup?.ScrollOffset);

            await PressAsync(ConsoleKey.End);
            Assert.Equal(9, sink.Popup?.ScrollOffset);

            await PressAsync(ConsoleKey.Home);
            Assert.Equal(0, sink.Popup?.ScrollOffset);

            await PressAsync(ConsoleKey.Escape);
            Assert.Null(sink.Popup);
        }

        [Fact]
        public async Task Register_ContextHandler_OldAutoCloseDoesNotCloseNewPopup()
        {
            var sink = new RecordingOverlaySink();
            KeyboardManager.OverlaySink = sink;
            KeyboardManager.PopupAutoCloseDelay = TimeSpan.FromMilliseconds(500);
            var text = "first";
            KeyboardManager.Register(ConsoleKey.P, "popup", ctx =>
            {
                ctx.WriteLine(text);
                return Task.CompletedTask;
            });

            await PressAsync(ConsoleKey.P);
            await Task.Delay(250, TestContext.Current.CancellationToken);
            text = "second";
            await PressAsync(ConsoleKey.P);
            await Task.Delay(350, TestContext.Current.CancellationToken);

            Assert.NotNull(sink.Popup);
            Assert.Equal("second", sink.Popup.Lines.Single().Text);

            await sink.WaitForHiddenAsync();
        }

        [Fact]
        public void Register_SameComboTwice_OverwritesEntry()
        {
            KeyboardManager.Register(ConsoleKey.K, ConsoleModifiers.Control, "first", NoopHandler);
            KeyboardManager.Register(ConsoleKey.K, ConsoleModifiers.Control, "second", NoopHandler);
            // 同一组合键重复注册覆盖先前入口，字典仍只有一条
            Assert.Single(KeyboardManager.Hotkeys);
            Assert.Equal("second", KeyboardManager.Hotkeys[(ConsoleKey.K, ConsoleModifiers.Control)].Description);
        }

        [Fact]
        public void Register_DifferentModifiers_AreSeparateKeys()
        {
            // 同一 ConsoleKey 配不同修饰键属于不同字典键，互不覆盖
            KeyboardManager.Register(ConsoleKey.K, 0, "bare", NoopHandler);
            KeyboardManager.Register(ConsoleKey.K, ConsoleModifiers.Control, "ctrl", NoopHandler);
            Assert.Equal(2, KeyboardManager.Hotkeys.Count);
        }

        [Fact]
        public void Unregister_ExistingCombo_RemovesAndReturnsTrue()
        {
            KeyboardManager.Register(ConsoleKey.K, ConsoleModifiers.Control, "x", NoopHandler);
            Assert.True(KeyboardManager.Unregister(ConsoleKey.K, ConsoleModifiers.Control));
            Assert.False(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.K, ConsoleModifiers.Control)));
        }

        [Fact]
        public void Unregister_MissingCombo_ReturnsFalse()
        {
            // 原本不存在的组合键，移除返回 false
            Assert.False(KeyboardManager.Unregister(ConsoleKey.K, ConsoleModifiers.Control));
        }

        [Fact]
        public void Unregister_DefaultsToZeroModifiers()
        {
            KeyboardManager.Register(ConsoleKey.F5, "x", NoopHandler); // 注册到 (F5, 0)
            // Unregister 不传 modifiers 默认 0，应能精确命中
            Assert.True(KeyboardManager.Unregister(ConsoleKey.F5));
            Assert.Empty(KeyboardManager.Hotkeys);
        }

        [Fact]
        public void Unregister_WrongModifiers_DoesNotRemove()
        {
            KeyboardManager.Register(ConsoleKey.K, ConsoleModifiers.Control, "x", NoopHandler);
            // 修饰键不匹配不应误删
            Assert.False(KeyboardManager.Unregister(ConsoleKey.K, ConsoleModifiers.Alt));
            Assert.True(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.K, ConsoleModifiers.Control)));
        }

        [Fact]
        public void UnregisterAll_ClearsEverything()
        {
            KeyboardManager.Register(ConsoleKey.A, "a", NoopHandler);
            KeyboardManager.Register(ConsoleKey.B, ConsoleModifiers.Control, "b", NoopHandler);
            KeyboardManager.UnregisterAll();
            Assert.Empty(KeyboardManager.Hotkeys);
        }

        static Task PressAsync(ConsoleKey key, ConsoleModifiers modifiers = 0)
        {
            return KeyboardManager.HandleKeyAsync(new ConsoleKeyInfo(
                '\0',
                key,
                modifiers.HasFlag(ConsoleModifiers.Shift),
                modifiers.HasFlag(ConsoleModifiers.Alt),
                modifiers.HasFlag(ConsoleModifiers.Control)));
        }

        sealed class RecordingOverlaySink : IKeyboardOverlaySink
        {
            readonly object sync = new();
            TaskCompletionSource hidden = new(TaskCreationOptions.RunContinuationsAsynchronously);
            KeyboardPopup? popup;

            public KeyboardPopup? Popup
            {
                get
                {
                    lock (sync)
                        return popup;
                }
            }

            public void ShowPopup(KeyboardPopup popup)
            {
                lock (sync)
                {
                    hidden = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    this.popup = popup;
                }
            }

            public void HidePopup()
            {
                TaskCompletionSource toComplete;
                lock (sync)
                {
                    popup = null;
                    toComplete = hidden;
                }

                toComplete.TrySetResult();
            }

            public Task WaitForHiddenAsync()
            {
                lock (sync)
                {
                    return popup is null ? Task.CompletedTask : hidden.Task.WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
        }

        // ── ⑤ Hotkeys 只读快照反映当前注册 ──────────────────────────────────

        [Fact]
        public void Hotkeys_ReflectsCurrentRegistrations()
        {
            Assert.Empty(KeyboardManager.Hotkeys);

            KeyboardManager.Register(ConsoleKey.A, "a", NoopHandler);
            Assert.Single(KeyboardManager.Hotkeys);
            Assert.True(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.A, 0)));

            KeyboardManager.Register(ConsoleKey.B, ConsoleModifiers.Control, "b", NoopHandler);
            Assert.Equal(2, KeyboardManager.Hotkeys.Count);

            KeyboardManager.Unregister(ConsoleKey.A);
            Assert.Single(KeyboardManager.Hotkeys);
            Assert.False(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.A, 0)));
            Assert.True(KeyboardManager.Hotkeys.ContainsKey((ConsoleKey.B, ConsoleModifiers.Control)));
        }

        [Fact]
        public void Hotkeys_KeyTupleMatchesRegisteredKeyAndModifiers()
        {
            KeyboardManager.Register(ConsoleKey.K, ConsoleModifiers.Control | ConsoleModifiers.Shift, "x", NoopHandler);
            var (key, mods) = Assert.Single(KeyboardManager.Hotkeys).Key;
            Assert.Equal(ConsoleKey.K, key);
            Assert.Equal(ConsoleModifiers.Control | ConsoleModifiers.Shift, mods);
        }
    }
}
