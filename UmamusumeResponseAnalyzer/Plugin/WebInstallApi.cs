using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UmamusumeResponseAnalyzer.TerminalGui;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace UmamusumeResponseAnalyzer.Plugin;

/// <summary>Local URACloud integration: numeric references, allowed origins, and Host-side confirmation.</summary>
internal static class WebInstallApi
{
    static readonly HashSet<string> AllowedOrigins = new(StringComparer.Ordinal)
    {
        "https://ura.shuise.net",
    };
    static readonly JsonSerializerSettings JsonSettings = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };

    internal static Func<PluginInformation, CancellationToken, bool> ConfirmInstall = (plugin, ct) =>
        ModalDialogs.Confirm(BuildInstallConfirmation(plugin), cancellationToken: ct);

    internal static string BuildInstallConfirmation(PluginInformation plugin) =>
        string.Format(i18n.InstallConfirmation, plugin.DisplayName, plugin.RawVersion,
            plugin.Author, plugin.InternalName, plugin.RepositoryUrl);

    public static void Register(WebserverLite server, CancellationToken cancellationToken)
    {
        var routes = server.Routes.PreAuthentication.Static;
        routes.Add(WatsonWebserver.Core.HttpMethod.OPTIONS, "/uracloud/status", ctx => Preflight(ctx, cancellationToken));
        routes.Add(WatsonWebserver.Core.HttpMethod.OPTIONS, "/uracloud/install", ctx => Preflight(ctx, cancellationToken));
        routes.Add(WatsonWebserver.Core.HttpMethod.GET, "/uracloud/status", ctx => StatusAsync(ctx, cancellationToken));
        routes.Add(WatsonWebserver.Core.HttpMethod.POST, "/uracloud/install", ctx => InstallAsync(ctx, cancellationToken));
    }

    static void ApplyCors(HttpContextBase ctx)
    {
        var origin = ctx.Request.Headers["Origin"];
        if (origin != null && AllowedOrigins.Contains(origin))
        {
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", origin);
            ctx.Response.Headers.Add("Vary", "Origin");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            ctx.Response.Headers.Add("Access-Control-Allow-Private-Network", "true");
        }
    }

    static async Task Preflight(HttpContextBase ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCors(ctx);
        ctx.Response.StatusCode = 204;
        await ctx.Response.Send(string.Empty);
    }

    static Task StatusAsync(HttpContextBase ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCors(ctx);
        var plugins = PluginManager.SnapshotPluginStatuses().Where(p => p.IsLoaded).Select(p => new
        {
            author = p.Author,
            internalName = p.InternalName,
            version = (p.Version ?? throw new InvalidOperationException(string.Format(i18n.LoadedVersionMissing, p.InternalName))).ToString(),
            loaded = true,
            source = (object?)null,
            error = (string?)null,
        }).ToArray();
        return SendJson(ctx, 200, new
        {
            app = "UmamusumeResponseAnalyzer",
            version = typeof(WebInstallApi).Assembly.GetName().Version!.ToString(),
            plugins,
        });
    }

    static async Task InstallAsync(HttpContextBase ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCors(ctx);
        var origin = ctx.Request.Headers["Origin"];
        if (origin is null || !AllowedOrigins.Contains(origin))
        {
            await SendJson(ctx, 403, new { ok = false, error = "origin_not_allowed" });
            return;
        }
        InstallRequest? request;
        try { request = JsonConvert.DeserializeObject<InstallRequest>(ctx.Request.DataAsString ?? ""); }
        catch (JsonException) { request = null; }
        if (request is null || request.RepositoryId <= 0 || request.ReleaseId <= 0)
        {
            await SendJson(ctx, 400, new { ok = false, error = "invalid_repository_or_release_id" });
            return;
        }
        try
        {
            var plugin = await PluginRepository.GetPluginAsync(request.RepositoryId, request.ReleaseId, cancellationToken);
            if (!ConfirmInstall(plugin, cancellationToken))
            {
                await SendJson(ctx, 409, new { ok = false, loaded = false, error = i18n.Cancelled });
                return;
            }
            var manifest = await PluginRepository.DownloadPluginZipAsync(plugin, cancellationToken);
            // The ZIP is installed even when the subsequent reload fails or shutdown begins.
            var loaded = false;
            var error = (string?)null;
            try
            {
                var result = (await PluginManager.ReloadPluginsAsync(manifest.InternalName)).Single();
                loaded = result.Outcome == PluginManager.PluginLifecycleOutcome.Succeeded;
                if (!loaded)
                    error = i18n.InstalledNotLoaded;
            }
            catch (Exception ex) { error = ex.Message; }
            await SendJson(ctx, 200, new { ok = true, loaded, installed = manifest.InternalName, error });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { await SendJson(ctx, 500, new { ok = false, error = ex.Message }); }
    }

    static Task SendJson(HttpContextBase ctx, int status, object payload)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.Send(JsonConvert.SerializeObject(payload, JsonSettings));
    }

    sealed record InstallRequest(long RepositoryId, long ReleaseId);
}
