using System.Globalization;
using System.Net.Http.Json;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer;

internal sealed record HachimiEdgeComponent(string Name, string Version, long Size, string Sha256, string DownloadUrl);
internal sealed record HachimiEdgeSnapshot(string Client, HachimiEdgeComponent[] Components);
internal sealed record HachimiEdgeRequest(string Executable, string NotifierHost,
    HachimiEdgeComponent[] Components, int? DllRedirectionBefore);

internal static class HachimiEdgeInstaller
{
    internal const string ApiRoot = "https://ura.shuise.net/api/";
    internal const long MaxComponentBytes = 256 * 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    static readonly ResourceManager Resources = new("UmamusumeResponseAnalyzer.Localization.HachimiEdge", typeof(HachimiEdgeInstaller).Assembly);

    internal static string Text(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Resources.GetString(key, CultureInfo.CurrentUICulture)!, args);

    internal static async Task ShowAsync(CancellationToken cancellationToken)
    {
        string? staging = null;
        try
        {
            var warnings = new List<string>();
            var games = UraCoreHelper.LoadGamePaths(warnings)
                .SelectMany(directory => HachimiEdgeGame.ExecutableNames.Select(name => Path.Combine(directory, name)))
                .Where(File.Exists).Select(path => HachimiEdgeGame.FromExecutable(path)).ToArray();
            foreach (var discoveryWarning in warnings) TerminalUi.Log("Hachimi-Edge", discoveryWarning, UiSeverity.Warning);
            var choice = ModalDialogs.Select(Text("SelectGame"), Enumerable.Range(0, games.Length + 1),
                i => i == games.Length ? Text("Browse") : $"{games[i].Label} · {games[i].Directory}", cancellationToken);
            var game = choice == games.Length
                ? HachimiEdgeGame.FromExecutable(ModalDialogs.PickExecutable(Text("SelectExecutable"), cancellationToken))
                : games[choice];
            HachimiEdgeInstallation.EnsureGameStopped(game);
            var notifier = DefaultNotifier(Config.Core.ListenAddress, Config.Core.ListenPort);
            staging = Path.Combine(Path.GetTempPath(), "ura-hachimi-edge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            HachimiEdgeRequest? request = null;
            await ModalDialogs.RunProgressAsync(async (progress, token) =>
                request = await PrepareAsync(game, notifier, staging, progress, token), cancellationToken);
            var elevate = HachimiEdgeInstallation.RequiresElevation(game, request!.DllRedirectionBefore != 1);

            var completed = false;
            try
            {
                await ModalDialogs.RunProgressAsync(async (progress, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    progress.Report(new("apply", Text("Applying"), 0, 1));
                    await HachimiEdgeInstallation.ApplyAsync(Path.Combine(staging, "request.json"), elevate, progress);
                    completed = true;
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (completed) { }
            if (!cancellationToken.IsCancellationRequested)
                ModalDialogs.Acknowledge(Text("Installed", game.Directory) +
                    (game.Platform == HachimiEdgePlatform.Dmm && request.DllRedirectionBefore != 1 ? "\n" + Text("RestartWindows") : ""), cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TerminalUi.LogException("Hachimi-Edge", ex);
            ModalDialogs.Acknowledge(Text("Failed") + "\n" + TerminalUi.FormatExceptionLogMessage(ex), cancellationToken);
        }
        finally
        {
            if (staging is not null)
                HachimiEdgeInstallation.DeleteOwnedDirectory(Path.GetTempPath(), Path.GetFileName(staging));
        }
    }

    internal static async Task<HachimiEdgeRequest> PrepareAsync(HachimiEdgeGame game, string notifier,
        string staging, IProgress<DownloadProgress>? progress, CancellationToken ct, RegistryKey? registry = null)
    {
        HachimiEdgeInstallation.ValidateTargetPaths(game);
        HachimiEdgeInstallation.EnsureGameStopped(game);
        notifier = NormalizeNotifier(notifier);
        var snapshot = await ResourceUpdater.HttpClient.GetFromJsonAsync<HachimiEdgeSnapshot>(ApiRoot + "HachimiEdge?client=" + game.Client, JsonOptions, ct)
            ?? throw new InvalidDataException("URACloud 返回空组件配置。 / URACloud returned an empty component configuration.");
        ValidateSnapshot(snapshot, game.Client);
        var components = game.Binaries.Select(b => snapshot.Components.Single(c => c.Name == b.Component)).ToArray();
        foreach (var component in components)
        {
            var path = Path.Combine(staging, component.Name + ".bin");
            await ResourceUpdater.Download(progress, $"{component.Name} {component.Version}", path, ct,
                downloadUrl: ApiRoot + component.DownloadUrl, expectedLength: component.Size);
            ValidateBinary(path, component);
        }
        var request = new HachimiEdgeRequest(game.Executable, notifier, components,
            game.Platform == HachimiEdgePlatform.Dmm ? HachimiEdgeInstallation.ReadDllRedirection(registry) : null);
        // Validate both existing configurations before applying files.
        MergeConfigurations(game.Directory, notifier);
        HachimiEdgeInstallation.WriteJson(Path.Combine(staging, "request.json"), request);
        return request;
    }

    internal static void ValidateSnapshot(HachimiEdgeSnapshot snapshot, string client)
    {
        if (snapshot.Client != client)
            throw new InvalidDataException($"URACloud 返回的客户端不匹配。 / URACloud client mismatch: expected {client}, received {snapshot.Client}.");
        var names = new[] { "edge", "httpforward", "cellar", "funnyhoney" };
        if (snapshot.Components is null || snapshot.Components.Length != 4 ||
            !snapshot.Components.Select(c => c?.Name).Order().SequenceEqual(names.Order()))
            throw new InvalidDataException("URACloud 组件列表不完整或重复。 / Invalid URACloud component list.");
        foreach (var component in snapshot.Components)
            if (string.IsNullOrWhiteSpace(component.Version) || component.Size is <= 0 or > MaxComponentBytes ||
                component.Sha256 is not { Length: 64 } || !component.Sha256.All(Uri.IsHexDigit) ||
                component.DownloadUrl != $"HachimiEdge/components/{component.Sha256}")
                throw new InvalidDataException($"URACloud 组件元数据无效。 / Invalid component metadata: {component.Name}");
    }

    internal static void ValidateBinary(string path, HachimiEdgeComponent component)
    {
        using var file = File.OpenRead(path);
        if (file.Length != component.Size || !Convert.ToHexStringLower(SHA256.HashData(file)).Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"组件长度或 SHA-256 不符。 / Component length or SHA-256 mismatch: {component.Name}");
        file.Position = 0;
        using var pe = new PEReader(file);
        if (pe.PEHeaders.PEHeader is null || pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
            throw new InvalidDataException($"组件必须是 Windows x64 PE 文件。 / Expected a Windows x64 PE file: {component.Name}");
    }

    internal static (string Config, string ForwardConfig) MergeConfigurations(string directory, string notifier)
    {
        var config = ReadConfiguration(Path.Combine(directory, HachimiEdgeGame.ConfigPath));
        JsonArray libraries;
        if (!config.TryGetPropertyValue("load_libraries", out var node)) config["load_libraries"] = libraries = [];
        else if (node is JsonArray list && list.All(n => n is JsonValue value && value.TryGetValue<string>(out _))) libraries = list;
        else throw new InvalidDataException("hachimi/config.json: load_libraries 必须为字符串数组。 / Expected an array of strings.");
        const string library = @"hachimi\hachimi_httpforward_plugin.dll";
        if (!libraries.Any(n => n!.GetValue<string>().Replace('/', '\\').Equals(library, StringComparison.OrdinalIgnoreCase)))
            libraries.Add(library);

        var forward = ReadConfiguration(Path.Combine(directory, HachimiEdgeGame.ForwardConfigPath));
        if (forward.TryGetPropertyValue("notifier_host", out var host) &&
            (host is not JsonValue hostValue || !hostValue.TryGetValue<string>(out _)))
            throw new InvalidDataException("httpforward.json: notifier_host 必须是字符串。 / Expected a string.");
        if (!forward.TryGetPropertyValue("notifier_timeout_ms", out var timeout)) forward["notifier_timeout_ms"] = 100;
        else if (timeout is not JsonValue timeoutValue || !timeoutValue.TryGetValue<ulong>(out var milliseconds) || milliseconds == 0)
            throw new InvalidDataException("httpforward.json: notifier_timeout_ms 必须是正整数。 / Expected a positive integer.");
        forward["notifier_host"] = NormalizeNotifier(notifier);
        return (config.ToJsonString(JsonOptions), forward.ToJsonString(JsonOptions));
    }

    static JsonObject ReadConfiguration(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new JsonException("Expected a JSON object.");
        }
        catch (JsonException ex) { throw new InvalidDataException($"配置文件无法解析。 / Invalid configuration: {path}\n{ex.Message}", ex); }
    }

    internal static string DefaultNotifier(string address, int port) => NormalizeNotifier(new UriBuilder("http", address switch
    {
        "0.0.0.0" or "*" or "+" => "127.0.0.1",
        "::" or "[::]" => "::1",
        _ => address
    }, port).Uri.AbsoluteUri);

    internal static string NormalizeNotifier(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("转发地址必须为 HTTP(S) 地址，且不含凭据、查询或片段。 / Expected an HTTP(S) URL without credentials, query or fragment.");
        return uri.AbsoluteUri.TrimEnd('/');
    }

}
