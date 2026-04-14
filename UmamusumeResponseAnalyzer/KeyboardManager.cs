using System.Text;
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
// 100% AI生成代码，阅读前请做好心理准备
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

        static readonly Dictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), HotkeyEntry> _hotkeys = [];
        static Func<string, Task>? _commandHandler;

        static readonly StringBuilder _buffer = new();
        static bool _inCommandMode;
        static int _commandRow = -1;

        static volatile bool _pauseActive;
        static CancellationTokenSource? _pauseCts;

        // 用于 Stop() 取消主循环
        static CancellationTokenSource? _runCts;

        // 进入底部行占用前保存的光标位置，ClearCommandLine 退出时还原
        static int _preCmdCursorLeft;
        static int _preCmdCursorTop;

        // 当前活跃的 popup context（由 context 版 Register 的 wrapper 写入）
        static KeyboardHandlerContext? _activeContext;

        /// <summary>触发快捷键后 Live 暂停的倒计时秒数，0 表示不暂停。</summary>
        public static int HotkeyPauseSeconds { get; set; } = 5;

        /// <summary>进入命令输入模式时触发，可用于暂停 Live 显示。</summary>
        public static Action? OnEnterCommandMode { get; set; }
        /// <summary>退出命令输入模式时触发，可用于恢复 Live 显示。</summary>
        public static Action? OnExitCommandMode { get; set; }
        /// <summary>快捷键 Handler 执行完毕后立即触发一次，用于强制刷新 Live 显示。</summary>
        public static Func<Task>? OnRefreshRequested { get; set; }

        /// <summary>注册带修饰键的快捷键（如 Ctrl+K）。</summary>
        public static void Register(ConsoleKey key, ConsoleModifiers modifiers, string description, Func<Task> handler, bool instant = false)
            => _hotkeys[(key, modifiers)] = new HotkeyEntry(description, handler, instant);

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
                _activeContext = ctx;
                await handler(ctx);
            }, instant);
        }

        /// <summary>注册无修饰键的快捷键，Handler 接收 <see cref="KeyboardHandlerContext"/>。</summary>
        public static void Register(ConsoleKey key, string description,
            Func<KeyboardHandlerContext, Task> handler, bool instant = false)
            => Register(key, 0, description, handler, instant);

        /// <summary>设置命令行输入的处理器。</summary>
        public static void SetCommandHandler(Func<string, Task> handler)
            => _commandHandler = handler;

        /// <summary>所有已注册的快捷键（只读）。</summary>
        public static IReadOnlyDictionary<(ConsoleKey, ConsoleModifiers), HotkeyEntry> Hotkeys => _hotkeys;

        /// <summary>
        /// 请求终止 <see cref="RunAsync"/> 循环，使主程序得以退出。
        /// 可在已注册的快捷键 Handler 中调用。
        /// </summary>
        public static void Stop() => _runCts?.Cancel();

        /// <summary>格式化按键组合为可读字符串，如 "Ctrl+K"。</summary>
        public static string FormatKeyCombo(ConsoleKey key, ConsoleModifiers mods)
        {
            var parts = new List<string>();
            if (mods.HasFlag(ConsoleModifiers.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ConsoleModifiers.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ConsoleModifiers.Shift)) parts.Add("Shift");
            parts.Add(key switch
            {
                ConsoleKey.UpArrow    => "↑",
                ConsoleKey.DownArrow  => "↓",
                ConsoleKey.LeftArrow  => "←",
                ConsoleKey.RightArrow => "→",
                _                     => key.ToString()
            });
            return string.Join("+", parts);
        }

        /// <summary>
        /// 启动键盘监听循环，直到外部 <paramref name="cancellationToken"/> 被取消
        /// 或调用 <see cref="Stop()"/> 为止。应在主线程（或顶层 await）中调用。
        /// </summary>
        public static async Task RunAsync(CancellationToken cancellationToken)
        {
            // 将外部 token 与内部 Stop() token 合并，任意一个取消都能退出循环
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCts = linked;

            // 让 Ctrl+C 作为普通按键被 ReadKey 捕获，由注册的快捷键处理，而非触发 CancelKeyPress
            Console.TreatControlCAsInput = true;

            try
            {
                while (!linked.Token.IsCancellationRequested)
                {
                    if (!Console.KeyAvailable)
                    {
                        try { await Task.Delay(50, linked.Token); } catch { break; }
                        continue;
                    }

                    var keyInfo = Console.ReadKey(intercept: true);

                    if (_inCommandMode)
                    {
                        switch (keyInfo.Key)
                        {
                            case ConsoleKey.Enter:
                            {
                                ClearCommandLine();
                                _inCommandMode = false;
                                var command = _buffer.ToString();
                                _buffer.Clear();
                                if (_commandHandler != null && !string.IsNullOrWhiteSpace(command))
                                {
                                    await _commandHandler(command);
                                    if (HotkeyPauseSeconds > 0)
                                    {
                                        // handler 可能输出内容，在此之后保存光标位置
                                        (_preCmdCursorLeft, _preCmdCursorTop) = (Console.CursorLeft, Console.CursorTop);
                                        _ = StartPauseCountdownAsync();
                                    }
                                    else
                                        OnExitCommandMode?.Invoke();
                                }
                                else
                                {
                                    OnExitCommandMode?.Invoke();
                                }
                                break;
                            }
                            case ConsoleKey.Escape:
                                ClearCommandLine();
                                _buffer.Clear();
                                _inCommandMode = false;
                                OnExitCommandMode?.Invoke();
                                break;

                            case ConsoleKey.Backspace:
                                if (_buffer.Length > 0)
                                {
                                    _buffer.Remove(_buffer.Length - 1, 1);
                                    DrawCommandLine();
                                }
                                else
                                {
                                    ClearCommandLine();
                                    _inCommandMode = false;
                                    OnExitCommandMode?.Invoke();
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
                    else
                    {
                        if (_pauseActive)
                        {
                            if (keyInfo.Key is ConsoleKey.Spacebar or ConsoleKey.Enter)
                                _pauseCts?.Cancel();
                            else if (_hotkeys.TryGetValue((keyInfo.Key, keyInfo.Modifiers), out var instantEntry) && instantEntry.Instant)
                            {
                                await instantEntry.Handler();
                                if (OnRefreshRequested != null)
                                    await OnRefreshRequested();
                            }
                        }
                        else if (_hotkeys.TryGetValue((keyInfo.Key, keyInfo.Modifiers), out var entry))
                        {
                            if (entry.Instant)
                            {
                                _activeContext = null;
                                await entry.Handler();
                                _activeContext = null; // instant 不显示 popup
                                if (OnRefreshRequested != null)
                                    await OnRefreshRequested();
                            }
                            else
                            {
                                _activeContext = null;
                                OnEnterCommandMode?.Invoke();
                                await entry.Handler();
                                if (OnRefreshRequested != null)
                                    await OnRefreshRequested();
                                // 必须先保存光标位置再渲染 popup：
                                // Render 会移动光标，若保存在 Render 之后则记录错误位置
                                (_preCmdCursorLeft, _preCmdCursorTop) = (Console.CursorLeft, Console.CursorTop);
                                var popupRow = Console.WindowTop + Console.WindowHeight - 1;
                                _activeContext?.Render(popupRow);
                                if (HotkeyPauseSeconds > 0)
                                    _ = StartPauseCountdownAsync();
                                else
                                {
                                    _activeContext?.Erase(popupRow);
                                    _activeContext = null;
                                    OnExitCommandMode?.Invoke();
                                }
                            }
                        }
                        else if (!char.IsControl(keyInfo.KeyChar) && keyInfo.Modifiers == 0)
                        {
                            _inCommandMode = true;
                            // 在 OnEnterCommandMode 移动光标之前保存位置
                            (_preCmdCursorLeft, _preCmdCursorTop) = (Console.CursorLeft, Console.CursorTop);
                            OnEnterCommandMode?.Invoke();
                            DrawCommandLine();          // 先画空提示符 "> _"，让 Live 暂停后立刻有内容占位
                            _buffer.Append(keyInfo.KeyChar);
                            DrawCommandLine();          // 再画首字符 "> w_"
                        }
                    }
                }
            }
            finally
            {
                // 还原 Ctrl+C 行为，防止后续代码（如 Console.ReadLine）出现异常
                Console.TreatControlCAsInput = false;
                _runCts = null;
            }
        }

        static async Task StartPauseCountdownAsync()
        {
            // 取消并释放已有的倒计时，避免快速触发时并发冲突
            var prev = _pauseCts;
            prev?.Cancel();
            prev?.Dispose();

            _pauseActive = true;
            var cts = new CancellationTokenSource();
            _pauseCts = cts;
            try
            {
                for (var remaining = HotkeyPauseSeconds; remaining > 0; remaining--)
                {
                    DrawPauseLine(remaining);
                    await Task.Delay(1000, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                cts.Dispose();
                // 仅当本次实例仍是当前倒计时时才清场，防止被新倒计时覆盖后误重置状态
                if (ReferenceEquals(_pauseCts, cts))
                {
                    ClearCommandLine();
                    _pauseActive = false;
                    _pauseCts = null;
                    // 若此时已进入命令模式（极窄竞态窗口），不能误调 OnExitCommandMode
                    if (!_inCommandMode)
                        OnExitCommandMode?.Invoke();
                }
            }
        }

        static void DrawPauseLine(int remaining)
        {
            try
            {
                _commandRow = Console.WindowTop + Console.WindowHeight - 1;
                var savedLeft = Console.CursorLeft;
                var savedTop = Console.CursorTop;
                Console.CursorVisible = false;
                Console.SetCursorPosition(0, _commandRow);

                var prevFg = Console.ForegroundColor;

                // 左侧：倒计时提示，秒数临近时变亮
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("按 空格/回车 继续 (");
                Console.ForegroundColor = remaining <= 2 ? ConsoleColor.Yellow : ConsoleColor.DarkYellow;
                Console.Write($"{remaining}s");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(")");
                Console.ForegroundColor = prevFg;

                var leftEnd = Console.CursorLeft;

                // 右侧：列出暂停期间可触发的 Instant 快捷键
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

                if (savedTop != _commandRow)
                    Console.SetCursorPosition(savedLeft, savedTop);
                else
                    Console.SetCursorPosition(Math.Min(leftEnd, Console.WindowWidth - 1), _commandRow);
                Console.CursorVisible = true;
            }
            catch { }
        }

        static void DrawCommandLine()
        {
            try
            {
                // 永远固定在可见区域最后一行
                _commandRow = Console.WindowTop + Console.WindowHeight - 1;
                Console.CursorVisible = false;
                Console.SetCursorPosition(0, _commandRow);

                var prevFg = Console.ForegroundColor;

                // 提示符（绿色）+ 输入内容（白色）
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("> ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(_buffer.ToString());
                Console.ForegroundColor = prevFg;

                // 写完后直接读列位置：终端对宽字符有感知，无需手动计算显示宽度
                var inputEnd = Math.Min(Console.CursorLeft, Console.WindowWidth - 1);

                // 右侧：固定提示，用 EstimateDisplayWidth 计算列宽（含宽字符）
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

                // 命令输入时光标始终留在命令行处，无论初始光标在哪里
                Console.SetCursorPosition(inputEnd, _commandRow);
                Console.CursorVisible = true;
            }
            catch { }
        }

        /// <summary>构建 Instant 快捷键的单行提示，格式：[Ctrl+C] 退出程序  [F1] 帮助</summary>
        static string BuildInstantHints()
        {
            var parts = _hotkeys
                .Where(kv => kv.Value.Instant)
                .Select(kv => $"[{FormatKeyCombo(kv.Key.Key, kv.Key.Modifiers)}] {kv.Value.Description}");
            return string.Join("  ", parts);
        }

        /// <summary>
        /// 估算字符串在终端中的显示列宽（宽字符按 2 列计，ASCII 按 1 列计）。
        /// 用于在写入前预算右对齐位置。
        /// </summary>
        internal static int EstimateDisplayWidth(string s)
        {
            var width = 0;
            foreach (var c in s)
                width += c >= 0x1100 ? 2 : 1;
            return width;
        }

        static void ClearCommandLine()
        {
            try
            {
                if (_commandRow >= 0 && _commandRow < Console.BufferHeight)
                {
                    Console.CursorVisible = false;
                    // 先擦除 popup（若有），再清命令行
                    _activeContext?.Erase(_commandRow);
                    _activeContext = null;
                    Console.SetCursorPosition(0, _commandRow);
                    Console.Write(new string(' ', Console.WindowWidth - 1));
                    // 还原到进入底部行占用前的光标位置，使 Live 恢复时不产生空白
                    Console.SetCursorPosition(_preCmdCursorLeft, _preCmdCursorTop);
                    Console.CursorVisible = true;
                }
            }
            catch { }
            _commandRow = -1;
        }
    }

    /// <summary>
    /// 快捷键 Handler 的输出上下文。向此对象写入的内容在 Live 最后一帧之后以浮动
    /// popup 的形式渲染在屏幕底部，倒计时结束或用户手动继续时自动消失。
    /// </summary>
    public sealed class KeyboardHandlerContext
    {
        readonly record struct Line(string Text, ConsoleColor Color);
        readonly List<Line> _lines = [];

        /// <summary>向 popup 追加一行文字。</summary>
        public KeyboardHandlerContext WriteLine(string text = "", ConsoleColor color = ConsoleColor.White)
        {
            _lines.Add(new Line(text, color));
            return this;
        }

        internal bool HasContent => _lines.Count > 0;

        // popup 实际占用行数：内容行 + 顶部边框 + 底部边框
        internal int TotalRows => _lines.Count > 0 ? _lines.Count + 2 : 0;

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

                // ── 顶部边框 ────────────────────────────────────────────────
                if (topRow >= 0)
                {
                    Console.SetCursorPosition(0, topRow);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('┌');
                    Console.Write(new string('─', w - 2));
                    Console.Write('┐');
                }

                // ── 内容行 ──────────────────────────────────────────────────
                for (var i = 0; i < _lines.Count; i++)
                {
                    var row = topRow + 1 + i;
                    if (row < 0 || row >= Console.BufferHeight) continue;
                    Console.SetCursorPosition(0, row);

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('│');

                    Console.ForegroundColor = _lines[i].Color;
                    Console.Write(' ');
                    Console.Write(_lines[i].Text);

                    // 补空白到右边框
                    var textDisplayW = 1 + KeyboardManager.EstimateDisplayWidth(_lines[i].Text);
                    var pad = contentW - textDisplayW;
                    if (pad > 0) Console.Write(new string(' ', pad));

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('│');
                }

                // ── 底部边框 ────────────────────────────────────────────────
                if (bottomRow >= 0 && bottomRow < Console.BufferHeight)
                {
                    Console.SetCursorPosition(0, bottomRow);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('└');
                    Console.Write(new string('─', w - 2));
                    Console.Write('┘');
                }

                Console.ForegroundColor = prevFg;
                // 无条件还原：Render 结束后光标必须回到调用前位置
                Console.SetCursorPosition(savedLeft, savedTop);
                Console.CursorVisible = true;
            }
            catch { }
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
            catch { }
        }
    }
}
