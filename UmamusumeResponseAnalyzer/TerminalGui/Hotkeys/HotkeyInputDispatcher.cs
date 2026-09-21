using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using UmamusumeResponseAnalyzer.Plugin;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class HotkeyInputDispatcher(HotkeyRuntime runtime)
{
    readonly SemaphoreSlim dispatchGate = new(1, 1);

    internal async Task HandleMouseWheelAsync(
        int steps,
        bool hasModifiers,
        bool isHorizontal)
    {
        await dispatchGate.WaitAsync();
        try
        {
            if (steps == 0 ||
                isHorizontal ||
                hasModifiers ||
                !runtime.TryCaptureWorkspaceSink(out var sink))
            {
                return;
            }

            var command = steps > 0 ? Command.Up : Command.Down;
            for (var remaining = Math.Abs(steps); remaining > 0; remaining--)
                await sink.TryHandleWorkspaceCommandAsync(command);
        }
        finally
        {
            dispatchGate.Release();
        }
    }

    internal async Task<bool> HandleKeyAsync(Key key)
    {
        await dispatchGate.WaitAsync();
        try
        {
            return await HandleKeyCoreAsync(key);
        }
        finally
        {
            dispatchGate.Release();
        }
    }

    async Task<bool> HandleKeyCoreAsync(Key key)
    {
        if (await TryHandlePopupShortcutAsync(key))
            return true;

        var hasActivePopup = runtime.HasPriorityPopup;
        if (hasActivePopup && await HandlePopupKeyAsync(key))
            return true;

        if (await TryHandleNotificationShortcutAsync(key))
            return true;

        if (!hasActivePopup &&
            runtime.TryCaptureWorkspaceSink(out var sink) &&
            TryGetWorkspaceCommand(key, out var workspaceCommand) &&
            await sink.TryHandleWorkspaceCommandAsync(workspaceCommand))
        {
            return true;
        }

        return await TryHandleHotkeyAsync(key);
    }

    static bool TryGetWorkspaceCommand(Key key, out Command command)
    {
        command = key.KeyCode switch
        {
            KeyCode.CursorUp => Command.Up,
            KeyCode.CursorDown => Command.Down,
            KeyCode.PageUp => Command.PageUp,
            KeyCode.PageDown => Command.PageDown,
            KeyCode.Home => Command.Start,
            KeyCode.End => Command.End,
            _ => Command.NotBound
        };
        return command != Command.NotBound;
    }

    async Task<bool> TryHandleHotkeyAsync(Key key)
    {
        var keyInfo = ConsoleKeyMapping.GetConsoleKeyInfoFromKeyCode(key.KeyCode);
        var entry = runtime.FindHotkey(keyInfo.Key, keyInfo.Modifiers);
        if (entry is null)
            return false;

        runtime.HidePopup();
        await InvokeSafely(entry);
        return true;
    }

    async Task<bool> TryHandlePopupShortcutAsync(Key key)
    {
        var keyInfo = ConsoleKeyMapping.GetConsoleKeyInfoFromKeyCode(key.KeyCode);
        var entry = runtime.FindPopupShortcut(keyInfo.Key, keyInfo.Modifiers);
        if (entry is null)
            return false;

        await InvokeSafely(entry);
        return true;
    }

    async Task<bool> TryHandleNotificationShortcutAsync(Key key)
    {
        var keyInfo = ConsoleKeyMapping.GetConsoleKeyInfoFromKeyCode(key.KeyCode);
        var entry = runtime.FindNotificationShortcut(
            keyInfo.Key,
            keyInfo.Modifiers,
            DateTimeOffset.Now);
        if (entry is null)
            return false;

        await InvokeSafely(entry);
        return true;
    }

    async Task<bool> HandlePopupKeyAsync(Key key)
    {
        if (key.IsCtrl || key.IsAlt || key.IsShift)
            return false;
        var selectable = runtime.HasSelectablePopup;

        switch (key.KeyCode)
        {
            case KeyCode.Enter when selectable:
                var handler = runtime.CloseAndGetPopupSelectionHandler();
                if (handler is not null)
                    await InvokeSafely(handler);
                return true;
            case KeyCode.Space:
            case KeyCode.Enter:
            case KeyCode.Esc:
                runtime.HidePopup();
                return true;
            case KeyCode.CursorUp:
            case KeyCode.CursorDown:
            case KeyCode.PageUp:
            case KeyCode.PageDown:
                var delta = key.KeyCode switch
                {
                    KeyCode.CursorUp => -1,
                    KeyCode.CursorDown => 1,
                    KeyCode.PageUp => -5,
                    _ => 5
                };
                if (selectable)
                    runtime.MovePopupSelection(delta);
                else
                    runtime.ScrollPopup(delta);
                return true;
            case KeyCode.Home:
            case KeyCode.End:
                var position = key.KeyCode == KeyCode.Home ? 0 : int.MaxValue;
                if (selectable)
                    runtime.SetPopupSelection(position);
                else
                    runtime.SetPopupScroll(position);
                return true;
            default:
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
            var failure = new InvalidOperationException(string.Format(i18n.Hotkey_HandlerFailed, ex.Message), ex);
            TerminalUi.Notify("Keyboard", failure.Message, UiSeverity.Error);
            TerminalUi.LogException("Keyboard", failure);
        }
    }

    static async Task InvokeSafely(HotkeyManager.HotkeyEntry entry)
    {
        using var registrationScope = HotkeyManager.RegisterScope(entry.Owner!);
        if (entry.Owner is IPlugin plugin)
        {
            await InvokeSafely(async () =>
            {
                using var callback = PluginManager.TryEnterPluginCallback(plugin);
                if (callback is null)
                    return;

                await entry.Handler();
            });
            return;
        }

        await InvokeSafely(entry.Handler);
    }
}
