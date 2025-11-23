using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public interface IPlugin
    {
        string Name { get; }
        string Author { get; }
        Version Version { get; }
        string[] Targets { get; }

        void Initialize() { }
        void Dispose() { }
        public void ConfigPrompt()
        {
            var properties = GetType().GetProperties().Where(x => x.GetCustomAttribute<PluginSettingAttribute>() != default);
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
                selection = AnsiConsole.Prompt(selectionPrompt).Split(':')[0];
                if (selection == "Reload")
                {
                    Initialize();
                }
                else if (selection == Localization.LaunchMenu.I18N_UpdateProgram)
                {
                    AnsiConsole.Progress().Start(UpdatePlugin);
                }
                else if (selection != Localization.Config.Return)
                {
                    var property = propDic[selection];
                    if (property != default)
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
                            var method = typeof(AnsiConsole).GetMethod("Prompt")!.MakeGenericMethod(type);
                            var value = method.Invoke(null, [prompt]);
                            property.SetValue(this, value);
                        }
                        else if (type == typeof(string))
                        {
                            var str = AnsiConsole.Prompt(new TextPrompt<string>($"{property.Name}: {description}").AllowEmpty());
                            property.SetValue(this, str);
                        }
                        Config.Plugin.PluginSettings[Name][property.Name] = property.GetValue(this)!;
                    }
                }
                AnsiConsole.Clear();
            } while (selection != Localization.Config.Return);
        }
        Task UpdatePlugin(ProgressContext ctx);
    }
}
