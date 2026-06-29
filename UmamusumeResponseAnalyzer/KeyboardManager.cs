using System.Collections.Concurrent;
using UmamusumeResponseAnalyzer.LiveDisplay;

namespace UmamusumeResponseAnalyzer
{
    public static class KeyboardManager
    {
        public record HotkeyEntry(
            string Description,
            Func<Task> Handler);

        const int PollIntervalMs = 50;

        static readonly ConcurrentDictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), HotkeyEntry> hotkeys = [];
        static readonly object popupSync = new();

        static KeyboardPopup? activePopup;
        static CancellationTokenSource? runCts;
        static CancellationTokenSource? popupAutoCloseCts;
        static int inputSuspensionCount;
        static int popupGeneration;

        public static TimeSpan PopupAutoCloseDelay { get; set; } = TimeSpan.FromSeconds(3);
        internal static IKeyboardOverlaySink? OverlaySink { get; set; }

        public static void Register(
            ConsoleKey key,
            ConsoleModifiers modifiers,
            string description,
            Func<Task> handler)
        {
            RegisterCore(key, modifiers, description, handler);
        }

        public static void Register(
            ConsoleKey key,
            ConsoleModifiers modifiers,
            string description,
            Func<KeyboardHandlerContext, Task> handler)
        {
            RegisterCore(
                key,
                modifiers,
                description,
                async () =>
                {
                    var context = new KeyboardHandlerContext();
                    await handler(context);
                    ShowPopup(context.ToPopup());
                });
        }

        public static void Register(ConsoleKey key, string description, Func<Task> handler)
        {
            Register(key, 0, description, handler);
        }

        public static void Register(ConsoleKey key, string description, Func<KeyboardHandlerContext, Task> handler)
        {
            Register(key, 0, description, handler);
        }

        static void RegisterCore(
            ConsoleKey key,
            ConsoleModifiers modifiers,
            string description,
            Func<Task> handler)
        {
            if (modifiers.HasFlag(ConsoleModifiers.Control) &&
                key is ConsoleKey.S or ConsoleKey.Q or ConsoleKey.Z)
            {
                throw new InvalidOperationException($"Ctrl+{key} 由终端保留，不能注册为热键。");
            }

            hotkeys[(key, modifiers)] = new(
                description,
                handler);
        }

        public static bool Unregister(ConsoleKey key, ConsoleModifiers modifiers = 0)
        {
            return hotkeys.TryRemove((key, modifiers), out _);
        }

        public static void UnregisterAll()
        {
            hotkeys.Clear();
        }

        public static IReadOnlyDictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), HotkeyEntry> Hotkeys => hotkeys;

        public static void Stop()
        {
            try
            {
                Volatile.Read(ref runCts)?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public static IDisposable SuspendInput()
        {
            Interlocked.Increment(ref inputSuspensionCount);
            HidePopup();
            return new InputSuspension();
        }

        public static string FormatKeyCombo(ConsoleKey key, ConsoleModifiers modifiers)
        {
            var parts = new List<string>(3);
            if (modifiers.HasFlag(ConsoleModifiers.Control))
                parts.Add("Ctrl");
            if (modifiers.HasFlag(ConsoleModifiers.Alt))
                parts.Add("Alt");
            if (modifiers.HasFlag(ConsoleModifiers.Shift))
                parts.Add("Shift");

            var keyName = key switch
            {
                ConsoleKey.Oem1 => ";",
                ConsoleKey.Oem2 => "/",
                ConsoleKey.Oem3 => "`",
                ConsoleKey.Oem4 => "[",
                ConsoleKey.Oem5 => "\\",
                ConsoleKey.Oem6 => "]",
                ConsoleKey.Oem7 => "'",
                ConsoleKey.OemPlus => "+",
                ConsoleKey.OemMinus => "-",
                ConsoleKey.OemComma => ",",
                ConsoleKey.OemPeriod => ".",
                ConsoleKey.Spacebar => "Space",
                ConsoleKey.Enter => "Enter",
                ConsoleKey.Escape => "Esc",
                ConsoleKey.UpArrow => "↑",
                ConsoleKey.DownArrow => "↓",
                ConsoleKey.LeftArrow => "←",
                ConsoleKey.RightArrow => "→",
                _ => key.ToString()
            };
            parts.Add(keyName);

            return string.Join("+", parts);
        }

        public static async Task RunAsync(CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (Interlocked.CompareExchange(ref runCts, linkedCts, null) is not null)
                throw new InvalidOperationException("KeyboardManager.RunAsync 已在运行中。");

            var treatControlCAsInputChanged = false;
            var previousTreatControlCAsInput = false;
            try
            {
                previousTreatControlCAsInput = Console.TreatControlCAsInput;
                Console.TreatControlCAsInput = true;
                treatControlCAsInputChanged = true;
            }
            catch (IOException)
            {
            }

            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    if (Volatile.Read(ref inputSuspensionCount) > 0)
                    {
                        await DelayPollAsync(linkedCts.Token);
                        continue;
                    }

                    if (!TryKeyAvailable())
                    {
                        await DelayPollAsync(linkedCts.Token);
                        continue;
                    }

                    ConsoleKeyInfo keyInfo;
                    try
                    {
                        keyInfo = Console.ReadKey(intercept: true);
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }

                    await HandleKeyAsync(keyInfo);
                }
            }
            finally
            {
                if (treatControlCAsInputChanged)
                {
                    try
                    {
                        Console.TreatControlCAsInput = previousTreatControlCAsInput;
                    }
                    catch (IOException)
                    {
                    }
                }

                HidePopup();
                Interlocked.CompareExchange(ref runCts, null, linkedCts);
            }
        }

        internal static async Task HandleKeyAsync(ConsoleKeyInfo keyInfo)
        {
            if (await TryHandleHotkeyAsync(keyInfo))
                return;

            if (HasActivePopup())
                HandlePopupKey(keyInfo);
        }

        static async Task<bool> TryHandleHotkeyAsync(ConsoleKeyInfo keyInfo)
        {
            var combo = (keyInfo.Key, keyInfo.Modifiers);
            if (!hotkeys.TryGetValue(combo, out var entry))
                return false;

            HidePopup();
            await InvokeSafely(entry.Handler);
            return true;
        }

        static bool HandlePopupKey(ConsoleKeyInfo keyInfo)
        {
            if (keyInfo.Modifiers != 0)
                return false;

            switch (keyInfo.Key)
            {
                case ConsoleKey.Spacebar:
                case ConsoleKey.Enter:
                case ConsoleKey.Escape:
                    HidePopup();
                    return true;

                case ConsoleKey.UpArrow:
                    ScrollPopup(-1);
                    return true;

                case ConsoleKey.DownArrow:
                    ScrollPopup(1);
                    return true;

                case ConsoleKey.PageUp:
                    ScrollPopup(-5);
                    return true;

                case ConsoleKey.PageDown:
                    ScrollPopup(5);
                    return true;

                case ConsoleKey.Home:
                    SetPopupScroll(0);
                    return true;

                case ConsoleKey.End:
                    SetPopupScroll(int.MaxValue);
                    return true;

                default:
                    return false;
            }
        }

        static bool HasActivePopup()
        {
            lock (popupSync)
                return activePopup is not null;
        }

        static void ShowPopup(KeyboardPopup popup)
        {
            if (popup.Lines.Count == 0)
                return;

            var overlaySink = OverlaySink ?? throw new InvalidOperationException("Keyboard popup 需要先绑定 LiveDisplay overlay sink。");
            KeyboardPopup shownPopup;
            int generation;
            lock (popupSync)
            {
                shownPopup = popup with
                {
                    ScrollOffset = Math.Max(0, popup.ScrollOffset),
                    ExpiresAt = GetPopupExpiresAt()
                };
                activePopup = shownPopup;
                generation = unchecked(++popupGeneration);
            }

            overlaySink.ShowPopup(shownPopup);
            SchedulePopupAutoClose(generation, shownPopup.ExpiresAt);
        }

        static void HidePopup()
        {
            HidePopup(null);
        }

        static void HidePopup(int? generation)
        {
            IKeyboardOverlaySink? overlaySink;
            lock (popupSync)
            {
                if (generation is not null && generation.Value != popupGeneration)
                    return;

                if (activePopup is null)
                    return;

                activePopup = null;
                popupGeneration = unchecked(popupGeneration + 1);
                CancelPopupAutoCloseLocked();
                overlaySink = OverlaySink;
            }

            overlaySink?.HidePopup();
        }

        static async Task DelayPollAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(PollIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        static void ScrollPopup(int delta)
        {
            int scrollOffset;
            lock (popupSync)
            {
                if (activePopup is null)
                    return;

                scrollOffset = activePopup.ScrollOffset + delta;
            }

            SetPopupScroll(scrollOffset);
        }

        static void SetPopupScroll(int scrollOffset)
        {
            KeyboardPopup popup;
            int generation;
            lock (popupSync)
            {
                if (activePopup is null)
                    return;

                var visibleCount = Math.Min(activePopup.Lines.Count, EstimateVisiblePopupLines());
                var maxOffset = Math.Max(0, activePopup.Lines.Count - visibleCount);
                popup = activePopup with
                {
                    ScrollOffset = Math.Clamp(scrollOffset, 0, maxOffset),
                    ExpiresAt = GetPopupExpiresAt()
                };
                activePopup = popup;
                generation = unchecked(++popupGeneration);
            }

            OverlaySink?.ShowPopup(popup);
            SchedulePopupAutoClose(generation, popup.ExpiresAt);
        }

        static DateTimeOffset? GetPopupExpiresAt()
        {
            var delay = PopupAutoCloseDelay;
            return delay <= TimeSpan.Zero ? null : DateTimeOffset.Now.Add(delay);
        }

        static void SchedulePopupAutoClose(int generation, DateTimeOffset? expiresAt)
        {
            CancellationToken token;
            lock (popupSync)
            {
                CancelPopupAutoCloseLocked();
                if (expiresAt is null || activePopup is null || generation != popupGeneration)
                    return;

                popupAutoCloseCts = new();
                token = popupAutoCloseCts.Token;
            }

            _ = AutoClosePopupAsync(generation, expiresAt.Value, token);
        }

        static async Task AutoClosePopupAsync(int generation, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            try
            {
                var delay = expiresAt - DateTimeOffset.Now;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                if (!cancellationToken.IsCancellationRequested)
                    HidePopup(generation);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ArgumentOutOfRangeException)
            {
                HidePopup(generation);
            }
        }

        static void CancelPopupAutoCloseLocked()
        {
            popupAutoCloseCts?.Cancel();
            popupAutoCloseCts?.Dispose();
            popupAutoCloseCts = null;
        }

        static int EstimateVisiblePopupLines()
        {
            try
            {
                return Math.Max(1, Console.WindowHeight - 2);
            }
            catch (IOException)
            {
                return 1;
            }
            catch (InvalidOperationException)
            {
                return 1;
            }
        }

        static bool TryKeyAvailable()
        {
            try
            {
                return Console.KeyAvailable;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        static async Task InvokeSafely(Func<Task> handler)
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                LiveDisplayConsole.Notify("Keyboard", $"热键处理失败: {ex.Message}", LiveDisplaySeverity.Error);
                LiveDisplayConsole.Log("Keyboard", ex.ToString(), LiveDisplaySeverity.Error);
            }
        }

        sealed class InputSuspension : IDisposable
        {
            int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                    Interlocked.Decrement(ref inputSuspensionCount);
            }
        }
    }
}
