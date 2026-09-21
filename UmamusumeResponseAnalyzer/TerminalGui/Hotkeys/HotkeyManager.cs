using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using Terminal.Gui.Input;

namespace UmamusumeResponseAnalyzer.TerminalGui;

public static class HotkeyManager
{
    public sealed class HotkeyEntry(
        string description,
        Func<Task> handler,
        object? owner = null)
    {
        public string Description { get; } = description;
        public Func<Task> Handler { get; } = handler;
        public object? Owner { get; } = owner;
    }

    static readonly AsyncLocal<object?> registrationOwner = new();
    static readonly HotkeyRuntime runtime = new();
    static readonly HotkeyInputDispatcher inputDispatcher = new(runtime);

    public static TimeSpan PopupAutoCloseDelay
    {
        get => runtime.PopupAutoCloseDelay;
        set => runtime.PopupAutoCloseDelay = value;
    }

    internal static IUiInputSink? OverlaySink
    {
        get => runtime.OverlaySink;
        set => runtime.OverlaySink = value;
    }

    internal static bool HasPriorityPopup => runtime.HasPriorityPopup;

    public static void Register(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        string description,
        Func<Task> handler)
        => RegisterCore(key, modifiers, description, handler);

    public static void Register(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        string description,
        Func<HotkeyContext, Task> handler)
        => RegisterCore(key, modifiers, description, CreateContextHandler(handler));

    public static void Register(ConsoleKey key, string description, Func<Task> handler)
        => RegisterCore(key, 0, description, handler);

    public static void Register(
        ConsoleKey key,
        string description,
        Func<HotkeyContext, Task> handler)
        => RegisterCore(key, 0, description, CreateContextHandler(handler));

    internal static HotkeyEntry CaptureTracked(
        string description,
        Func<Task> handler)
        => new(description, handler, registrationOwner.Value);

    internal static HotkeyEntry? RegisterTracked(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        HotkeyEntry entry)
        => runtime.RegisterTracked(key, modifiers, entry);

    internal static void RestoreTracked(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        HotkeyEntry entry,
        HotkeyEntry? replaced)
        => runtime.RestoreTracked(key, modifiers, entry, replaced);

    static Func<Task> CreateContextHandler(Func<HotkeyContext, Task> handler)
    {
        return async () =>
        {
            var context = new HotkeyContext();
            await handler(context);
            ShowPopup(context.ToPopup());
        };
    }

    static HotkeyEntry RegisterCore(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        string description,
        Func<Task> handler)
    {
        if (modifiers.HasFlag(ConsoleModifiers.Control) &&
            key is ConsoleKey.S or ConsoleKey.Q or ConsoleKey.Z)
        {
            throw new InvalidOperationException(string.Format(i18n.Hotkey_Reserved, key));
        }

        var entry = new HotkeyEntry(description, handler, registrationOwner.Value);
        runtime.Register(key, modifiers, entry);
        return entry;
    }

    public static bool Unregister(ConsoleKey key, ConsoleModifiers modifiers = 0)
        => runtime.Unregister(key, modifiers);

    internal static bool Unregister(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        HotkeyEntry entry)
        => runtime.Unregister(key, modifiers, entry);

    public static void UnregisterAll()
        => runtime.Clear();

    public static IDisposable RegisterScope(object owner)
    {
        var previous = registrationOwner.Value;
        registrationOwner.Value = owner;
        return new RegistrationScope(previous);
    }

    public static int UnregisterByOwner(object owner)
    {
        var workspaceCount = (runtime.OverlaySink as UiHost)?
            .RemoveWorkspaceHotkeysByOwner(owner) ?? 0;
        return workspaceCount + runtime.UnregisterByOwner(owner);
    }

    public static IReadOnlyDictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), HotkeyEntry>
        Hotkeys => runtime.SnapshotHotkeys();

    public static string FormatKeyCombo(ConsoleKey key, ConsoleModifiers modifiers)
    {
        var parts = new List<string>(3);
        if (modifiers.HasFlag(ConsoleModifiers.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ConsoleModifiers.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ConsoleModifiers.Shift))
            parts.Add("Shift");

        parts.Add(key switch
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
        });
        return string.Join("+", parts);
    }

    internal static Task HandleMouseWheelAsync(
        int steps,
        bool hasModifiers,
        bool isHorizontal = false)
        => inputDispatcher.HandleMouseWheelAsync(steps, hasModifiers, isHorizontal);

    internal static Task<bool> HandleKeyAsync(Key key)
        => inputDispatcher.HandleKeyAsync(key);

    internal static void ShowPopup(HotkeyContext context)
        => ShowPopup(context.ToPopup());

    internal static void ShowPopup(HotkeyPopup popup)
        => runtime.ShowPopup(popup, registrationOwner.Value);

    internal static long RegisterNotificationShortcuts(
        DateTimeOffset expiresAt,
        IReadOnlyList<UiShortcut> shortcuts)
        => runtime.RegisterNotificationShortcuts(
            expiresAt,
            shortcuts,
            registrationOwner.Value);

    internal static void UnregisterNotificationShortcuts(long registrationId)
        => runtime.UnregisterNotificationShortcuts(registrationId);

    sealed class RegistrationScope(object? previous) : IDisposable
    {
        int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                registrationOwner.Value = previous;
        }
    }
}
