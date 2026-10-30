using System.Net.WebSockets;
using System.Reflection;
using UmamusumeResponseAnalyzer.TerminalGui;
using WatsonWebserver.Core;
using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed record PluginRouteRegistration(
    IPlugin Plugin,
    HttpMethod Method,
    string Path,
    Func<HttpContextBase, Task>? HttpHandler,
    Func<HttpContextBase, WebSocket, CancellationToken, Task>? WebSocketHandler)
{
    internal async Task InvokeAsync(HttpContextBase context)
    {
        using var callback = PluginManager.TryEnterPluginCallback(Plugin);
        if (callback is null)
        {
            context.Response.StatusCode = 503;
            await context.Response.Send(string.Empty).ConfigureAwait(false);
            return;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            PluginManager.RouteLifetime(Plugin), Server.LifetimeToken, context.Token);
        using var cancelRequest = lifetime.Token.Register(context.TokenSource.Cancel);
        using var owner = HotkeyManager.RegisterScope(Plugin);
        try
        {
            if (WebSocketHandler is { } websocket)
                await WebSocketUpgrade.RunAsync(context, (socket, token) => websocket(context, socket, token), lifetime.Token)
                    .ConfigureAwait(false);
            else
                await HttpHandler!(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (WebSocketException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!PluginManager.TryDisableForMissingAssembly(Plugin, error, "Route"))
            {
                var failure = new InvalidOperationException(string.Format(i18n.RouteFailed,
                    PluginManager.InternalName(Plugin), Path, PluginManager.DescribeException(error)), error);
                PluginManager.ReportPluginFailure("Plugin", failure, error.ToString());
            }
            // Watson sends HTTP 500 for escaped exceptions, which would corrupt an upgraded WebSocket stream.
            if (!context.Response.ResponseSent)
            {
                context.Response.StatusCode = 500;
                await context.Response.Send(string.Empty).ConfigureAwait(false);
            }
        }
    }
}

internal static partial class PluginManager
{
    internal static CancellationToken RouteLifetime(IPlugin plugin) => RequireLifecycle(plugin).Stopping;

    static List<PluginRouteRegistration> CreateRouteRegistrations(IPlugin plugin)
    {
        var routes = new List<PluginRouteRegistration>();
        foreach (var method in plugin.GetType().GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                     BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var http = method.GetCustomAttribute<RouteAttribute>();
            var websocket = method.GetCustomAttribute<WebSocketRouteAttribute>();
            if (http is null && websocket is null) continue;
            var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            var expected = websocket is null
                ? new[] { typeof(HttpContextBase) }
                : [typeof(HttpContextBase), typeof(WebSocket), typeof(CancellationToken)];
            if (http is not null && websocket is not null || method.ContainsGenericParameters ||
                method.ReturnType != typeof(Task) || !parameters.SequenceEqual(expected))
                throw new InvalidOperationException(string.Format(i18n.RouteSignatureInvalid,
                    InternalName(plugin), method.Name,
                    websocket is null ? "Task(HttpContextBase)" : "Task(HttpContextBase, WebSocket, CancellationToken)"));

            var relativePath = http?.Path ?? websocket!.Path;
            if (string.IsNullOrEmpty(relativePath) || relativePath.Split('/').Any(segment =>
                    segment is "" or "." or ".." || segment.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not ('-' or '_' or '.'))))
                throw new InvalidOperationException(string.Format(i18n.RoutePathInvalid, InternalName(plugin), relativePath));
            var verb = http?.Method ?? HttpMethod.GET;
            if (!Enum.IsDefined(verb) || verb == HttpMethod.UNKNOWN)
                throw new InvalidOperationException(string.Format(i18n.RoutePathInvalid, InternalName(plugin), relativePath));
            var path = $"/{InternalName(plugin)}/{relativePath}";
            if (routes.Any(route => route.Method == verb && route.Path == path))
                throw new InvalidOperationException(string.Format(i18n.RouteDuplicate, verb, path));
            routes.Add(new(plugin, verb, path,
                websocket is not null ? null : method.IsStatic
                    ? method.CreateDelegate<Func<HttpContextBase, Task>>()
                    : method.CreateDelegate<Func<HttpContextBase, Task>>(plugin),
                websocket is null ? null : method.IsStatic
                    ? method.CreateDelegate<Func<HttpContextBase, WebSocket, CancellationToken, Task>>()
                    : method.CreateDelegate<Func<HttpContextBase, WebSocket, CancellationToken, Task>>(plugin)));
        }
        return routes;
    }

    static void CommitPluginRegistrations(PluginRegistrationPlan plan)
    {
        var added = new List<PluginRouteRegistration>();
        try
        {
            foreach (var route in plan.Routes)
            {
                var registry = Server.Instance.Routes.PreAuthentication.Static;
                if (registry.Exists(route.Method, route.Path))
                    throw new InvalidOperationException(string.Format(i18n.RouteDuplicate, route.Method, route.Path));
                registry.Add(route.Method, route.Path, route.InvokeAsync);
                added.Add(route);
            }
            CommitAnalyzerRegistrations(plan.Analyzers);
            if (added.Count != 0) Runtime.Routes.Add(added[0].Plugin, added);
        }
        catch
        {
            foreach (var route in added)
                Server.Instance.Routes.PreAuthentication.Static.Remove(route.Method, route.Path);
            throw;
        }
    }

    static void RemoveRoutes(IPlugin plugin)
    {
        if (!Runtime.Routes.Remove(plugin, out var routes)) return;
        foreach (var route in routes)
            Server.Instance.Routes.PreAuthentication.Static.Remove(route.Method, route.Path);
    }
}
