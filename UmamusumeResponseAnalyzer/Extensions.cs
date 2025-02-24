using Gallop;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer
{
    public enum SystemVersion
    {
        Default = 0,
        Windows7 = 1
    }
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
        public static SystemVersion GetSystemVersion(this OperatingSystem _)
        {
            return Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1
                ? SystemVersion.Windows7
                : SystemVersion.Default;
        }
        public static bool Contains<T>(this IEnumerable<T> list, Predicate<T> predicate)
        {
            if (list == default || !list.Any()) return false;
            foreach (var i in list)
            {
                if (EqualityComparer<T>.Default.Equals(i, default))
                    continue;
                if (predicate(i))
                    return true;
            }
            return false;
        }
        public static bool IsScenario(this SingleModeCheckEventResponse @event, ScenarioType type)
        {
            var data = @event.data;
            return type switch
            {
                ScenarioType.Ura => data.chara_info.scenario_id == 1,
                ScenarioType.Aoharu => data.chara_info.scenario_id == 2,
                ScenarioType.GrandLive => data.chara_info.scenario_id == 3,
                ScenarioType.MakeANewTrack => data.chara_info.scenario_id == 4 && data.free_data_set.pick_up_item_info_array != null,
                ScenarioType.GrandMasters => data.chara_info.scenario_id == 5 && data.venus_data_set != null,
                ScenarioType.LArc => data.chara_info.scenario_id == 6 && data.arc_data_set != null,
                ScenarioType.UAF => data.chara_info.scenario_id == 7 && data.sport_data_set != null,
                ScenarioType.Cook => data.chara_info.scenario_id == 8 && data.cook_data_set != null,
                ScenarioType.Mecha => data.chara_info.scenario_id == 9 && data.mecha_data_set != null,
                ScenarioType.Legend => data.chara_info.scenario_id == 10 && data.legend_data_set != null,
                ScenarioType.Unknown => true,
                _ => false
            };
        }
    }
    public static class TableExtension
    {
        static readonly Dictionary<Table, Dictionary<(int, int), string>> saved = [];
        private static void Prepare(this Table table)
        {
            if (!saved.ContainsKey(table))
            {
                saved.Add(table, []);
            }
        }
        public static bool Edit(this Table? table, int column, int row, string content)
        {
            if (table is null) return false;
            table.Prepare();
            if (saved[table].ContainsKey((column, row)))
                saved[table][(column, row)] = content;
            else
                saved[table].Add((column, row), content);
            return true;
        }
        public static bool AddToRows(this Table? table, int rowIndex, params string[] contents)
        {
            if (table is null) return false;
            table.Prepare();
            for (var index = 0; index < contents.Length; index++)
            {
                saved[table].Add((index, rowIndex), contents[index]);
            }
            return true;
        }
        public static bool Finish(this Table? table)
        {
            if (table is null) return false;
            foreach (var i in saved[table].GroupBy(x => x.Key.Item2))
            {
                table.AddRow(i.Select(x => x.Value).ToArray());
            }
            saved.Clear();
            return true;
        }
    }
    public static class ManualResetEventExtensions
    {
        static ConcurrentDictionary<ManualResetEvent, ConcurrentDictionary<Action, bool>> _dictionaries { get; set; } = new();
        public static void Signal(this ManualResetEvent mre)
        {
            if (_dictionaries.TryGetValue(mre, out var waitings))
            {
                if (!waitings.IsEmpty)
                    mre.Set();
            }
        }
        public static void Wait(this ManualResetEvent mre, Action action)
        {
            _dictionaries.TryAdd(mre, new());
            _dictionaries[mre][action] = false;
            mre.WaitOne();
            _ = Task.Run(action);
            _dictionaries[mre][action] = true;
            if (_dictionaries[mre].Values.All(x => x))
            {
                mre.Reset();
            }
        }
    }
}
