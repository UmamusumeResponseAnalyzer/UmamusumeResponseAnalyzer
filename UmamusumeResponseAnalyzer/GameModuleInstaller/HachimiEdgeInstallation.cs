using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer;

internal static class HachimiEdgeInstallation
{
    internal const string RegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    const string LockFileName = ".ura-hachimi-edge.lock";

    internal static void EnsureGameStopped(HachimiEdgeGame game)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game.Executable)))
        {
            using (process)
            {
                if (!process.HasExited)
                    throw new IOException($"请先关闭游戏。 / Close the game before installing: {Path.GetFileName(game.Executable)} (PID {process.Id})");
            }
        }
    }

    internal static void ValidateTargetPaths(HachimiEdgeGame game)
    {
        RejectLinks(game.Directory);
        foreach (var relative in game.TargetPaths)
        {
            var path = Path.Combine(game.Directory, relative);
            RejectLinks(path, allowFileLink: true);
            if (Directory.Exists(path)) throw new IOException($"目标文件被目录占用。 / A directory occupies the target file: {path}");
        }
        RejectLinks(Path.Combine(game.Directory, LockFileName));
    }

    static void RejectLinks(string path, bool allowFileLink = false)
    {
        if (allowFileLink && new FileInfo(path) is { LinkTarget: not null } link &&
            (link.Attributes & FileAttributes.Directory) == 0)
            path = Path.GetDirectoryName(Path.GetFullPath(path))!;
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"此路径不支持文件系统链接。 / Filesystem links are not supported at this path: {current}");
    }

    internal static bool RequiresElevation(HachimiEdgeGame game, bool registryChange)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Hachimi-Edge installation requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return false;
        if (game.Platform == HachimiEdgePlatform.Dmm && registryChange) return true;
        try
        {
            var directories = game.TargetPaths.Append(LockFileName).Select(relative =>
            {
                var directory = Path.GetDirectoryName(Path.Combine(game.Directory, relative))!;
                while (!Directory.Exists(directory)) directory = Path.GetDirectoryName(directory)!;
                return directory;
            }).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories)
            {
                using var probe = new FileStream(Path.Combine(directory, $".ura-access-{Guid.NewGuid():N}.tmp"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            foreach (var relative in game.TargetPaths)
            {
                var path = Path.Combine(game.Directory, relative);
                if (File.Exists(path) && new FileInfo(path).LinkTarget is null && (File.GetAttributes(path) & FileAttributes.ReadOnly) == 0)
                {
                    using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                }
            }
            return false;
        }
        catch (UnauthorizedAccessException) { return true; }
    }

    internal static async Task ApplyAsync(string requestPath, bool elevate, IProgress<DownloadProgress>? progress = null,
        Func<ProcessStartInfo, Task<int>>? runElevated = null)
    {
        if (!elevate) { Execute(requestPath, progress); return; }
        var start = CreateElevationStartInfo(requestPath);
        int exitCode;
        try { exitCode = await (runElevated ?? StartAndWaitAsync)(start); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("用户取消了提权。 / Elevation was cancelled.", ex);
        }
        if (exitCode == 0) return;
        var errorPath = Path.Combine(Path.GetDirectoryName(requestPath)!, "error.json");
        var error = File.Exists(errorPath)
            ? JsonSerializer.Deserialize<string>(File.ReadAllText(errorPath), HachimiEdgeInstaller.JsonOptions) : null;
        throw new IOException(error ?? $"安装进程异常退出，请重新安装。 / Installer exited unexpectedly ({exitCode}); run the installation again.");
    }

    internal static ProcessStartInfo CreateElevationStartInfo(string path)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the URA executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(HachimiEdgeInstallation).Assembly.Location);
        start.ArgumentList.Add("--apply-hachimi-edge");
        start.ArgumentList.Add(Path.GetFullPath(path));
        start.ArgumentList.Add("--confirmed");
        return start;
    }

    static async Task<int> StartAndWaitAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new IOException("无法启动安装进程。 / Could not start the installer.");
        // Closing the progress dialog must not leave a child writing files in the background.
        await process.WaitForExitAsync(CancellationToken.None);
        return process.ExitCode;
    }

    internal static int RunApplyCommand(string requestPath)
    {
        string? errorPath = null;
        try
        {
            ValidateRequestPath(requestPath);
            errorPath = Path.Combine(Path.GetDirectoryName(requestPath)!, "error.json");
            Execute(requestPath);
            return 0;
        }
        catch (Exception ex)
        {
            var error = TerminalUi.FormatExceptionLogMessage(ex);
            Console.Error.WriteLine(error);
            if (errorPath is not null) WriteJson(errorPath, error);
            return 1;
        }
    }

    static void ValidateRequestPath(string requestPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
        var name = Path.GetFileName(directory);
        if (Path.GetFileName(requestPath) != "request.json" || !name.StartsWith("ura-hachimi-edge-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name["ura-hachimi-edge-".Length..], "N", out _))
            throw new InvalidDataException("无效的安装暂存路径。 / Invalid installer staging path.");
        RejectLinks(requestPath);
    }

    internal static void Execute(string requestPath, IProgress<DownloadProgress>? progress = null, RegistryKey? registry = null)
    {
        ValidateRequestPath(requestPath);
        var request = JsonSerializer.Deserialize<HachimiEdgeRequest>(File.ReadAllText(requestPath), HachimiEdgeInstaller.JsonOptions)
            ?? throw new InvalidDataException("Empty installation request.");
        var game = HachimiEdgeGame.FromExecutable(request.Executable);
        ValidateTargetPaths(game);
        if (request.Components is null || !request.Components.Select(c => c?.Name).SequenceEqual(game.Binaries.Select(b => b.Component)))
            throw new InvalidDataException("无效的安装组件。 / Invalid installation components.");
        HachimiEdgeInstaller.NormalizeNotifier(request.NotifierHost);
        EnsureGameStopped(game);
        var staging = Path.GetDirectoryName(requestPath)!;
        foreach (var component in request.Components)
        {
            var path = Path.Combine(staging, component.Name + ".bin");
            RejectLinks(path);
            HachimiEdgeInstaller.ValidateBinary(path, component);
        }
        using var installLock = new FileStream(Path.Combine(game.Directory, LockFileName), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        if (game.Platform == HachimiEdgePlatform.Dmm && ReadDllRedirection(registry) != request.DllRedirectionBefore)
            throw new IOException("DLL redirection 设置已改变，请重新安装。 / DLL redirection changed; start again.");
        var merged = HachimiEdgeInstaller.MergeConfigurations(game.Directory, request.NotifierHost);
        var completed = 0;
        foreach (var (binary, component) in game.Binaries.Zip(request.Components))
        {
            using var input = File.OpenRead(Path.Combine(staging, binary.Component + ".bin"));
            ReplaceFile(Path.Combine(game.Directory, binary.RelativePath), input.CopyTo, component);
            progress?.Report(new("apply", HachimiEdgeInstaller.Text("Applying"), ++completed, game.TargetPaths.Length));
        }
        foreach (var (relative, content) in new[] { (HachimiEdgeGame.ConfigPath, merged.Config), (HachimiEdgeGame.ForwardConfigPath, merged.ForwardConfig) })
        {
            ReplaceFile(Path.Combine(game.Directory, relative), output => output.Write(System.Text.Encoding.UTF8.GetBytes(content)));
            progress?.Report(new("apply", HachimiEdgeInstaller.Text("Applying"), ++completed, game.TargetPaths.Length));
        }
        if (game.Platform == HachimiEdgePlatform.Dmm) EnableDllRedirection(registry);
    }

    internal static int? ReadDllRedirection(RegistryKey? key = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var machine = key is null ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64) : null;
        using var opened = key is null ? machine!.OpenSubKey(RegistryPath) : null;
        var target = key ?? opened;
        var value = target?.GetValue("DevOverrideEnable");
        if (value is null) return null;
        if (value is not int number || target!.GetValueKind("DevOverrideEnable") != RegistryValueKind.DWord)
            throw new InvalidDataException("DevOverrideEnable 必须为 DWORD。 / DevOverrideEnable must be a DWORD.");
        return number;
    }

    internal static bool EnableDllRedirection(RegistryKey? key = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (ReadDllRedirection(key) == 1) return false;
        using var machine = key is null ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64) : null;
        using var opened = key is null ? machine!.CreateSubKey(RegistryPath, true) : null;
        var target = key ?? opened!;
        target.SetValue("DevOverrideEnable", 1, RegistryValueKind.DWord);
        target.Flush();
        if (ReadDllRedirection(key) != 1) throw new IOException("DLL redirection 注册表写入验证失败。 / Registry write verification failed.");
        return true;
    }

    internal static void WriteJson<T>(string path, T value)
    {
        RejectLinks(path);
        // Recreate the file to keep writes from following hard links.
        File.Delete(path);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, value, HachimiEdgeInstaller.JsonOptions);
    }

    static void ReplaceFile(string target, Action<Stream> write, HachimiEdgeComponent? component = null)
    {
        RejectLinks(target, allowFileLink: true);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        // Stage beside the target so replacement stays on the same volume, including file symlinks.
        var temp = Path.Combine(directory, $".ura-hachimi-edge-{Guid.NewGuid():N}.tmp");
        var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
        try
        {
            using (output) { write(output); output.Flush(true); }
            if (component is not null) HachimiEdgeInstaller.ValidateBinary(temp, component);
            // Changing attributes through a symlink would modify the external file.
            if (File.Exists(target) && new FileInfo(target).LinkTarget is null)
            {
                var attributes = File.GetAttributes(target);
                if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
            }
            File.Move(temp, target, true);
        }
        finally { File.Delete(temp); }
    }

    internal static void DeleteOwnedDirectory(string parent, string name)
    {
        var id = name.StartsWith("ura-hachimi-edge-", StringComparison.Ordinal) ? name["ura-hachimi-edge-".Length..] : name;
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid owned directory name.");
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(root, name));
        if (!Path.GetDirectoryName(target)!.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Directory left its parent.");
        RejectLinks(target);
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
