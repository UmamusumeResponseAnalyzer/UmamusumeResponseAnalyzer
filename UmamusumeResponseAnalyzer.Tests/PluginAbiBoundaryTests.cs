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
        public void PluginContractsAndGallopModelsComeFromHostAssembly()
        {
            var hostAssembly = typeof(Server).Assembly;

            Assert.Same(hostAssembly, typeof(IPlugin).Assembly);
            Assert.Same(hostAssembly, typeof(AnalyzerAttribute).Assembly);
            Assert.Same(hostAssembly, typeof(ILiveDisplayOutput).Assembly);
            Assert.Same(hostAssembly, typeof(LiveDisplayWorkspace).Assembly);
            Assert.Same(hostAssembly, typeof(IGameEndpoint).Assembly);
            Assert.Same(hostAssembly, typeof(DataLinkIndexResponse).Assembly);
            Assert.Null(hostAssembly.GetType("UmamusumeResponseAnalyzer.Game.TurnInfo.SingleModeTurnData"));
            Assert.Contains(hostAssembly.GetTypes(), type =>
                type.Namespace == "Gallop" ||
                type.Namespace?.StartsWith("Gallop.", StringComparison.Ordinal) == true);
        }

        [Fact]
        public void GeneratedGallopSourcesAreSourceOnlyAtOutputRoot()
        {
            var repositoryRoot = FindRepositoryRoot();
            var gallopRoot = Path.Combine(repositoryRoot.FullName, "UmamusumeResponseAnalyzer", "Gallop");

            Assert.True(Directory.Exists(gallopRoot), $"找不到 Gallop generated source directory: {gallopRoot}");
            Assert.False(File.Exists(Path.Combine(gallopRoot, "_generated.txt")), "Gallop generated sources should not include _generated.txt.");
            Assert.False(Directory.Exists(Path.Combine(gallopRoot, "Gallop")), "Gallop generated sources should be directly under the Gallop output directory.");
            Assert.True(File.Exists(Path.Combine(gallopRoot, "RequestBase.cs")), "Gallop DTO sources should be directly under the Gallop output directory.");
            Assert.True(File.Exists(Path.Combine(gallopRoot, "Endpoints", "GameApi.g.cs")), "Gallop endpoint sources should be directly under Gallop/Endpoints.");
        }

        private static DirectoryInfo FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UmamusumeResponseAnalyzer.sln")))
                    return directory;
            }

            throw new InvalidOperationException("找不到 UmamusumeResponseAnalyzer repository root。");
        }
    }
}
