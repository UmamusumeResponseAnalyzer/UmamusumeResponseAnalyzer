var separator = Array.IndexOf(args, "--");
var requestedIds = separator >= 0 ? args[(separator + 1)..] : args;
var casesById = PluginCases.All.ToDictionary(pluginCase => pluginCase.Id, StringComparer.OrdinalIgnoreCase);

var selectedCases = new List<PluginCase>();
foreach (var requestedId in requestedIds)
{
    if (!casesById.TryGetValue(requestedId, out var pluginCase))
    {
        Console.Error.WriteLine(
            $"Unknown plugin id '{requestedId}'. Available: {string.Join(", ", PluginCases.All.Select(x => x.Id))}");
        Environment.ExitCode = 2;
        return;
    }

    selectedCases.Add(pluginCase);
}

if (selectedCases.Count == 0)
    selectedCases.AddRange(PluginCases.All);

using var ui = new WorkspaceSmokeSession();
var failures = new List<string>();

void RunPluginCase(PluginCase pluginCase)
{
    try
    {
        pluginCase.Run(ui);
        if (pluginCase.Id is "OldScenarioAnalyzer" or "OnsenScenarioAnalyzer" or "PioneerScenarioAnalyzer" or "UAFScenarioAnalyzer")
            PluginCases.VerifyLocalizedTraining(pluginCase, ui);
        Console.WriteLine($"PASS {pluginCase.Id}");
    }
    catch (Exception ex)
    {
        failures.Add($"{pluginCase.Id}: {ex.Message}");
        Console.Error.WriteLine($"FAIL {pluginCase.Id}: {ex.Message}");
    }
}

foreach (var pluginCase in selectedCases.Where(pluginCase => pluginCase.Id != "DMMPlugin"))
    RunPluginCase(pluginCase);

if (selectedCases.Any(pluginCase => pluginCase.Id == "EventLoggerPlugin"))
{
    try
    {
        EventLoggerRuntimeSmoke.Run(ui);
        Console.WriteLine("PASS EventLoggerPlugin runtime behavior");
    }
    catch (Exception ex)
    {
        failures.Add($"EventLoggerPlugin runtime behavior: {ex.Message}");
        Console.Error.WriteLine($"FAIL EventLoggerPlugin runtime behavior: {ex.Message}");
    }
}

// The DMM failure probe intentionally leaves a persistent notification card.
foreach (var pluginCase in selectedCases.Where(pluginCase => pluginCase.Id == "DMMPlugin"))
    RunPluginCase(pluginCase);

if (selectedCases.Any(pluginCase => pluginCase.Id == "GamePacketCollector"))
{
    try
    {
        GamePacketCollectorRuntimeSmoke.Run(ui);
        Console.WriteLine("PASS GamePacketCollector permanent upload failure");
    }
    catch (Exception ex)
    {
        failures.Add($"GamePacketCollector permanent upload failure: {ex.Message}");
        Console.Error.WriteLine($"FAIL GamePacketCollector permanent upload failure: {ex.Message}");
    }
}

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine($"PASS plugin runtime smoke: plugins={selectedCases.Count}");
    return;
}

Console.Error.WriteLine($"FAILED plugin runtime smoke: {failures.Count}/{selectedCases.Count}");
foreach (var failure in failures)
    Console.Error.WriteLine($"- {failure}");
Environment.ExitCode = 1;
