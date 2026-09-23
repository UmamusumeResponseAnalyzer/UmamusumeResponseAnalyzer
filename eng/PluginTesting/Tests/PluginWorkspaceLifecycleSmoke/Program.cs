using Terminal.Gui.App;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

using var ui = new WorkspaceSmokeSession();
var plugins = new (string Name, Func<IPlugin> Create)[]
{
    ("AIRedirector", () => new AIRedirector.AIRedirector()),
    ("BreedersScenarioAnalyzer", () => new BreedersScenarioAnalyzer.BreedersScenarioAnalyzer()),
    ("CookScenarioAnalyzer", () => new CookScenarioAnalyzer.CookScenarioAnalyzer()),
    ("DMMPlugin", () => new DMMPlugin.DMMPlugin()),
    ("EventLoggerPlugin", () => new EventLoggerPlugin.EventLoggerPlugin()),
    ("EventResponseAnalyzer", () => new EventResponseAnalyzer.EventResponseAnalyzer()),
    ("ExamplePlugin", () => new ExamplePlugin.ExamplePlugin()),
    ("GamePacketCollector", () => new GamePacketCollector.GamePacketCollectorPlugin()),
    ("LegendScenarioAnalyzer", () => new LegendScenarioAnalyzer.LegendScenarioAnalyzer()),
    ("MechaScenarioAnalyzer", () => new MechaScenarioAnalyzer.MechaScenarioAnalyzer()),
    ("Notifications", () => new Notifications.Notifications()),
    ("OldScenarioAnalyzer", () => new OldScenarioAnalyzer.OldScenarioAnalyzer()),
    ("OnsenScenarioAnalyzer", () => new OnsenScenarioAnalyzer.OnsenScenarioAnalyzer()),
    ("PioneerScenarioAnalyzer", () => new PioneerScenarioAnalyzer.PioneerScenarioAnalyzer()),
    ("RaceAnalyzer", () => new RaceAnalyzer.RaceAnalyzer()),
    ("RamenScenarioAnalyzer", () => new RamenScenarioAnalyzer.RamenScenarioAnalyzer()),
    ("SendGameStatusPlugin", () => new SendGameStatusPlugin.SendGameStatusPlugin()),
    ("SkillEffectPlugin", () => new SkillEffectPlugin.SkillEffectPlugin()),
    ("SkillTipsResponseAnalyzer", () => new SkillTipsResponseAnalyzer.SkillTipsResponseAnalyzer()),
    ("TeamStadiumOpponentListResponseAnalyzer", () => new TeamStadiumOpponentListResponseAnalyzer.TeamStadiumOpponentListResponseAnalyzer()),
    ("UAFScenarioAnalyzer", () => new UAFScenarioAnalyzer.UAFScenarioAnalyzer()),
    ("WinSaddleAnalyzer", () => new WinSaddleAnalyzer.WinSaddleAnalyzer())
};

if (plugins.Length != 22)
    throw new InvalidOperationException($"Workspace lifecycle smoke must cover 22 plugins, got {plugins.Length}.");

var originalCwd = Directory.GetCurrentDirectory();
foreach (var (name, create) in plugins)
{
    var testDirectory = Path.Combine(
        Path.GetTempPath(),
        "ura-plugin-workspace-lifecycle",
        $"{name}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(testDirectory);
    try
    {
        Directory.SetCurrentDirectory(testDirectory);
        PreparePluginState(name);
        ui.Bootstrap.SwitchTo();
        var plugin = create();
        try
        {
            plugin.Initialize(new SmokePluginContext(ui.Application));
            ui.Flush();
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
                throw new InvalidOperationException($"{name} Initialize changed Workspace.Current.");

            if (name == "GamePacketCollector"
                && !ui.CaptureScreen().Contains("GamePacketCollector", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "GamePacketCollector initialization status was not visible in the bootstrap framebuffer.");
            }
        }
        finally
        {
            plugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        ui.Flush();
        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
            throw new InvalidOperationException($"{name} unused Dispose changed Workspace.Current.");
        Console.WriteLine($"PASS {name}");
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCwd);
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, recursive: true);
    }
}

AssertCanonicalSharedPanelContract(ui);
AssertTombstoneRejectsLateCallback(ui);
Console.WriteLine($"PASS plugin workspace lifecycle smoke: plugins={plugins.Length}, shared-contract=2");

static void AssertCanonicalSharedPanelContract(WorkspaceSmokeSession ui)
{
    var firstPluginHandle = Workspace.Create("Shared plugin workspace");
    var secondPluginHandle = Workspace.Create("SHARED PLUGIN WORKSPACE");
    if (!ReferenceEquals(firstPluginHandle, secondPluginHandle))
        throw new InvalidOperationException("Two plugins must receive the same canonical Workspace reference.");
    if (firstPluginHandle.Title != "Shared plugin workspace")
        throw new InvalidOperationException("Canonical Workspace must preserve the first title spelling.");

    firstPluginHandle.SetPanel(
        "shared-key",
        "First plugin",
        WorkspaceContent.Text("first plugin content"));
    secondPluginHandle.SetPanel(
        "shared-key",
        "Second plugin",
        WorkspaceContent.Text("second plugin content"));
    var screen = ui.CaptureScreen();
    if (!screen.Contains("second plugin content", StringComparison.Ordinal) ||
        screen.Contains("first plugin content", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Framebuffer did not expose the final shared panel content.");
    }

    if (!secondPluginHandle.RemovePanel("shared-key"))
        throw new InvalidOperationException("Either plugin must be able to remove the shared panel key.");
    if (firstPluginHandle.RemovePanel("shared-key"))
        throw new InvalidOperationException("Removing an absent shared panel must return false.");
    if (ui.CaptureScreen().Contains("second plugin content", StringComparison.Ordinal))
        throw new InvalidOperationException("RemovePanel left the shared key visible.");
    if (!ReferenceEquals(Workspace.Create("shared plugin workspace"), firstPluginHandle))
        throw new InvalidOperationException("Removing a panel must not remove its Workspace generation.");
}

static void AssertTombstoneRejectsLateCallback(WorkspaceSmokeSession ui)
{
    const string title = "Late callback workspace";
    const string oldPanelText = "OLD-GENERATION-PANEL-SENTINEL";
    const string newPanelText = "NEW-GENERATION-PANEL-SENTINEL";
    const string latePanelText = "LATE-CALLBACK-PANEL-SENTINEL";

    var removed = Workspace.Create(title);
    removed.SetPanel("generation", "Old generation", WorkspaceContent.Text(oldPanelText));
    removed.Remove();

    var recreated = Workspace.Create(title.ToUpperInvariant());
    if (ReferenceEquals(recreated, removed))
        throw new InvalidOperationException("Recreating a removed title must produce a new generation.");
    recreated.SetPanel("generation", "New generation", WorkspaceContent.Text(newPanelText));

    Exception? lateFailure = null;
    Task.Run(() =>
    {
        try
        {
            removed.SetPanel("generation", "Late callback", WorkspaceContent.Text(latePanelText));
        }
        catch (Exception ex)
        {
            lateFailure = ex;
        }
    }).GetAwaiter().GetResult();

    if (lateFailure is not InvalidOperationException)
        throw new InvalidOperationException("A late callback must fail against its tombstoned Workspace handle.");

    removed.Remove();
    var screen = ui.CaptureScreen();
    if (!ReferenceEquals(Workspace.Current, recreated)
        || !ReferenceEquals(Workspace.Create(title.ToLowerInvariant()), recreated))
    {
        throw new InvalidOperationException(
            "A repeated Remove on the tombstoned handle changed the active Workspace generation.");
    }
    if (!screen.Contains(newPanelText, StringComparison.Ordinal)
        || screen.Contains(oldPanelText, StringComparison.Ordinal)
        || screen.Contains(latePanelText, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "The recreated Workspace framebuffer was polluted by the tombstoned generation.");
    }
    recreated.RemovePanel("generation");
}

static void PreparePluginState(string name)
{
    if (name != "GamePacketCollector")
        return;

    var dataDirectory = Path.Combine("PluginData", "游戏包采集");
    Directory.CreateDirectory(dataDirectory);
    File.WriteAllText(
        Path.Combine(dataDirectory, "config.json"),
        """{"uploadUrl":"https://ura.shuise.net/api/GamePackets","serverRegionHint":null,"enabled":false,"endpointGroups":["single-mode"]}""");
}

sealed class SmokePluginContext(IApplication application) : IPluginContext
{
    public IApplication Application { get; } = application;
    public IPluginAnalyzerRegistry Analyzers { get; } = new SmokeAnalyzerRegistry();
    public bool IsPluginAvailable(string internalName) => false;

    public void ReportBackgroundFailure(Exception error)
        => throw new InvalidOperationException("Plugin background work failed.", error);
}

sealed class SmokeAnalyzerRegistry : IPluginAnalyzerRegistry
{
    public void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0) { }
}
