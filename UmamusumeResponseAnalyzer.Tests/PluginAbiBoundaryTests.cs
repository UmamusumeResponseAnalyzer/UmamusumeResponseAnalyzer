using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    public sealed class PluginAbiBoundaryTests
    {
        [Fact]
        public void PluginContractsAndGallopModelsComeFromExternalAssemblies()
        {
            var hostAssembly = typeof(Server).Assembly;

            Assert.Equal("UmamusumeResponseAnalyzer.Plugin.Abstractions", typeof(IPlugin).Assembly.GetName().Name);
            Assert.Equal("UmamusumeResponseAnalyzer.Plugin.Abstractions", typeof(AnalyzerAttribute).Assembly.GetName().Name);
            Assert.Equal("UmamusumeResponseAnalyzer.Plugin.Abstractions", typeof(ILiveDisplayOutput).Assembly.GetName().Name);
            Assert.Equal("UmamusumeResponseAnalyzer.Plugin.Abstractions", typeof(LiveDisplayWorkspace).Assembly.GetName().Name);
            Assert.Equal("Gallop", typeof(IGameEndpoint).Assembly.GetName().Name);
            Assert.Equal("Gallop", typeof(DataLinkIndexResponse).Assembly.GetName().Name);

            Assert.NotSame(hostAssembly, typeof(IPlugin).Assembly);
            Assert.NotSame(hostAssembly, typeof(DataLinkIndexResponse).Assembly);
            Assert.Null(hostAssembly.GetType("UmamusumeResponseAnalyzer.Game.TurnInfo.SingleModeTurnData"));
            Assert.DoesNotContain(hostAssembly.GetTypes(), type =>
                type.Namespace == "Gallop" ||
                type.Namespace?.StartsWith("Gallop.", StringComparison.Ordinal) == true);
        }
    }
}
