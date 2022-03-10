
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
                        Resource.LaunchMenu_SetRaceSchedule,
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
                        if (i.Value.GetType() == typeof(bool) && (bool)i.Value)
                        {
                            multiSelection.Select(i.Key);
                        }
                    }
                    foreach (var i in Config.ConfigSet)
                    {
                        if (i.Value != Array.Empty<string>() && Config.Configuration.Where(x => x.Value.GetType() == typeof(bool) && (bool)x.Value == true).Select(x => x.Key).Intersect(i.Value).Count() == i.Value.Length)
                        {
                            multiSelection.Select(i.Key);
                        }
                    }
                    var options = AnsiConsole.Prompt(multiSelection);
                    foreach (var i in Config.Configuration.Keys)
                    {
                        if (options.Contains(i))
                            Config.Set(i, true);
                        else
                            Config.Set(i, false);
                    }
                }
                else if (prompt == Resource.LaunchMenu_SetRaceSchedule)
                {
                    var races = AnsiConsole.Ask<string>(Resource.LaunchMenu_SetRaceScheduleInstruction);
                    Config.Set("Races", races.Split(',').ToList());
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
                            { //races.json
                                var task = ctx.AddTask(Resource.LaunchMenu_Update_DownloadRacesInstruction, false);
                                using var response = await client.GetAsync("https://cdn.jsdelivr.net/gh/EtherealAO/UmamusumeResponseAnalyzer@master/races.json", HttpCompletionOption.ResponseContentRead);
                                task.MaxValue(response.Content.Headers.ContentLength ?? 0);
                                task.StartTask();
                                using var contentStream = await response.Content.ReadAsStreamAsync();
                                using var fileStream = new FileStream(Database.RACE_CODES_FILEPATH, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
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
                            { //id.json
                                var task = ctx.AddTask(Resource.LaunchMenu_Update_DownloadIdToNameInstruction, false);
                                using var response = await client.GetAsync("https://cdn.jsdelivr.net/gh/EtherealAO/UmamusumeResponseAnalyzer@master/id.json", HttpCompletionOption.ResponseContentRead);
                                task.MaxValue(response.Content.Headers.ContentLength ?? 0);
                                task.StartTask();
                                using var contentStream = await response.Content.ReadAsStreamAsync();
                                using var fileStream = new FileStream(Database.ID_TO_NAME_FILEPATH, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
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
            await AnsiConsole.Status().StartAsync(Resource.LaunchMenu_Start_Checking, async ctx =>
             {
                 var processes = Process.GetProcessesByName("umamusume");
                 AnsiConsole.MarkupLine(string.Format(Resource.LaunchMenu_Start_Checking_Log, string.Format(Resource.LaunchMenu_Start_Checking_Found, processes.Length)));
                 if (!processes.Any())
                 {
                     ctx.Spinner(Spinner.Known.BouncingBar);
                     ctx.Status(Resource.LaunchMenu_Start_GetToken);
                     var dmmToken = await DMM.GetExecuteArgsAsync();
                     AnsiConsole.MarkupLine(string.Format(Resource.LaunchMenu_Start_Checking_Log, string.IsNullOrEmpty(dmmToken) ? Resource.LaunchMenu_Start_TokenFailed : Resource.LaunchMenu_Start_TokenGot));
                     ctx.Status(Resource.LaunchMenu_Start_Launching);
                     if (!string.IsNullOrEmpty(dmmToken)) DMM.Launch(dmmToken);
                 }
                 else
                 {
                     ctx.Status(Resource.LaunchMenu_Start_Checking_AlreadyRunning);
                 }
             });
            Database.Initialize();
            Server.Start();
            AnsiConsole.MarkupLine(Resource.LaunchMenu_Start_Started);
            while (true)
            {
                Console.ReadLine();
            }
        }
    }
}