namespace UmamusumeResponseAnalyzer;

internal enum HachimiEdgePlatform { Dmm, Taiwan, SteamJapan, SteamGlobal }

internal sealed record HachimiEdgeGame(string Executable, HachimiEdgePlatform Platform, string Client, string Label)
{
    static readonly (string Exe, HachimiEdgePlatform Platform, string Client, string Label)[] Clients =
    [
        ("umamusume.exe", HachimiEdgePlatform.Dmm, "dmm", "DMM · JP"),
        ("komoeumamusume.exe", HachimiEdgePlatform.Taiwan, "taiwan", "Komoe · TW"),
        ("UmamusumePrettyDerby_Jpn.exe", HachimiEdgePlatform.SteamJapan, "steamJapan", "Steam · JP"),
        ("UmamusumePrettyDerby.exe", HachimiEdgePlatform.SteamGlobal, "steamGlobal", "Steam · Global")
    ];
    internal static readonly string[] ExecutableNames = [.. Clients.Select(c => c.Exe)];

    internal string Directory => Path.GetDirectoryName(Executable)!;

    internal static HachimiEdgeGame FromExecutable(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var index = Array.FindIndex(ExecutableNames, name => name.Equals(Path.GetFileName(fullPath), StringComparison.OrdinalIgnoreCase));
        if (index < 0 || !File.Exists(fullPath))
            throw new InvalidDataException($"请选择受支持的游戏 EXE。 / Select a supported game executable: {string.Join(", ", ExecutableNames)}\n{fullPath}");
        var client = Clients[index];
        return new(fullPath, client.Platform, client.Client, client.Label);
    }

    internal (string Component, string RelativePath)[] Binaries => Platform switch
    {
        HachimiEdgePlatform.Dmm => [("edge", "umamusume.exe.local/UnityPlayer.dll"),
            ("cellar", "umamusume.exe.local/apphelp.dll"), ("httpforward", ForwarderPath)],
        HachimiEdgePlatform.Taiwan => [("edge", "winhttp.dll"), ("httpforward", ForwarderPath)],
        HachimiEdgePlatform.SteamGlobal => [("edge", "cri_mana_vpx.dll"), ("httpforward", ForwarderPath)],
        HachimiEdgePlatform.SteamJapan => [("edge", "cri_mana_vpx.dll"),
            ("funnyhoney", "UmamusumePrettyDerby_Jpn.exe"), ("httpforward", ForwarderPath)],
        _ => throw new InvalidOperationException()
    };

    internal const string ForwarderPath = "hachimi/hachimi_httpforward_plugin.dll";
    internal const string ConfigPath = "hachimi/config.json";
    internal const string ForwardConfigPath = "hachimi/httpforward.json";
    internal string[] TargetPaths => [.. Binaries.Select(b => b.RelativePath), ConfigPath, ForwardConfigPath];
}
