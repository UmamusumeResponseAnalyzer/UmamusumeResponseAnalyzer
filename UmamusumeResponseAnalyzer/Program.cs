
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using System;
using System.Text.RegularExpressions;
using MessagePack;
using System.Globalization;
using UmamusumeResponseAnalyzer.Localization;

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
            var json = MessagePackSerializer.ConvertToJson(msgpackBytes);
            File.WriteAllText(jsonPath, JObject.Parse(json).ToString());
            File.WriteAllText(jsonPath + ".msgpack.json", JsonConvert.SerializeObject(Server.TryDeserialize<Gallop.SingleModeCheckEventResponse>(msgpackBytes), Formatting.Indented));
#endif
            Console.BufferWidth = 160;
            Console.SetWindowSize(Console.BufferWidth, Console.WindowHeight + 3);
            Console.OutputEncoding = Encoding.UTF8;
            Config.Initialize();
            var prompt = string.Empty;
            do
            {
                prompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .Title(Resource.LaunchMenu)
                    .PageSize(10)
                    .AddChoices(new[]
                    {
                        Resource.LaunchMenu_Start,
                        Resource.LaunchMenu_Options,
                        Resource.LaunchMenu_Update,
                        Resource.LaunchMenu_Kill4693
                    }
                    ));
                if (prompt == Resource.LaunchMenu_Options)
                {
                    var multiSelection = new MultiSelectionPrompt<string>()
                        .Title(Resource.LaunchMenu_Options)
                        .Mode(SelectionMode.Leaf)
                        .PageSize(10)
                        .InstructionsText(Resource.LaunchMenu_Options_Instruction);
                    foreach (var i in Config.ConfigSet)
                    {
                        if (i.Value == Array.Empty<string>())
                        {
                            multiSelection.AddChoice(i.Key);
                        }
                        else
                        {
                            multiSelection.AddChoiceGroup(i.Key, i.Value);
                        }
                    }
                    foreach (var i in Config.Configuration)
                    {
                        if (i.Value)
                        {
                            multiSelection.Select(i.Key);
                        }
                    }
                    foreach (var i in Config.ConfigSet)
                    {
                        if (i.Value != Array.Empty<string>() && Config.Configuration.Where(x => x.Value == true).Select(x => x.Key).Intersect(i.Value).Count() == i.Value.Length)
                        {
                            multiSelection.Select(i.Key);
                        }
                    }
                    var options = AnsiConsole.Prompt(multiSelection);
                    foreach (var i in Config.Configuration.Keys)
                    {
                        if (options.Contains(i))
                            Config.Configuration[i] = true;
                        else
                            Config.Configuration[i] = false;
                    }
                }
                else if (prompt == Resource.LaunchMenu_Update)
                {
                    await AnsiConsole.Progress()
                        .StartAsync(async ctx =>
                        {
                            var client = new HttpClient();
                            { //events.json
                                var task = ctx.AddTask(Resource.LaunchMenu_Update_DownloadEventsInstruction, false);
                                using var response = await client.GetAsync("https://cdn.jsdelivr.net/gh/EtherealAO/UmamusumeResponseAnalyzer@master/events.json", HttpCompletionOption.ResponseContentRead);
                                task.MaxValue(response.Content.Headers.ContentLength ?? 0);
                                task.StartTask();
                                using var contentStream = await response.Content.ReadAsStreamAsync();
                                using var fileStream = new FileStream(Database.EVENT_NAME_FILEPATH, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
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
                                var task = ctx.AddTask(Resource.LaunchMenu_Update_DownloadSuccessEventsInstruction, false);
                                using var response = await client.GetAsync("https://cdn.jsdelivr.net/gh/EtherealAO/UmamusumeResponseAnalyzer@master/successevents.json", HttpCompletionOption.ResponseContentRead);
                                task.MaxValue(response.Content.Headers.ContentLength ?? 0);
                                task.StartTask();
                                using var contentStream = await response.Content.ReadAsStreamAsync();
                                using var fileStream = new FileStream(Database.SUCCESS_EVENT_FILEPATH, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
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
                    AnsiConsole.MarkupLine(Resource.LaunchMenu_Update_DownloadedInstruction);
                    Console.WriteLine(Resource.LaunchMenu_Options_BackToMenuInstruction);
                    Console.ReadKey();
                }
                else if (prompt == Resource.LaunchMenu_Kill4693)
                {
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

                                var decision = AnsiConsole.Confirm(string.Format(Resource.LaunchMenu_Kill4693_Confirm, pid, name));
                                if (decision)
                                {
                                    if (name == "System")
                                    {
                                        Console.WriteLine(Resource.LaunchMenu_Kill4693_KillSystemAlert);
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
                    Console.WriteLine(Resource.LaunchMenu_Kill4693_NotFound);
                    Console.WriteLine(Resource.LaunchMenu_Options_BackToMenuInstruction);
                    Console.ReadKey();
                }
                Console.Clear();
            } while (prompt != Resource.LaunchMenu_Start);
            Database.Initialize();
            Server.Start();
            while (true)
            {
                Console.ReadLine();
            }
        }
    }
}