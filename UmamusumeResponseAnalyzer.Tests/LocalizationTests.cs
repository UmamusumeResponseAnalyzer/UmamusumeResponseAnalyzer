using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text;
using UmamusumeResponseAnalyzer.Entities;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationCollection { }

[Collection("Localization")]
public sealed class LocalizationTests
{
    [Fact]
    public void ResourceSetsAndGeneratedPropertiesAgreeInEverySupportedLanguage()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = typeof(Config).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".resources", StringComparison.Ordinal))
            .Select(name => name[..^".resources".Length])
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(13, resourceNames.Length);

        foreach (var resourceName in resourceNames)
        {
            var designer = assembly.GetType(resourceName);
            // A fresh manager prevents an earlier fallback lookup from masking a missing satellite set.
            var manager = new ResourceManager(resourceName, assembly);
            var cultureProperty = designer?.GetProperty("Culture", flags);
            var originalCulture = cultureProperty?.GetValue(null);
            try
            {
                var neutralSet = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
                Assert.NotNull(neutralSet);
                var neutral = neutralSet.Cast<DictionaryEntry>()
                    .ToDictionary(entry => (string)entry.Key, entry => Assert.IsType<string>(entry.Value));
                var properties = designer?.GetProperties(flags)
                    .Where(property => property.PropertyType == typeof(string)).ToDictionary(property => property.Name);
                if (properties is not null)
                    Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), properties.Keys.Order(StringComparer.Ordinal));

                foreach (var language in new[] { "en-US", "zh-CN", "ja-JP" })
                {
                    var culture = CultureInfo.GetCultureInfo(language);
                    var resourceSet = manager.GetResourceSet(culture, true, false);
                    Assert.NotNull(resourceSet);
                    var values = resourceSet.Cast<DictionaryEntry>()
                        .ToDictionary(entry => (string)entry.Key, entry => Assert.IsType<string>(entry.Value));
                    Assert.True(neutral.Keys.Order(StringComparer.Ordinal).SequenceEqual(values.Keys.Order(StringComparer.Ordinal)),
                        $"{resourceName}/{language}: resource keys differ from the neutral set.");
                    cultureProperty?.SetValue(null, culture);
                    foreach (var key in neutral.Keys)
                    {
                        var label = $"{resourceName}/{language}/{key}";
                        Assert.False(string.IsNullOrWhiteSpace(neutral[key]), $"{label}: neutral value is empty.");
                        Assert.False(string.IsNullOrWhiteSpace(values[key]), $"{label}: translated value is empty.");
                        Assert.True(PlaceholderIndexes(neutral[key]).SequenceEqual(PlaceholderIndexes(values[key])),
                            $"{label}: composite format placeholder indexes differ.");
                        if (language == "en-US")
                            Assert.True(neutral[key] == values[key], $"{label}: neutral and en-US values differ.");
                        if (properties is not null)
                            Assert.Equal(values[key], properties[key].GetValue(null));
                    }
                }
            }
            finally
            {
                cultureProperty?.SetValue(null, originalCulture);
                manager.ReleaseAllResources();
            }
        }
    }

    [Fact]
    public void ExistingDomainInstancesAndItemCacheFollowLanguageChanges()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalItems = Database.ClimaxItem;
        var resourceCultures = typeof(Config).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("UmamusumeResponseAnalyzer.Localization", StringComparison.Ordinal) == true)
            .Select(type => type.GetField("resourceCulture", BindingFlags.Static | BindingFlags.NonPublic))
            .OfType<FieldInfo>()
            .Select(field => (Field: field, Value: field.GetValue(null)))
            .ToArray();
        var motivation = Motivation.Best;
        var card = new SupportCardName(1, "Original name", "Original nickname", 101, 1);
        try
        {
            foreach (var (language, mood, cardType, item, unknown) in new[]
            {
                ("zh-CN", "绝好调", "[速]", "速+3", "未知"),
                ("en-US", "Great", "[Spd]", "Spd+3", "Unknown"),
                ("ja-JP", "絶好調", "[速]", "速+3", "不明"),
            })
            {
                global::UmamusumeResponseAnalyzer.UmamusumeResponseAnalyzer.ApplyCultureInfo(CultureInfo.GetCultureInfo(language));
                Assert.Equal(mood, motivation.ToString());
                Assert.Equal(cardType, card.TypeName);
                Assert.Equal("Original name", card.Name);
                Assert.Equal(item, Database.ClimaxItem[1001]);
                Assert.Equal(unknown, Database.ClimaxItem[-1]);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            foreach (var (field, value) in resourceCultures)
                field.SetValue(null, value);
            typeof(Database).GetProperty(nameof(Database.ClimaxItem))!.SetValue(null, originalItems);
        }
    }

    [Fact]
    public void PublishedGameResourcePropertiesRemainPublic()
    {
        var publishedProperties = new[]
        {
            "I18N_Blue", "I18N_Dirt", "I18N_Grass", "I18N_Long", "I18N_Middle", "I18N_Mile", "I18N_Month",
            "I18N_MotivationBad", "I18N_MotivationBest", "I18N_MotivationGood", "I18N_MotivationNormal", "I18N_MotivationWorst",
            "I18N_Nige", "I18N_Nuts", "I18N_NutsSimple", "I18N_Oikomi", "I18N_Power", "I18N_PowerSimple", "I18N_Proper",
            "I18N_Red", "I18N_Sashi", "I18N_Senko", "I18N_Short", "I18N_Speed", "I18N_SpeedSimple",
            "I18N_Stamina", "I18N_StaminaSimple", "I18N_Stat", "I18N_StatSimple", "I18N_Vital", "I18N_VitalSimple",
            "I18N_Wiz", "I18N_WizSimple", "I18N_Year", "I18N_Yellow",
        };
        Assert.True(typeof(Localization.Game).IsPublic);
        foreach (var name in publishedProperties)
        {
            var property = typeof(Localization.Game).GetProperty(name, BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(property);
            Assert.Equal(typeof(string), property.PropertyType);
            Assert.True(property.GetMethod!.IsPublic);
        }
    }

    static int[] PlaceholderIndexes(string value)
    {
        var format = CompositeFormat.Parse(value);
        var collector = new PlaceholderCollector();
        _ = string.Format(collector, format, Enumerable.Range(0, format.MinimumArgumentCount).Cast<object>().ToArray());
        return [.. collector.Indexes.Order()];
    }

    sealed class PlaceholderCollector : IFormatProvider, ICustomFormatter
    {
        internal HashSet<int> Indexes { get; } = [];
        public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
        public string Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            Indexes.Add((int)arg!);
            return string.Empty;
        }
    }
}
