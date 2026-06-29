using Spectre.Console;
using System.Reflection;
using UmamusumeResponseAnalyzer.LiveDisplay;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public interface IPlugin
    {
        string Name { get; }
        string Author { get; }
        Version Version { get => GetType().Assembly.GetName().Version ?? new Version(0, 0, 0); }
        string[] Targets { get; }
        string DataDirectory => Path.Combine("PluginData", Name);
        string SettingsFilePath => Path.Combine(DataDirectory, "settings.yaml");

        void Initialize()
        {
            Directory.CreateDirectory(DataDirectory);
        }
        void Initialize(ILiveDisplayOutput liveDisplay)
        {
            Initialize();
        }
        void Dispose() { }
        Task ConfigPromptAsync()
        {
            ConfigPrompt();
            return Task.CompletedTask;
        }
        void ConfigPrompt()
        {
            RunDefaultConfigPromptAsync().GetAwaiter().GetResult();
        }
        private Task RunDefaultConfigPromptAsync()
        {
            return LiveDisplayConsole.RunAsync(ConfigPromptCore);
        }
        private async Task ConfigPromptCore()
        {
            var properties = GetType()
                .GetProperties()
                .Where(x => x.GetCustomAttribute<PluginSettingAttribute>() != null)
                .ToArray();
            var propDic = new Dictionary<string, PropertyInfo>();

            var selection = string.Empty;
            do
            {
                var choices = new List<string>
                {
                    $"插件: {Name}",
                    $"版本: {Version}",
                    $"作者: {Author}"
                };
                foreach (var i in properties)
                {
                    var key = i.GetCustomAttribute<PluginDescriptionAttribute>()?.Description ?? i.Name;
                    propDic.TryAdd(key, i);
                    choices.Add($"{key}: {i.GetValue(this)}");
                }
                choices.Add("Reload");
                choices.Add(Localization.LaunchMenu.I18N_UpdateProgram);
                choices.Add(Localization.Config.Return);
                var selectionPrompt = new SelectionPrompt<string>()
                    .Title(GetType().GetProperty("Name")?.GetCustomAttribute<PluginDescriptionAttribute>()?.Description ?? Name)
                    .WrapAround(true)
                    .AddChoices(choices);
                selection = LiveDisplayConsole.Prompt(selectionPrompt).Split(':')[0];
                if (selection == "Reload")
                {
                    PluginSettingsManager.LoadSettings(this);
                }
                else if (selection == Localization.LaunchMenu.I18N_UpdateProgram)
                {
                    await LiveDisplayConsole.RunProgressAsync(p => p.StartAsync(UpdatePlugin));
                }
                else if (selection != Localization.Config.Return && propDic.TryGetValue(selection, out var property))
                {
                    var description = property.GetCustomAttribute<PluginDescriptionAttribute>()?.Description;
                    var type = property.PropertyType;
                    if (type == typeof(bool))
                    {
                        var value = (bool)(property.GetValue(this) ?? false);
                        property.SetValue(this, !value);
                    }
                    else if (type.IsPrimitive || type == typeof(decimal))
                    {
                        var promptType = typeof(TextPrompt<>).MakeGenericType(type);
                        var prompt = Activator.CreateInstance(promptType, $"{property.Name}: {description}", null);
                        var method = typeof(LiveDisplayConsole).GetMethod(nameof(LiveDisplayConsole.Prompt))!.MakeGenericMethod(type);
                        var value = method.Invoke(null, [prompt]);
                        property.SetValue(this, value);
                    }
                    else if (type == typeof(string))
                    {
                        var str = LiveDisplayConsole.Prompt(new TextPrompt<string>($"{property.Name}: {description}").AllowEmpty());
                        property.SetValue(this, str);
                    }
                    PluginSettingsManager.SaveSettings(this);
                }
                LiveDisplayConsole.Clear();
            } while (selection != Localization.Config.Return);
        }
        Task UpdatePlugin(ProgressContext ctx);
    }

    public static class UraEvents
    {
        /// <summary>
        /// 在内置HTTP服务器启动完成后触发
        /// </summary>
        public static event Func<Task>? OnStarted;

        internal static async Task TriggerStartedAsync()
        {
            if (OnStarted != null)
            {
                foreach (var handler in OnStarted.GetInvocationList().Cast<Func<Task>>())
                {
                    try
                    {
                        await handler();
                    }
                    catch (Exception ex)
                    {
                        LiveDisplayConsole.Notify("Plugin", $"插件事件处理错误: {ex.Message}", LiveDisplaySeverity.Error);
                        LiveDisplayConsole.Log("Plugin", ex.ToString(), LiveDisplaySeverity.Error);
                    }
                }
            }
        }
    }
}
