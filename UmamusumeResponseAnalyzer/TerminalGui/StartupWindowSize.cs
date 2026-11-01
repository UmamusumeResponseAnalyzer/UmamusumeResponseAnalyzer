using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Terminal.Gui.Drivers;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal static partial class StartupWindowSize
{
    internal const int Columns = 120;
    internal const int Rows = 35;

    internal static void Apply(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || Config.Misc.StartupWindowSize == StartupWindowSizeMode.Never)
            return;

        var windowHandle = GetConsoleWindow();
        var isClassicConsole = false;
        if (IsWindowVisible(windowHandle))
        {
            // PseudoConsoleWindow can also be visible; only ConsoleWindowClass is the classic host.
            var className = new char[256];
            var length = GetClassNameW(windowHandle, className, className.Length);
            if (length == 0)
                throw NativeFailure("GetClassNameW");
            isClassicConsole = className.AsSpan(0, length).SequenceEqual("ConsoleWindowClass");
        }
        if (!isClassicConsole && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION")))
            return;

        var app = TerminalUi.RequireHost().Application;
        var size = app.Screen.Size;
        if (size.Width == Columns && size.Height == Rows)
            return;

        switch (Config.Misc.StartupWindowSize)
        {
            case StartupWindowSizeMode.Ask:
                var (resize, preference) = ModalDialogs.AskStartupWindowSize(size.Width, size.Height, cancellationToken);
                if (preference != StartupWindowSizeMode.Ask)
                {
                    Config.Misc.StartupWindowSize = preference;
                    Config.Save();
                }
                if (!resize)
                    return;
                break;
            case StartupWindowSizeMode.Always:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Config.Misc.StartupWindowSize), Config.Misc.StartupWindowSize, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!isClassicConsole)
        {
            app.Driver!.WriteRaw(EscSeqUtils.CSI_SetTerminalWindowSize(Rows, Columns));
            return;
        }

        // Terminal.Gui can activate a different buffer from the standard output handle.
        const uint genericReadWrite = 0xC0000000;
        const uint shareReadWrite = 3;
        const uint openExisting = 3;
        using var output = CreateFileW("CONOUT$", genericReadWrite, shareReadWrite, 0, openExisting, 0, 0);
        if (output.IsInvalid)
            throw NativeFailure("CreateFileW(CONOUT$)");
        if (!GetConsoleScreenBufferInfo(output, out var info))
            throw NativeFailure("GetConsoleScreenBufferInfo(CONOUT$)");
        var bufferSize = new Coord
        {
            X = Math.Max(info.Size.X, (short)Columns),
            Y = Math.Max(info.Size.Y, (short)Rows)
        };
        if ((bufferSize.X != info.Size.X || bufferSize.Y != info.Size.Y) &&
            !SetConsoleScreenBufferSize(output, bufferSize))
            throw NativeFailure("SetConsoleScreenBufferSize(CONOUT$)");
        var window = new SmallRect { Right = Columns - 1, Bottom = Rows - 1 };
        if (!SetConsoleWindowInfo(output, true, ref window))
            throw NativeFailure("SetConsoleWindowInfo(CONOUT$)");
    }

    static Win32Exception NativeFailure(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"{operation}: {new Win32Exception(error).Message} (Win32 {error})");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Coord { internal short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct SmallRect { internal short Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct ConsoleScreenBufferInfo
    {
        internal Coord Size, CursorPosition;
        internal ushort Attributes;
        internal SmallRect Window;
        internal Coord MaximumWindowSize;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetClassNameW(nint window, [Out] char[] className, int count);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(SafeFileHandle output, out ConsoleScreenBufferInfo info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleScreenBufferSize(SafeFileHandle output, Coord size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleWindowInfo(SafeFileHandle output, [MarshalAs(UnmanagedType.Bool)] bool absolute, ref SmallRect window);
}
