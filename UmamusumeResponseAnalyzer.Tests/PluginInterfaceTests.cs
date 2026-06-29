using Spectre.Console;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    public sealed class PluginInterfaceTests
    {
        [Fact]
        public async Task ConfigPromptAsync_DefaultImplementation_CallsSynchronousOverride()
        {
            IPlugin plugin = new SyncConfigPromptPlugin();

            await plugin.ConfigPromptAsync();

            Assert.True(((SyncConfigPromptPlugin)plugin).ConfigPromptCalled);
        }

        sealed class SyncConfigPromptPlugin : IPlugin
        {
            public bool ConfigPromptCalled { get; private set; }
            public string Name => "SyncConfigPromptPlugin";
            public string Author => "Test";
            public string[] Targets => [];

            public void ConfigPrompt()
            {
                ConfigPromptCalled = true;
            }

            public Task UpdatePlugin(ProgressContext ctx)
            {
                return Task.CompletedTask;
            }
        }
    }
}
