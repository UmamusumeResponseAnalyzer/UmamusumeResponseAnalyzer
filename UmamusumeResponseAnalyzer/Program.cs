
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using System;
using System.Text.RegularExpressions;

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
                            "Update events.json",
                            "Kill process who occupied 4693 ports"
                            }
                    ));
                switch (prompt)
                {
                    case "Update events.json":
                        await AnsiConsole.Progress()
                            .StartAsync(async ctx =>
                            {
                                var client = new HttpClient();
                                var task = ctx.AddTask("Downloading events.json from github", false);
                                using var response = await client.GetAsync("https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/events.json", HttpCompletionOption.ResponseContentRead);
                                response.EnsureSuccessStatusCode();
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
                            });
                        Console.Clear();
                        AnsiConsole.MarkupLine($"Download completed!");
                        Console.WriteLine("Press any key to return main menu...");
                        Console.ReadKey();
                        break;
                    case "Kill process who occupied 4693 ports":
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
                        Console.ReadLine();
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