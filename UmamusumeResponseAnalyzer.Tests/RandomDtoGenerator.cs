using System.Reflection;

namespace UmamusumeResponseAnalyzer.PluginTesting;

internal enum RandomDtoProfile { Protocol, Analyzer }

internal sealed class RandomDtoGenerator(int seed, RandomDtoProfile profile)
{
    readonly int maxDepth = profile == RandomDtoProfile.Protocol ? 4 : 8;
    readonly Random random = new(seed);

    public static object Create(Type type, string salt, RandomDtoProfile profile)
        => new RandomDtoGenerator(StableSeed(type.FullName + ":" + salt), profile).CreateValue(type, 0)
            ?? throw new InvalidOperationException($"Cannot create DTO root value: {type.FullName}");

    static int StableSeed(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in text)
                hash = (hash * 31) + c;
            return hash;
        }
    }

    object? CreateValue(Type type, int depth)
    {
        if (type == typeof(string))
            return $"dto-{depth}-{random.Next(1, 1_000_000)}";
        if (type == typeof(bool))
            return random.Next(0, 2) == 0;
        if (type == typeof(byte))
            return (byte)random.Next(byte.MinValue, byte.MaxValue + 1);
        if (type == typeof(sbyte))
            return (sbyte)random.Next(sbyte.MinValue, sbyte.MaxValue + 1);
        if (type == typeof(short))
            return (short)random.Next(short.MinValue, short.MaxValue);
        if (type == typeof(ushort))
            return (ushort)random.Next(ushort.MinValue, ushort.MaxValue);
        if (type == typeof(int))
            return random.Next(profile == RandomDtoProfile.Protocol ? -1_000_000 : 0, profile == RandomDtoProfile.Protocol ? 1_000_000 : 100);
        if (type == typeof(uint))
            return (uint)random.Next(0, profile == RandomDtoProfile.Protocol ? 1_000_000 : 100);
        if (type == typeof(long))
            return random.NextInt64(profile == RandomDtoProfile.Protocol ? -1_000_000_000 : 0, profile == RandomDtoProfile.Protocol ? 1_000_000_000 : 100);
        if (type == typeof(ulong))
            return (ulong)random.NextInt64(0, profile == RandomDtoProfile.Protocol ? 1_000_000_000 : 100);
        if (type == typeof(float))
            return (float)(random.NextDouble() * (profile == RandomDtoProfile.Protocol ? 10_000 : 100));
        if (type == typeof(double))
            return random.NextDouble() * (profile == RandomDtoProfile.Protocol ? 10_000 : 100);
        if (type == typeof(decimal))
            return (decimal)(random.NextDouble() * (profile == RandomDtoProfile.Protocol ? 10_000 : 100));
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length == 0 ? Activator.CreateInstance(type) : values.GetValue(random.Next(values.Length));
        }
        if (Nullable.GetUnderlyingType(type) is { } nullableType)
            return CreateValue(nullableType, depth);
        if (type == typeof(byte[]))
        {
            var bytes = new byte[4];
            random.NextBytes(bytes);
            return bytes;
        }
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var length = depth >= maxDepth ? 0 : profile == RandomDtoProfile.Protocol ? 2 : 1;
            var array = Array.CreateInstance(elementType, length);
            for (var i = 0; i < length; i++)
                array.SetValue(CreateValue(elementType, depth + 1), i);
            return array;
        }
        if (type.IsAbstract || type.IsInterface)
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        if (depth >= maxDepth && !type.IsValueType)
            return null;

        var instance = Activator.CreateInstance(type);
        if (instance is null)
            return null;

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            field.SetValue(instance, CreateValue(field.FieldType, depth + 1));
        return instance;
    }
}
