using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Spectre.Console;

namespace UmamusumeResponseAnalyzer
{
    /// <summary>
    /// 管理键盘快捷键注册与命令行输入。
    /// 已注册的快捷键（含组合键）直接触发对应动作；
    /// 未注册的可打印字符将激活底部命令行输入模式。
    /// </summary>
    public static class KeyboardManager
    {
        /// <param name="Instant">为 true 时绕过暂停倒计时，暂停期间也可触发。</param>
        public record HotkeyEntry(string Description, Func<Task> Handler, bool Instant = false);

        // 轮询间隔：Console.KeyAvailable 没有事件通知，只能轮询
        const int PollIntervalMs = 50;

        static readonly ConcurrentDictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), HotkeyEntry> _hotkeys = new();
        static volatile Func<string, Task>? _commandHandler;

        static readonly StringBuilder _buffer = new();
        static volatile bool _inCommandMode;
        static volatile bool _pauseActive;

        // 倒计时与主循环的取消源：只能通过 Interlocked 换位，避免 Cancel 到已 Dispose 的实例
        static CancellationTokenSource? _pauseCts;
        static CancellationTokenSource? _runCts;

        // 当前活跃的 popup 会话，聚合光标记录、命令行位置、popup 内容与屏幕快照
        static PopupSession? _session;

        /// <summary>触发快捷键后 Live 暂停的倒计时秒数，0 表示不暂停。</summary>
        public static int HotkeyPauseSeconds { get; set; } = 5;

        /// <summary>进入命令输入模式时触发，可用于暂停 Live 显示。</summary>
        public static Action? OnEnterCommandMode { get; set; }

        /// <summary>退出命令输入模式时触发，可用于恢复 Live 显示。</summary>
        public static Action? OnExitCommandMode { get; set; }

        /// <summary>快捷键 Handler 执行完毕后立即触发一次，用于强制刷新 Live 显示。</summary>
        public static Func<Task>? OnRefreshRequested { get; set; }

        /// <summary>注册带修饰键的快捷键（如 Ctrl+K）。重复注册同一组合会覆盖先前的入口。</summary>
        public static void Register(ConsoleKey key, ConsoleModifiers modifiers, string description, Func<Task> handler, bool instant = false)
        {
            if (modifiers.HasFlag(ConsoleModifiers.Control) && key is ConsoleKey.S or ConsoleKey.Q or ConsoleKey.Z)
                throw new InvalidOperationException($"快捷键 {FormatKeyCombo(key, modifiers)} 被操作系统终端保留（XOFF/XON/Suspend），无法注册。请使用其他组合键。");

            _hotkeys[(key, modifiers)] = new HotkeyEntry(description, handler, instant);
        }

        /// <summary>注册无修饰键的快捷键。</summary>
        public static void Register(ConsoleKey key, string description, Func<Task> handler, bool instant = false)
            => Register(key, 0, description, handler, instant);

        /// <summary>
        /// 注册带修饰键的快捷键，Handler 接收 <see cref="KeyboardHandlerContext"/>。
        /// 向 ctx 写入的内容会在 Live 最后一次渲染后以浮动 popup 的形式显示，
        /// 倒计时结束或用户按空格/回车后自动消失。
        /// </summary>
        public static void Register(ConsoleKey key, ConsoleModifiers modifiers, string description,
            Func<KeyboardHandlerContext, Task> handler, bool instant = false)
        {
            _hotkeys[(key, modifiers)] = new HotkeyEntry(description, async () =>
            {
                var ctx = new KeyboardHandlerContext();
                _session?.SetContext(ctx);
                await handler(ctx);
            }, instant);
        }

        /// <summary>注册无修饰键的快捷键，Handler 接收 <see cref="KeyboardHandlerContext"/>。</summary>
        public static void Register(ConsoleKey key, string description,
            Func<KeyboardHandlerContext, Task> handler, bool instant = false)
            => Register(key, 0, description, handler, instant);

        /// <summary>取消注册指定组合键。成功移除返回 true，原本不存在返回 false。</summary>
        public static bool Unregister(ConsoleKey key, ConsoleModifiers modifiers = 0)
            => _hotkeys.TryRemove((key, modifiers), out _);

        /// <summary>清空所有已注册的快捷键。</summary>
        public static void UnregisterAll() => _hotkeys.Clear();

        /// <summary>设置命令行输入的处理器。</summary>
        public static void SetCommandHandler(Func<string, Task> handler)
            => _commandHandler = handler;

        /// <summary>所有已注册的快捷键（只读）。</summary>
        public static IReadOnlyDictionary<(ConsoleKey, ConsoleModifiers), HotkeyEntry> Hotkeys => _hotkeys;

        /// <summary>
        /// 请求终止 <see cref="RunAsync"/> 循环，使主程序得以退出。
        /// 可在已注册的快捷键 Handler 中调用。
        /// </summary>
        public static void Stop() => Volatile.Read(ref _runCts)?.Cancel();

        /// <summary>格式化按键组合为可读字符串，如 "Ctrl+K"。</summary>
        public static string FormatKeyCombo(ConsoleKey key, ConsoleModifiers mods)
        {
            var sb = new StringBuilder(16);
            if (mods.HasFlag(ConsoleModifiers.Control)) sb.Append("Ctrl+");
            if (mods.HasFlag(ConsoleModifiers.Alt))     sb.Append("Alt+");
            if (mods.HasFlag(ConsoleModifiers.Shift))   sb.Append("Shift+");
            sb.Append(key switch
            {
                ConsoleKey.UpArrow    => "↑",
                ConsoleKey.DownArrow  => "↓",
                ConsoleKey.LeftArrow  => "←",
                ConsoleKey.RightArrow => "→",
                _                     => key.ToString()
            });
            return sb.ToString();
        }

        /// <summary>
        /// 启动键盘监听循环，直到外部 <paramref name="cancellationToken"/> 被取消
        /// 或调用 <see cref="Stop()"/> 为止。应在主线程（或顶层 await）中调用。
        /// </summary>
        public static async Task RunAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (Interlocked.CompareExchange(ref _runCts, linked, null) != null)
                throw new InvalidOperationException("KeyboardManager.RunAsync 已在运行中");

            // 让 Ctrl+C 作为普通按键被 ReadKey 捕获，由注册的快捷键处理，而非触发 CancelKeyPress
            var prevTreatCtrlC = false;
            try { prevTreatCtrlC = Console.TreatControlCAsInput; Console.TreatControlCAsInput = true; }
            catch (IOException) { /* 无终端（重定向），后续 ReadKey 也会抛，这里先忽略 */ }

            try
            {
                while (!linked.Token.IsCancellationRequested)
                {
                    if (!TryKeyAvailable())
                    {
                        try { await Task.Delay(PollIntervalMs, linked.Token); }
                        catch (OperationCanceledException) { break; }
                        continue;
                    }

                    ConsoleKeyInfo keyInfo;
                    try { keyInfo = Console.ReadKey(intercept: true); }
                    catch (InvalidOperationException) { break; } // 终端被关闭

                    if (_inCommandMode)
                        await HandleCommandModeKeyAsync(keyInfo);
                    else
                        await HandleNormalKeyAsync(keyInfo);
                }
            }
            finally
            {
                try { Console.TreatControlCAsInput = prevTreatCtrlC; } catch { }

                // 取消残留的倒计时，释放快照并还原屏幕
                var pause = Interlocked.Exchange(ref _pauseCts, null);
                pause?.Cancel();
                pause?.Dispose();
                _pauseActive = false;

                _session?.End();
                _session = null;

                Interlocked.CompareExchange(ref _runCts, null, linked);
            }
        }

        static bool TryKeyAvailable()
        {
            try { return Console.KeyAvailable; }
            catch (InvalidOperationException) { return false; } // stdin 被重定向
        }

        // ── 主循环分支 ────────────────────────────────────────────────────────

        static async Task HandleCommandModeKeyAsync(ConsoleKeyInfo keyInfo)
        {
            switch (keyInfo.Key)
            {
                case ConsoleKey.Enter:
                {
                    _inCommandMode = false;
                    var command = _buffer.ToString();
                    _buffer.Clear();

                    if (_commandHandler != null && !string.IsNullOrWhiteSpace(command))
                    {
                        // handler 要向屏幕输出，但 snapshot 必须保留到倒计时结束
                        _session?.ClearCommandRow();
                        await InvokeSafely(() => _commandHandler(command));

                        if (HotkeyPauseSeconds > 0)
                        {
                            // handler 输出后光标位置作为 popup 结束的"回到点"
                            _session?.RecordCursorNow();
                            StartPauseCountdown();
                        }
                        else
                        {
                            EndSession();
                        }
                    }
                    else
                    {
                        EndSession();
                    }
                    break;
                }

                case ConsoleKey.Escape:
                    _inCommandMode = false;
                    _buffer.Clear();
                    EndSession();
                    break;

                case ConsoleKey.Backspace:
                    if (_buffer.Length > 0)
                    {
                        _buffer.Remove(_buffer.Length - 1, 1);
                        DrawCommandLine();
                    }
                    else
                    {
                        _inCommandMode = false;
                        EndSession();
                    }
                    break;

                default:
                    if (!char.IsControl(keyInfo.KeyChar))
                    {
                        _buffer.Append(keyInfo.KeyChar);
                        DrawCommandLine();
                    }
                    break;
            }
        }

        static async Task HandleNormalKeyAsync(ConsoleKeyInfo keyInfo)
        {
            var combo = (keyInfo.Key, keyInfo.Modifiers);

            if (_pauseActive)
            {
                if (keyInfo.Key is ConsoleKey.Spacebar or ConsoleKey.Enter)
                {
                    Volatile.Read(ref _pauseCts)?.Cancel();
                    return;
                }
                if (_hotkeys.TryGetValue(combo, out var instantEntry) && instantEntry.Instant)
                {
                    await InvokeSafely(instantEntry.Handler);
                    await InvokeRefreshAsync();
                }
                return;
            }

            if (_hotkeys.TryGetValue(combo, out var entry))
            {
                if (entry.Instant)
                {
                    await InvokeSafely(entry.Handler);
                    // instant 不显示 popup：丢弃 wrapper 可能写入的 context
                    _session?.DiscardContext();
                    await InvokeRefreshAsync();
                    return;
                }

                BeginSession();
                OnEnterCommandMode?.Invoke();
                await InvokeSafely(entry.Handler);
                await InvokeRefreshAsync();

                // Render 会移动光标，必须在渲染前记录回到点
                _session?.RecordCursorNow();
                var popupRow = Console.WindowTop + Console.WindowHeight - 1;
                _session?.RenderPopup(popupRow);

                if (HotkeyPauseSeconds > 0)
                    StartPauseCountdown();
                else
                    EndSession();
            }
            else if (!char.IsControl(keyInfo.KeyChar) && keyInfo.Modifiers == 0)
            {
                _inCommandMode = true;
                BeginSession();
                OnEnterCommandMode?.Invoke();
                DrawCommandLine();                  // 先画空提示符"> _"占位
                _buffer.Append(keyInfo.KeyChar);
                DrawCommandLine();                  // 再画首字符"> w_"
            }
        }

        // ── 会话生命周期 ─────────────────────────────────────────────────────

        static void BeginSession()
        {
            _session ??= PopupSession.Begin();
        }

        static void EndSession()
        {
            _session?.End();
            _session = null;
            OnExitCommandMode?.Invoke();
        }

        // ── 倒计时 ───────────────────────────────────────────────────────────

        static void StartPauseCountdown() => _ = StartPauseCountdownAsync();

        static async Task StartPauseCountdownAsync()
        {
            var cts = new CancellationTokenSource();
            var prev = Interlocked.Exchange(ref _pauseCts, cts);
            prev?.Cancel();
            prev?.Dispose();

            _pauseActive = true;
            try
            {
                for (var remaining = HotkeyPauseSeconds; remaining > 0; remaining--)
                {
                    DrawPauseLine(remaining);
                    await Task.Delay(1000, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // 吃掉任何未预期异常，避免 fire-and-forget 引发 UnobservedTaskException
            }
            finally
            {
                // 仅当本次实例仍是当前倒计时时才清场，防止被新倒计时覆盖后误重置状态
                if (Interlocked.CompareExchange(ref _pauseCts, null, cts) == cts)
                {
                    _pauseActive = false;
                    if (!_inCommandMode)
                        EndSession();
                }
                cts.Dispose();
            }
        }

        // ── 绘制 ─────────────────────────────────────────────────────────────

        static void DrawPauseLine(int remaining)
        {
            try
            {
                var commandRow = Console.WindowTop + Console.WindowHeight - 1;
                _session?.SetCommandRow(commandRow);

                var savedLeft = Console.CursorLeft;
                var savedTop = Console.CursorTop;
                var prevFg = Console.ForegroundColor;

                Console.CursorVisible = false;
                Console.SetCursorPosition(0, commandRow);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("按 空格/回车 继续 (");
                Console.ForegroundColor = remaining <= 2 ? ConsoleColor.Yellow : ConsoleColor.DarkYellow;
                Console.Write($"{remaining}s");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(")");
                Console.ForegroundColor = prevFg;

                var leftEnd = Console.CursorLeft;

                var hints = BuildInstantHints();
                var hintWidth = EstimateDisplayWidth(hints);
                var hintStart = Console.WindowWidth - 1 - hintWidth;
                if (hints.Length > 0 && hintStart > leftEnd + 2)
                {
                    Console.Write(new string(' ', hintStart - leftEnd));
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(hints);
                    Console.ForegroundColor = prevFg;
                }
                else
                {
                    var rest = Console.WindowWidth - 1 - leftEnd;
                    if (rest > 0) Console.Write(new string(' ', rest));
                }

                if (savedTop != commandRow)
                    Console.SetCursorPosition(savedLeft, savedTop);
                else
                    Console.SetCursorPosition(Math.Min(leftEnd, Console.WindowWidth - 1), commandRow);
                Console.CursorVisible = true;
            }
            catch (IOException) { }
            catch (ArgumentOutOfRangeException) { }
        }

        static void DrawCommandLine()
        {
            try
            {
                var commandRow = Console.WindowTop + Console.WindowHeight - 1;
                _session?.SetCommandRow(commandRow);

                var prevFg = Console.ForegroundColor;

                Console.CursorVisible = false;
                Console.SetCursorPosition(0, commandRow);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("> ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(_buffer.ToString());
                Console.ForegroundColor = prevFg;

                var inputEnd = Math.Min(Console.CursorLeft, Console.WindowWidth - 1);

                const string hint = "ESC 取消";
                var hintStart = Console.WindowWidth - 1 - EstimateDisplayWidth(hint);
                if (hintStart > inputEnd + 1)
                {
                    Console.Write(new string(' ', hintStart - inputEnd));
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(hint);
                    Console.ForegroundColor = prevFg;
                }
                else
                {
                    var rest = Console.WindowWidth - 1 - inputEnd;
                    if (rest > 0) Console.Write(new string(' ', rest));
                }

                Console.SetCursorPosition(inputEnd, commandRow);
                Console.CursorVisible = true;
            }
            catch (IOException) { }
            catch (ArgumentOutOfRangeException) { }
        }

        /// <summary>构建 Instant 快捷键的单行提示，格式：[Ctrl+C] 退出程序  [F1] 帮助</summary>
        static string BuildInstantHints()
        {
            var sb = new StringBuilder();
            foreach (var kv in _hotkeys)
            {
                if (!kv.Value.Instant) continue;
                if (sb.Length > 0) sb.Append("  ");
                sb.Append('[').Append(FormatKeyCombo(kv.Key.Key, kv.Key.Modifiers)).Append("] ").Append(kv.Value.Description);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 计算字符串在终端中的显示列宽
        /// </summary>
        internal static int EstimateDisplayWidth(string s) => s.GetCellWidth();

        // ── 辅助 ─────────────────────────────────────────────────────────────

        static async Task InvokeSafely(Func<Task> fn)
        {
            try { await fn(); }
            catch (Exception ex)
            {
                // handler 抛错不应拖垮主循环；写一行提示并让用户看到
                try
                {
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[KeyboardManager] handler 异常: {ex.GetType().Name}: {ex.Message}");
                    Console.ForegroundColor = prev;
                }
                catch { }
            }
        }

        static async Task InvokeRefreshAsync()
        {
            var fn = OnRefreshRequested;
            if (fn == null) return;
            try { await fn(); } catch { }
        }
    }

    /// <summary>
    /// 快捷键 Handler 的输出上下文。向此对象写入的内容在 Live 最后一帧之后以浮动
    /// popup 的形式渲染在屏幕底部，倒计时结束或用户手动继续时自动消失。
    /// </summary>
    public sealed class KeyboardHandlerContext
    {
        readonly record struct Line(string Text, ConsoleColor Color, bool IsMarkup);
        readonly List<Line> _lines = [];

        /// <summary>向 popup 追加一行文字。</summary>
        public KeyboardHandlerContext WriteLine(string text = "", ConsoleColor color = ConsoleColor.White)
        {
            _lines.Add(new Line(text, color, IsMarkup: false));
            return this;
        }

        /// <summary>
        /// 向 popup 追加一行 Spectre.Console 标记文本（如 "[red]错误[/] 详情"）。
        /// 内部需要转义的字面 [ / ] 由调用方通过 <c>EscapeMarkup()</c> 自行处理。
        /// </summary>
        public KeyboardHandlerContext MarkupLine(string markup = "")
        {
            _lines.Add(new Line(markup, ConsoleColor.White, IsMarkup: true));
            return this;
        }

        /// <summary>
        /// 在 commandRow 正上方渲染 popup 方框。
        /// 调用前 Live 应已暂停，调用后不移动光标。
        /// </summary>
        internal void Render(int commandRow)
        {
            if (_lines.Count == 0) return;
            try
            {
                var w = Console.WindowWidth - 1;        // 可用宽度（留最后一列防换行）
                var contentW = w - 2;                   // │ 和 │ 之间的宽度
                var prevFg = Console.ForegroundColor;
                var savedLeft = Console.CursorLeft;
                var savedTop = Console.CursorTop;
                Console.CursorVisible = false;

                var topRow    = commandRow - _lines.Count - 2; // ┌─┐
                var bottomRow = commandRow - 1;                // └─┘

                if (topRow >= 0)
                {
                    Console.SetCursorPosition(0, topRow);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('┌');
                    Console.Write(new string('─', w - 2));
                    Console.Write('┐');
                }

                for (var i = 0; i < _lines.Count; i++)
                {
                    var row = topRow + 1 + i;
                    if (row < 0 || row >= Console.BufferHeight) continue;
                    Console.SetCursorPosition(0, row);
                    var line = _lines[i];

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('│');
                    Console.Write(' ');

                    int textDisplayW;
                    if (line.IsMarkup)
                    {
                        AnsiConsole.Markup(line.Text);
                        textDisplayW = 1 + KeyboardManager.EstimateDisplayWidth(Markup.Remove(line.Text));
                    }
                    else
                    {
                        Console.ForegroundColor = line.Color;
                        Console.Write(line.Text);
                        textDisplayW = 1 + KeyboardManager.EstimateDisplayWidth(line.Text);
                    }

                    var pad = contentW - textDisplayW;
                    if (pad > 0) Console.Write(new string(' ', pad));

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('│');
                }

                if (bottomRow >= 0 && bottomRow < Console.BufferHeight)
                {
                    Console.SetCursorPosition(0, bottomRow);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('└');
                    Console.Write(new string('─', w - 2));
                    Console.Write('┘');
                }

                Console.ForegroundColor = prevFg;
                Console.SetCursorPosition(savedLeft, savedTop);
                Console.CursorVisible = true;
            }
            catch (IOException) { }
            catch (ArgumentOutOfRangeException) { }
        }

        /// <summary>擦除 popup 占用的所有行（用空格覆盖）。不管理光标可见性，由调用方负责。</summary>
        internal void Erase(int commandRow)
        {
            if (_lines.Count == 0) return;
            try
            {
                var w = Console.WindowWidth - 1;
                var topRow    = commandRow - _lines.Count - 2;
                var bottomRow = commandRow - 1;
                for (var row = topRow; row <= bottomRow; row++)
                {
                    if (row < 0 || row >= Console.BufferHeight) continue;
                    Console.SetCursorPosition(0, row);
                    Console.Write(new string(' ', w));
                }
            }
            catch (IOException) { }
            catch (ArgumentOutOfRangeException) { }
        }
    }

    /// <summary>
    /// Popup 会话状态。聚合光标记录、命令行位置、popup 内容与屏幕快照，
    /// 使 Begin/End 一一对应，避免零散字段互相错位。
    /// </summary>
    sealed class PopupSession
    {
        int _preCursorLeft;
        int _preCursorTop;
        int _commandRow = -1;
        KeyboardHandlerContext? _context;
        ConsoleSnapshot? _snapshot;

        public static PopupSession Begin()
        {
            var left = 0;
            var top = 0;
            try { left = Console.CursorLeft; top = Console.CursorTop; } catch { }
            return new PopupSession
            {
                _preCursorLeft = left,
                _preCursorTop = top,
                _snapshot = ConsoleSnapshot.CaptureViewport()
            };
        }

        public void SetContext(KeyboardHandlerContext ctx) => _context = ctx;
        public void DiscardContext() => _context = null;
        public void SetCommandRow(int row) => _commandRow = row;

        /// <summary>handler 执行后光标位置作为 popup 结束时的"回到点"（退化路径用）。</summary>
        public void RecordCursorNow()
        {
            try { _preCursorLeft = Console.CursorLeft; _preCursorTop = Console.CursorTop; }
            catch { }
        }

        public void RenderPopup(int commandRow)
        {
            _commandRow = commandRow;
            _context?.Render(commandRow);
        }

        /// <summary>清空当前命令行，但保留 snapshot。命令模式 Enter 后 handler 要输出时使用。</summary>
        public void ClearCommandRow()
        {
            if (_commandRow < 0 || _commandRow >= Console.BufferHeight) return;
            try
            {
                Console.CursorVisible = false;
                Console.SetCursorPosition(0, _commandRow);
                Console.Write(new string(' ', Console.WindowWidth - 1));
                Console.SetCursorPosition(_preCursorLeft, _preCursorTop);
                Console.CursorVisible = true;
            }
            catch (IOException) { }
            catch (ArgumentOutOfRangeException) { }
            _commandRow = -1;
        }

        /// <summary>结束会话。有快照时整块写回（真彩色仅近似），否则退化为"擦行 + 回光标"。</summary>
        public void End()
        {
            try { Console.CursorVisible = false; } catch { }

            if (_snapshot != null)
            {
                _snapshot.Restore();
                _snapshot = null;
            }
            else
            {
                // 退化路径：擦 popup 区域 + 命令行
                if (_commandRow >= 0 && _commandRow < Console.BufferHeight)
                {
                    try
                    {
                        _context?.Erase(_commandRow);
                        Console.SetCursorPosition(0, _commandRow);
                        Console.Write(new string(' ', Console.WindowWidth - 1));
                    }
                    catch (IOException) { }
                    catch (ArgumentOutOfRangeException) { }
                }
                try { Console.SetCursorPosition(_preCursorLeft, _preCursorTop); } catch { }
            }

            _context = null;
            _commandRow = -1;
            try { Console.CursorVisible = true; } catch { }
        }
    }

    /// <summary>
    /// 使用 Win32 ReadConsoleOutput / WriteConsoleOutput 捕获并还原终端可视区域。
    /// 用于 KeyboardManager 的 popup 效果：写入前抓屏，popup 消失后原样写回。
    /// 仅在 Windows 控制台有效，其他平台或 API 调用失败时 CaptureViewport 返回 null。
    /// 注意：CHAR_INFO 只容纳 16 色属性，真彩色渲染会被量化到最近的调色板色。
    /// </summary>
    internal sealed class ConsoleSnapshot
    {
        [StructLayout(LayoutKind.Sequential)]
        struct COORD { public short X; public short Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct SMALL_RECT { public short Left; public short Top; public short Right; public short Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct CHAR_INFO
        {
            public ushort UnicodeChar;
            public ushort Attributes;
        }

        // 使用旧式 DllImport：LibraryImport 源生成器对嵌套 struct 数组支持不全，
        // 会把参数视为未绑定。SYSLIB1054 hint 仅提示，不影响发布。
#pragma warning disable SYSLIB1054
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", EntryPoint = "ReadConsoleOutputW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ReadConsoleOutput(IntPtr hConsole, [Out] CHAR_INFO[] buffer, COORD bufSize, COORD bufCoord, ref SMALL_RECT region);

        [DllImport("kernel32.dll", EntryPoint = "WriteConsoleOutputW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool WriteConsoleOutput(IntPtr hConsole, [In] CHAR_INFO[] buffer, COORD bufSize, COORD bufCoord, ref SMALL_RECT region);
#pragma warning restore SYSLIB1054

        const int STD_OUTPUT_HANDLE = -11;
        static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        CHAR_INFO[] _buffer = null!;
        short _left;
        short _top;
        short _width;
        short _height;
        int _cursorLeft;
        int _cursorTop;

        ConsoleSnapshot() { }

        public static ConsoleSnapshot? CaptureViewport()
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE) return null;

                var top = (short)Math.Max(0, Console.WindowTop);
                var width = (short)Math.Min(Console.BufferWidth, short.MaxValue);
                var height = (short)Math.Min(Console.WindowHeight, short.MaxValue);
                if (width <= 0 || height <= 0) return null;

                var buffer = new CHAR_INFO[width * height];
                var size = new COORD { X = width, Y = height };
                var coord = new COORD { X = 0, Y = 0 };
                var region = new SMALL_RECT
                {
                    Left = 0,
                    Top = top,
                    Right = (short)(width - 1),
                    Bottom = (short)(top + height - 1)
                };
                if (!ReadConsoleOutput(handle, buffer, size, coord, ref region))
                    return null;

                return new ConsoleSnapshot
                {
                    _buffer = buffer,
                    _left = 0,
                    _top = top,
                    _width = width,
                    _height = height,
                    _cursorLeft = Console.CursorLeft,
                    _cursorTop = Console.CursorTop
                };
            }
            catch { return null; }
        }

        public void Restore()
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE) return;

                var size = new COORD { X = _width, Y = _height };
                var coord = new COORD { X = 0, Y = 0 };
                var region = new SMALL_RECT
                {
                    Left = _left,
                    Top = _top,
                    Right = (short)(_left + _width - 1),
                    Bottom = (short)(_top + _height - 1)
                };
                WriteConsoleOutput(handle, _buffer, size, coord, ref region);

                try
                {
                    var cl = Math.Clamp(_cursorLeft, 0, Console.BufferWidth - 1);
                    var ct = Math.Clamp(_cursorTop, 0, Console.BufferHeight - 1);
                    Console.SetCursorPosition(cl, ct);
                }
                catch { }
            }
            catch { }
        }
    }
}
