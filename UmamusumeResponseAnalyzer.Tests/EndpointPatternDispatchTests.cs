using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

public sealed class EndpointPatternDispatchTests
{
    const string AccountIndexPath = "/umamusume/account/index";

    [Fact]
    public void AnalyzerRegistry_UsesOneGenericRegisterForDtoAndRawPayloads()
    {
        var contract = Assert.Single(typeof(IPluginAnalyzerRegistry).GetMethods());
        Assert.Equal("Register", contract.Name);
        Assert.True(contract.IsGenericMethodDefinition);
        Assert.Equal(typeof(void), contract.ReturnType);

        var plugin = new TestPlugin();
        using var stage = PluginManager.BeginRegistrationStage(plugin);
        var registry = PluginManager.AnalyzersFor(plugin);

        registry.Register<DataLinkIndexResponse>(
            AnalyzerKind.Response,
            [EndpointPattern.Wildcard("/umamusume/single_mode*/check_event")],
            _ => ValueTask.CompletedTask);
        registry.Register<ReadOnlyMemory<byte>>(
            AnalyzerKind.Response,
            [EndpointPattern.Regex("^/umamusume/account/index$")],
            _ => ValueTask.CompletedTask);

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register<byte[]>(
                AnalyzerKind.Response,
                [EndpointPattern.Exact(AccountIndexPath)],
                _ => ValueTask.CompletedTask));
    }

    [Fact]
    public void ExpandEndpointPatterns_ExpandsAtRegistrationTimeAndDeduplicatesTheUnion()
    {
        var endpoints = PluginManager.ExpandEndpointPatterns(
        [
            EndpointPattern.Wildcard("/umamusume/single_mode*/check_event"),
            EndpointPattern.Regex("^/umamusume/single_mode(?:_[^/]+)?/check_event$"),
            EndpointPattern.Exact("/umamusume/single_mode/check_event"),
            EndpointPattern.Exact("/umamusume/single_mode/check_event"),
        ]);

        Assert.NotEmpty(endpoints);
        Assert.Equal(endpoints.Count, endpoints.Select(endpoint => endpoint.EndpointType).Distinct().Count());
        Assert.Single(endpoints, endpoint => endpoint.EndpointType == typeof(GameApi.SingleMode.CheckEvent));
        Assert.All(endpoints, endpoint => Assert.Matches("^/umamusume/single_mode[^/]*/check_event$", endpoint.Path));
        Assert.Contains(endpoints, endpoint => endpoint.Path == "/umamusume/single_mode/check_event");
    }

    [Fact]
    public void ExpandEndpointPatterns_UsesOrdinalCaseSensitiveCanonicalPaths()
    {
        var exact = Assert.Single(PluginManager.ExpandEndpointPatterns([EndpointPattern.Exact(AccountIndexPath)]));

        Assert.Equal(typeof(GameApi.Account.Index), exact.EndpointType);
        Assert.Throws<InvalidOperationException>(() =>
            PluginManager.ExpandEndpointPatterns([EndpointPattern.Exact("/umamusume/ACCOUNT/index")]));
    }

    [Fact]
    public void ExpandEndpointPatterns_WildcardNeverCrossesAPathSeparator()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            PluginManager.ExpandEndpointPatterns([EndpointPattern.Wildcard("/umamusume/*")]));

        Assert.Equal(string.Format(i18n.EndpointPatternUnmatched, EndpointPatternKind.Wildcard, "/umamusume/*"), error.Message);
    }

    [Fact]
    public void ExpandEndpointPatterns_RegexAlwaysMatchesTheWholeCanonicalPath()
    {
        var exact = Assert.Single(PluginManager.ExpandEndpointPatterns(
            [EndpointPattern.Regex("/umamusume/account/index")]));

        Assert.Equal(AccountIndexPath, exact.Path);
        Assert.Throws<InvalidOperationException>(() =>
            PluginManager.ExpandEndpointPatterns([EndpointPattern.Regex("/umamusume/account")]));
    }

    [Theory]
    [InlineData("account/index")]
    [InlineData("/umamusume/account/index/")]
    [InlineData("/umamusume//account/index")]
    [InlineData("/umamusume/account/../index")]
    [InlineData("/umamusume/account/index?viewer_id=1")]
    [InlineData("/umamusume/account/index#fragment")]
    public void ExpandEndpointPatterns_RejectsNonCanonicalExactPaths(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            PluginManager.ExpandEndpointPatterns([EndpointPattern.Exact(path)]));
    }

    [Fact]
    public void ExpandEndpointPatterns_RejectsWildcardWithoutAStar()
    {
        Assert.Throws<ArgumentException>(() =>
            PluginManager.ExpandEndpointPatterns([EndpointPattern.Wildcard(AccountIndexPath)]));
    }

    [Fact]
    public void ExpandEndpointPatterns_RejectsAdjacentDoubleStarWildcard()
    {
        Assert.Throws<ArgumentException>(() =>
            PluginManager.ExpandEndpointPatterns(
                [EndpointPattern.Wildcard("/umamusume/single_mode**/check_event")]));
    }

    [Fact]
    public void ExpandEndpointPatterns_RejectsRegexUnsupportedByNonBacktrackingMode()
    {
        Assert.Throws<NotSupportedException>(() =>
            PluginManager.ExpandEndpointPatterns(
                [EndpointPattern.Regex(@"^(/umamusume/account)/\1$")]));
    }

    [Fact]
    public void AnalyzerDispatchContext_CachesSuccessfulProjectionByPayloadType()
    {
        var descriptor = GameEndpointCatalog.ByEndpointType[typeof(GameApi.Account.Index)];
        var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse());
        var context = new AnalyzerDispatchContext(descriptor, payload, EmptyHeaders());

        var first = context.GetDto(typeof(DataLinkIndexResponse));
        var second = context.GetDto(typeof(DataLinkIndexResponse));

        Assert.Same(first, second);
    }

    [Fact]
    public void AnalyzerDispatchContext_CachesProjectionFailureAndLeavesRawPayloadAvailable()
    {
        var descriptor = GameEndpointCatalog.ByEndpointType[typeof(GameApi.Account.Index)];
        byte[] payload = [0xC1];
        var headers = EmptyHeaders();
        var context = new AnalyzerDispatchContext(descriptor, payload, headers);

        var first = Assert.Throws<AnalyzerProjectionException>(() =>
            context.GetDto(typeof(DataLinkIndexResponse)));
        var second = Assert.Throws<AnalyzerProjectionException>(() =>
            context.GetDto(typeof(DataLinkIndexResponse)));

        Assert.Same(first, second);
        Assert.True(first.TryMarkReported());
        Assert.False(second.TryMarkReported());
        Assert.Equal(payload, context.Payload.ToArray());
        Assert.Same(headers, context.Headers);
    }

    [Fact]
    public void AnalyzerDispatchContext_CachesProjectionFailuresIndependentlyByPayloadType()
    {
        var descriptor = GameEndpointCatalog.ByEndpointType[typeof(GameApi.Account.Index)];
        var context = new AnalyzerDispatchContext(descriptor, [0xC1], EmptyHeaders());

        var accountFailure = Assert.Throws<AnalyzerProjectionException>(() =>
            context.GetDto(typeof(DataLinkIndexResponse)));
        var bannerFailure = Assert.Throws<AnalyzerProjectionException>(() =>
            context.GetDto(typeof(BannerUrlResponse)));

        Assert.NotSame(accountFailure, bannerFailure);
    }

    static GameHttpHeaders EmptyHeaders()
        => new(null, null, null, null, null, null);

    sealed class TestPlugin : IPlugin
    {
        public void Initialize(IPluginContext context) { }
    }
}
