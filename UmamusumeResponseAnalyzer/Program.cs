
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using System;
using System.Text.RegularExpressions;
using MessagePack;

namespace UmamusumeResponseAnalyzer
{
    public static class UmamusumeResponseAnalyzer
    {
        public static async Task Main()
        {
#if DEBUG
            var msgpackPath = @"response/637795073363227948.bin";
            var msgpackBytes = File.ReadAllBytes(msgpackPath);
            var jsonPath = @"response/637795073363227948.json";
            var json = MessagePack.MessagePackSerializer.ConvertToJson(msgpackBytes);
            File.WriteAllText(jsonPath, JObject.Parse(json).ToString());
            File.WriteAllText(jsonPath + ".msgpack.json", JsonConvert.SerializeObject(Server.TryDeserialize<Gallop.SingleModeCheckEventResponse>(msgpackBytes), Formatting.Indented));
#endif
            Console.BufferWidth = 160;
            Console.SetWindowSize(Console.BufferWidth, Console.WindowHeight + 3);
            Console.OutputEncoding = Encoding.UTF8;
            var prompt = string.Empty;
            do
            {
                prompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .Title("Launch Menu")
                    .PageSize(10)
                    .AddChoices(new[]
                            {
                            "Start!",
                            "Options",
                            "Update events.json",
                            "Kill process who occupied 4693 ports"
                            }
                    ));
                switch (prompt)
                {
                    case "Options":
                        var multiSelection = new MultiSelectionPrompt<string>()
                            .Title("Options")
                            .Mode(SelectionMode.Leaf)
                            .PageSize(10)
                            .InstructionsText(
                                "[grey](Press [blue]<space>[/] to toggle a fruit, " +
                                "[green]<enter>[/] to accept)[/]");
                        foreach (var i in Config.ConfigSet)
                        {
                            if (i.Value == Array.Empty<string>())
                            {
                                Debug.WriteLine($"Add selection: {i.Key}");
                                multiSelection.AddChoice(i.Key);
                            }
                            else
                            {
                                Debug.WriteLine($"Add selection group {i.Key}: {string.Join(',', i.Value)}");
                                multiSelection.AddChoiceGroup(i.Key, i.Value);
                            }
                        }
                        foreach (var i in Config.Configuration)
                        {
                            if (i.Value)
                            {
                                Debug.WriteLine($"Set {i.Key} to true because of configuration file");
                                multiSelection.Select(i.Key);
                            }
                        }
                        foreach (var i in Config.ConfigSet)
                        {
                            if (i.Value != Array.Empty<string>() && Config.Configuration.Where(x => x.Value == true).Select(x => x.Key).Intersect(i.Value).Count() == i.Value.Length)
                            {
                                Debug.WriteLine($"All of {i.Key} was selected, select {i.Key} too");
                                multiSelection.Select(i.Key);
                            }
                        }
                        var options = AnsiConsole.Prompt(multiSelection);
#if DEBUG
                        foreach (var i in options)
                        {
                            if (!Config.Configuration.ContainsKey(i))
                                Config.Configuration.Add(i, true);
                        }
                        File.WriteAllBytes(@".config", MessagePackSerializer.Serialize(Config.Configuration));
#endif
                        foreach (var i in Config.Configuration.Keys)
                        {
                            if (options.Contains(i))
                                Config.Configuration[i] = true;
                            else
                                Config.Configuration[i] = false;
                        }
                        break;
                    case "Update events.json":
                        await AnsiConsole.Progress()
                            .StartAsync(async ctx =>
                            {
                                var client = new HttpClient();
                                { //events.json
                                    var task = ctx.AddTask("Downloading events.json from github", false);
                                    using var response = await client.GetAsync("https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/events.json", HttpCompletionOption.ResponseContentRead);
                                    task.MaxValue(response.Content.Headers.ContentLength ?? 0);
                                    task.StartTask();
                                    using var contentStream = await response.Content.ReadAsStreamAsync();
                                    using var fileStream = new FileStream("events.json", FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                                    var buffer = new byte[8192];
                                    while (true)
                                    {
                                        var read = await contentStream.ReadAsync(buffer);
                                        if (read == 0)
                                            break;
                                        task.Increment(read);
                                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                                    }
                                }
                                { //successevent.json
                                    var task = ctx.AddTask("Downloading successevent.json from github", false);
                                    using var response = await client.GetAsync("https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/successevent.json", HttpCompletionOption.ResponseContentRead);
                                    task.MaxValue(response.Content.Headers.ContentLength ?? 0);
                                    task.StartTask();
                                    using var contentStream = await response.Content.ReadAsStreamAsync();
                                    using var fileStream = new FileStream("successevent.json", FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                                    var buffer = new byte[8192];
                                    while (true)
                                    {
                                        var read = await contentStream.ReadAsync(buffer);
                                        if (read == 0)
                                            break;
                                        task.Increment(read);
                                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                                    }
                                }
                            });
                        AnsiConsole.MarkupLine($"Download completed!");
                        Console.WriteLine("Press any key to return main menu...");
                        Console.ReadKey();
                        break;
                    case "Kill process who occupied 4693 port":
                        using (var Proc = new Process())
                        {

                            var StartInfo = new ProcessStartInfo();
                            StartInfo.FileName = "netstat.exe";
                            StartInfo.Arguments = "-a -n -o";
                            StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                            StartInfo.UseShellExecute = false;
                            StartInfo.RedirectStandardInput = true;
                            StartInfo.RedirectStandardOutput = true;
                            StartInfo.RedirectStandardError = true;
                            Proc.StartInfo = StartInfo;
                            Proc.Start();
                            var NetStatRows = Regex.Split(Proc.StandardOutput.ReadToEnd() + Proc.StandardError.ReadToEnd(), "\r\n");
                            foreach (string NetStatRow in NetStatRows)
                            {
                                string[] Tokens = Regex.Split(NetStatRow, "\\s+");
                                if (Tokens.Length > 4 && (Tokens[1].Equals("UDP") || Tokens[1].Equals("TCP")))
                                {
                                    string IpAddress = Regex.Replace(Tokens[2], @"\[(.*?)\]", "1.1.1.1");
                                    var port = Convert.ToInt32(IpAddress.Split(':')[1]);
                                    if (port != 4693) continue;
                                    var pid = Tokens[1] == "UDP" ? Convert.ToInt16(Tokens[4]) : Convert.ToInt16(Tokens[5]);
                                    var name = Tokens[1] == "UDP" ? Process.GetProcessById(pid).ProcessName : Process.GetProcessById(pid).ProcessName;

                                    var decision = AnsiConsole.Confirm($"Confirm to kill ProcessId:{pid} Name:{name}?");
                                    if (decision)
                                    {
                                        if (name == "System")
                                        {
                                            Console.WriteLine("YOU CAN'T KILL SYSTEM! Please follow FAQ to solve conflict problem.");
                                            break;
                                        }
                                        Process.GetProcessById(pid).Kill();
                                    }
                                    else
                                    {
                                        Environment.Exit(1);
                                    }
                                }
                            }
                        }
                        Console.WriteLine("Program using 4693 port not found!");
                        Console.WriteLine("Press any key to return main menu...");
                        Console.ReadKey();
                        break;
                }
                Console.Clear();
            } while (prompt != "Start!");
            Database.Initialize();
            Server.Start();
            while (true)
            {
                Console.ReadLine();
            }
        }
    }
}