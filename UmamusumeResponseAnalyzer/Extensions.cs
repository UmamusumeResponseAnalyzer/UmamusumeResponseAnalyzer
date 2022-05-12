using System;
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
                bool foundMatch = true;
                for (int j = 0; j < pattern.Length; j++)
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

            return result.ToArray();
        }
        public static SystemVersion GetSystemVersion(this OperatingSystem _)
        {
            if (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1)
                return SystemVersion.Windows7;
            return SystemVersion.Default;
        }
        public static byte[] GetContentMD5(this HttpContentHeaders headers)
        {
            headers.TryGetValues("content-md5", out var values);
            return Convert.FromBase64String(values!.First());
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
    }
}
