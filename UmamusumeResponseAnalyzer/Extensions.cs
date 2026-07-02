using Gallop;
using System.Reflection;

namespace UmamusumeResponseAnalyzer
{
    public enum ScenarioType
    {
        Ura = 1,
        Aoharu = 2,
        GrandLive = 3,
        MakeANewTrack = 4, //巅峰杯
        GrandMasters = 5,
        LArc = 6,
        UAF = 7,
        Cook = 8,
        Mecha = 9,
        Legend = 10,
        Pioneer = 11,
        Onsen = 12,
        Breeders = 13,
        Unknown = int.MaxValue
    }
    public static class Extensions
    {
        public static byte[] Replace(this byte[] input, byte[] pattern, byte[] replacement)
        {
            if (pattern.Length == 0)
            {
                return input;
            }

            var result = new List<byte>();

            int i;

            for (i = 0; i <= input.Length - pattern.Length; i++)
            {
                var foundMatch = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (input[i + j] != pattern[j])
                    {
                        foundMatch = false;
                        break;
                    }
                }

                if (foundMatch)
                {
                    result.AddRange(replacement);
                    i += pattern.Length - 1;
                }
                else
                {
                    result.Add(input[i]);
                }
            }

            for (; i < input.Length; i++)
            {
                result.Add(input[i]);
            }

            return [.. result];
        }
        public static string AllowMirror(this string? url)
        {
            if (url == null) return string.Empty;
            if (Config.Updater.IsGithubBlocked && !Config.Updater.ForceUseGithubToUpdate)
            {
                url = url.Replace("https://", "https://gh.shuise.dev/");
            }
            return url;
        }
        public static string AppendValue(this PropertyInfo property, object? obj, Dictionary<string, string> translatedDic = null!)
        {
            var value = property.GetValue(obj);
            var valueString = string.Empty;
            if (value is IEnumerable<string> enumerable)
            {
                valueString = string.Join(",", enumerable.Select(x => x.Replace("[", "[[").Replace("]", "]]")));
            }
            else
            {
                valueString = value?.ToString()?.Replace("[", "[[").Replace("]", "]]") ?? string.Empty;
            }
            if (translatedDic == null) return $"{property.Name}: {valueString}";

            translatedDic.TryGetValue(property.Name, out var translated);
            return $"{translated ?? property.Name}: {valueString}";
        }
    }
    public static class GallopExtensions
    {
        public static int GetCommandInfoStage(this SingleModeCheckEventResponse @event)
            => @event.data.GetCommandInfoStage();

        public static int GetCommandInfoStage(this SingleModeCheckEventResponse.CommonResponse data)
        {
            // unchecked_event_array 可能为 null（playing_state==1 时已知会缺），统一兜底为空数组，
            // 让所有分支都以「null 视为无事件」的一致语义处理，避免 ==5 分支裸调 .Any() 抛 NRE。
            var events = data.unchecked_event_array ?? [];
            if (data.chara_info.playing_state == 1 && events.Length == 0)
            {
                return 2;
            } //常规训练
            else if (data.chara_info.playing_state == 5 && events.Any(x => x.story_id == 400010112)) //选buff
            {
                return 5;
            }
            else if (data.chara_info.playing_state == 5 &&
                events.Any(x => x.story_id == 830241003)) //选团卡事件
            {
                return 3;
            }
            else
            {
                return 0;
            }
        }
    }
}
