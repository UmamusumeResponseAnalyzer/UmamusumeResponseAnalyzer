using System.Reflection;
using System.Runtime.ExceptionServices;
using UmamusumeResponseAnalyzer.LiveDisplay;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal static class PluginConfigPrompt
    {
        public static Task RunAsync(IPlugin plugin)
            => LiveDisplayConsole.RunAsync(() =>
            {
                PluginManager.EnterDispatch();
                try
                {
                    using var callback = PluginManager.EnterPluginCallbackScope();
                    InvokeConfigPromptAsync(plugin).GetAwaiter().GetResult();
                    return Task.CompletedTask;
                }
                finally
                {
                    PluginManager.ExitDispatch();
                }
            });

        static Task InvokeConfigPromptAsync(IPlugin plugin)
        {
            var method = plugin.GetType().GetMethod(
                nameof(IPlugin.ConfigPromptAsync),
                BindingFlags.Instance | BindingFlags.Public,
                Type.EmptyTypes);
            if (method is null || method.DeclaringType == typeof(IPlugin))
                return Task.CompletedTask;

            if (!typeof(Task).IsAssignableFrom(method.ReturnType))
                throw new InvalidOperationException(
                    $"插件 ConfigPromptAsync 签名无效: plugin={plugin.Name}, " +
                    $"method={method.DeclaringType?.FullName}.{method.Name}, expected=Task, actual={method.ReturnType.FullName}");

            try
            {
                return (Task)method.Invoke(plugin, [])!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
