using static HistoryConfigDialog;
using System.Globalization;
using System.Text.Json;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Time;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;

var cases = new PluginCase[]
{
    new("BreedersScenarioAnalyzer", static () => new BreedersScenarioAnalyzer.BreedersScenarioAnalyzer()),
    new("CookScenarioAnalyzer", static () => new CookScenarioAnalyzer.CookScenarioAnalyzer()),
    new("EventResponseAnalyzer", static () => new EventResponseAnalyzer.EventResponseAnalyzer()),
    new("LegendScenarioAnalyzer", static () => new LegendScenarioAnalyzer.LegendScenarioAnalyzer()),
    new("MechaScenarioAnalyzer", static () => new MechaScenarioAnalyzer.MechaScenarioAnalyzer()),
    new("OldScenarioAnalyzer", static () => new OldScenarioAnalyzer.OldScenarioAnalyzer()),
    new("OnsenScenarioAnalyzer", static () => new OnsenScenarioAnalyzer.OnsenScenarioAnalyzer()),
    new("PioneerScenarioAnalyzer", static () => new PioneerScenarioAnalyzer.PioneerScenarioAnalyzer()),
    new("RamenScenarioAnalyzer", static () => new RamenScenarioAnalyzer.RamenScenarioAnalyzer()),
    new("UAFScenarioAnalyzer", static () => new UAFScenarioAnalyzer.UAFScenarioAnalyzer()),
};

using var application = Application.Create(new VirtualTimeProvider()).Init(DriverRegistry.Names.ANSI);
application.Driver!.SetScreenSize(100, 30);

foreach (var pluginCase in cases)
{
    Console.WriteLine($"RUN {pluginCase.InternalName}");
    AssertMissingSettingsUsesDefaultWithoutCreatingFile(application, pluginCase);
    AssertInvalidSettingsFailFast(application, pluginCase);
    AssertConfigTransactions(application, pluginCase);
    Console.WriteLine($"PASS {pluginCase.InternalName}");
}

Console.WriteLine($"Analyzer history config smoke passed: {cases.Length} plugins.");

static void AssertMissingSettingsUsesDefaultWithoutCreatingFile(
    IApplication application,
    PluginCase pluginCase)
{
    using var currentDirectory = new TempCurrentDirectory($"{pluginCase.InternalName}-default");
    var settingsPath = SettingsPath(pluginCase);
    var plugin = pluginCase.Create();
    using var context = new RuntimePluginContext(application);
    try
    {
        plugin.Initialize(context);
        AssertDraftLimit(application, plugin, 100, $"{pluginCase.InternalName} missing-file default");
        Assert(!File.Exists(settingsPath), $"{pluginCase.InternalName} must not create missing settings.json during Initialize.");
    }
    finally
    {
        plugin.Dispose();
    }
}

static void AssertInvalidSettingsFailFast(IApplication application, PluginCase pluginCase)
{
    var invalidSettings = new (string Name, string Json)[]
    {
        ("malformed", "{"),
        ("missing", "{}"),
        ("unknown", "{\"historyLimit\":100,\"unknown\":true}"),
        ("wrong-case", "{\"HistoryLimit\":100}"),
        ("below-range", "{\"historyLimit\":-1}"),
        ("above-range", "{\"historyLimit\":1001}"),
    };

    foreach (var invalid in invalidSettings)
    {
        using var currentDirectory = new TempCurrentDirectory(
            $"{pluginCase.InternalName}-{invalid.Name}");
        var settingsPath = SettingsPath(pluginCase);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, invalid.Json);

        var plugin = pluginCase.Create();
        using var context = new RuntimePluginContext(application);
        try
        {
            Exception? failure = null;
            try
            {
                plugin.Initialize(context);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            Assert(
                failure is InvalidDataException or JsonException,
                failure is null
                    ? $"{pluginCase.InternalName} accepted {invalid.Name} settings."
                    : $"{pluginCase.InternalName} {invalid.Name} settings failed with unexpected {failure.GetType().Name}: {failure.Message}");
        }
        finally
        {
            plugin.Dispose();
        }
    }
}

static void AssertConfigTransactions(IApplication application, PluginCase pluginCase)
{
    using var currentDirectory = new TempCurrentDirectory($"{pluginCase.InternalName}-config");
    var settingsPath = SettingsPath(pluginCase);
    var plugin = pluginCase.Create();

    OnNextDialog(application, dialog =>
    {
        SetHistoryLimit(dialog, 23);
        AcceptButton(application, dialog, "保存");
    });
    plugin.ConfigPromptAsync(application).GetAwaiter().GetResult();
    AssertSettings(settingsPath, 23, $"{pluginCase.InternalName} pre-Initialize Save");

    using var context = new RuntimePluginContext(application);
    plugin.Initialize(context);
    AssertDraftLimit(application, plugin, 23, $"{pluginCase.InternalName} reload after pre-Initialize Save");

    OnNextDialog(application, dialog =>
    {
        SetHistoryLimit(dialog, 31);
        AcceptButton(application, dialog, "保存");
    });
    plugin.ConfigPromptAsync(application).GetAwaiter().GetResult();
    AssertSettings(settingsPath, 31, $"{pluginCase.InternalName} active Save");
    AssertDraftLimit(application, plugin, 31, $"{pluginCase.InternalName} active Save runtime apply");

    var baseline = File.ReadAllBytes(settingsPath);
    AssertCanceledWithoutMutation(
        application,
        pluginCase,
        plugin,
        settingsPath,
        baseline,
        dialog => AcceptButton(application, dialog, "取消"),
        "Cancel");
    AssertCanceledWithoutMutation(
        application,
        pluginCase,
        plugin,
        settingsPath,
        baseline,
        _ => application.Keyboard.RaiseKeyDownEvent(Key.Esc),
        "Esc");
    AssertCanceledWithoutMutation(
        application,
        pluginCase,
        plugin,
        settingsPath,
        baseline,
        dialog => application.RequestStop(dialog),
        "close");

    using (var cancellation = new CancellationTokenSource())
    {
        OnNextDialog(application, dialog =>
        {
            SetHistoryLimit(dialog, 991);
            cancellation.Cancel();
        });
        AssertCanceled(
            plugin.ConfigPromptAsync(application, cancellation.Token),
            $"{pluginCase.InternalName} cancellation token");
        AssertUnchanged(application, pluginCase, plugin, settingsPath, baseline, "cancellation token");
    }

    plugin.Dispose();

    var reloaded = pluginCase.Create();
    using var reloadedContext = new RuntimePluginContext(application);
    try
    {
        reloaded.Initialize(reloadedContext);
        AssertDraftLimit(application, reloaded, 31, $"{pluginCase.InternalName} rebuilt instance reload");
    }
    finally
    {
        reloaded.Dispose();
    }
}

static void AssertCanceledWithoutMutation(
    IApplication application,
    PluginCase pluginCase,
    IPlugin plugin,
    string settingsPath,
    byte[] baseline,
    Action<Dialog> cancel,
    string operation)
{
    OnNextDialog(application, dialog =>
    {
        SetHistoryLimit(dialog, 991);
        cancel(dialog);
    });
    AssertCanceled(plugin.ConfigPromptAsync(application), $"{pluginCase.InternalName} {operation}");
    AssertUnchanged(application, pluginCase, plugin, settingsPath, baseline, operation);
}

static void AssertUnchanged(
    IApplication application,
    PluginCase pluginCase,
    IPlugin plugin,
    string settingsPath,
    byte[] baseline,
    string operation)
{
    Assert(
        baseline.SequenceEqual(File.ReadAllBytes(settingsPath)),
        $"{pluginCase.InternalName} {operation} wrote settings.json.");
    AssertDraftLimit(
        application,
        plugin,
        31,
        $"{pluginCase.InternalName} {operation} changed runtime historyLimit");
}

static void AssertCanceled(Task task, string operation)
{
    try
    {
        task.GetAwaiter().GetResult();
        throw new InvalidOperationException($"{operation} must cancel ConfigPromptAsync.");
    }
    catch (OperationCanceledException)
    {
    }
}

static void AssertSettings(string path, int expected, string operation)
{
    Assert(File.Exists(path), $"{operation} did not write settings.json.");
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var properties = document.RootElement.EnumerateObject().ToArray();
    AssertEqual(1, properties.Length, $"{operation} settings property count");
    AssertEqual("historyLimit", properties[0].Name, $"{operation} settings property name");
    AssertEqual(expected, properties[0].Value.GetInt32(), $"{operation} historyLimit");
}

static void OnNextDialog(IApplication application, Action<Dialog> action)
{
    application.AddTimeout(TimeSpan.Zero, () =>
    {
        if (application.TopRunnableView is not Dialog dialog)
            return true;

        action(dialog);
        return false;
    });
}

static void AssertDraftLimit(
    IApplication application,
    IPlugin plugin,
    int expected,
    string operation)
{
    int? actual = null;
    OnNextDialog(application, dialog =>
    {
        var views = Descendants(dialog).ToArray();
        actual = views.OfType<NumericUpDown<int>>().FirstOrDefault()?.Value;
        if (actual is null && views.OfType<TextField>().FirstOrDefault() is { } text)
            actual = int.Parse(text.Text, CultureInfo.InvariantCulture);
        AcceptButton(application, dialog, "取消");
    });
    AssertCanceled(plugin.ConfigPromptAsync(application), $"{operation} draft inspection");
    AssertEqual(expected, actual, operation);
}

static string SettingsPath(PluginCase pluginCase)
    => Path.Combine("PluginData", pluginCase.InternalName, "settings.json");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertEqual<T>(T expected, T actual, string operation)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{operation}: expected {expected}, actual {actual}.");
}

sealed record PluginCase(string InternalName, Func<IPlugin> Create);
