using Spectre.Console;
using System.Reflection;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public static partial class PluginSettingsManager
    {
        private static readonly ISerializer _complexSerializer = new SerializerBuilder().Build();
        private static readonly IDeserializer _deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        private static readonly ISerializer _saveSerializer = new SerializerBuilder().WithQuotingNecessaryStrings().Build();

        public static void LoadSettings(IPlugin plugin)
        {
            var settingsFilePath = plugin.SettingsFilePath;

            if (!File.Exists(settingsFilePath))
            {
                SaveSettings(plugin);
                return;
            }

            try
            {
                var yaml = File.ReadAllText(settingsFilePath);
                var yamlStream = new YamlStream();
                using (var reader = new StringReader(yaml))
                    yamlStream.Load(reader);

                if (yamlStream.Documents.Count == 0) return;
                if (yamlStream.Documents[0].RootNode is not YamlMappingNode root) return;

                var properties = plugin.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetCustomAttribute<PluginSettingAttribute>() != null);

                foreach (var property in properties)
                {
                    // 尝试多种命名规范匹配键名
                    var key = FindMatchingKey(root, property.Name);
                    if (key == null) continue;

                    try
                    {
                        var valueNode = root[key];
                        var valueYaml = valueNode.ToString();

                        // 复杂类型需要完整序列化
                        if (valueNode is YamlMappingNode or YamlSequenceNode)
                        {
                            valueYaml = _complexSerializer.Serialize(valueNode);
                        }

                        var typedValue = _deserializer.Deserialize(valueYaml, property.PropertyType);
                        if (typedValue != null)
                        {
                            property.SetValue(plugin, typedValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[yellow]加载设置 '{property.Name.EscapeMarkup()}' 失败: {ex.Message.EscapeMarkup()}[/]");
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]加载插件设置失败: {ex.Message.EscapeMarkup()}[/]");
                SaveSettings(plugin);
            }
        }

        public static void SaveSettings(IPlugin plugin)
        {
            var settingsFilePath = plugin.SettingsFilePath;

            var settings = new Dictionary<string, object?>();
            var properties = plugin.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<PluginSettingAttribute>() != null);

            foreach (var property in properties)
            {
                // 使用属性名本身作为键（保留原始命名）
                settings[property.Name] = property.GetValue(plugin);
            }

            var directory = Path.GetDirectoryName(settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(settingsFilePath, _saveSerializer.Serialize(settings));
        }

        /// <summary>
        /// 尝试多种命名规范匹配键名
        /// </summary>
        private static YamlScalarNode? FindMatchingKey(YamlMappingNode root, string propertyName)
        {
            var candidates = GenerateKeyVariations(propertyName);

            foreach (var candidate in candidates)
            {
                var key = new YamlScalarNode(candidate);
                if (root.Children.ContainsKey(key))
                    return key;
            }

            // 最后尝试不区分大小写匹配
            foreach (var child in root.Children)
            {
                if (child.Key is YamlScalarNode scalarKey &&
                    string.Equals(scalarKey.Value, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return scalarKey;
                }
            }

            return null;
        }

        /// <summary>
        /// 生成可能的键名变体
        /// </summary>
        private static IEnumerable<string> GenerateKeyVariations(string name)
        {
            // 原始属性名
            yield return name;

            // snake_case
            yield return ToSnakeCase(name);

            // camelCase
            yield return ToCamelCase(name);

            // PascalCase
            yield return ToPascalCase(name);

            // kebab-case
            yield return ToKebabCase(name);

            // 全小写
            yield return name.ToLowerInvariant();

            // 全大写
            yield return name.ToUpperInvariant();
        }

        private static string ToSnakeCase(string name) =>
            WordBoundaryRegex().Replace(name, "_$1").ToLowerInvariant();

        private static string ToCamelCase(string name) =>
            string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

        private static string ToPascalCase(string name) =>
            string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];

        private static string ToKebabCase(string name) =>
            WordBoundaryRegex().Replace(name, "-$1").ToLowerInvariant();

        [GeneratedRegex(@"(?<!^)([A-Z])")]
        private static partial Regex WordBoundaryRegex();
    }
}
