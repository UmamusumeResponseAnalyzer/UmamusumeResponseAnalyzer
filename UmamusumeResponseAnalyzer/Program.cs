using Newtonsoft.Json;
using Spectre.Console;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer.Localization;

namespace UmamusumeResponseAnalyzer
{
    public static class UmamusumeResponseAnalyzer
    {
        public static async Task Main(string[] args)
        {
            Console.Title = $"UmamusumeResponseAnalyzer v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
            Console.OutputEncoding = Encoding.UTF8;
            //加载设置
            Config.Initialize();
            //尝试更新程序本体
            await TryUpdateProgram(args);

            //根据操作系统显示启动菜单
            switch (Environment.OSVersion.GetSystemVersion())
            {
                case SystemVersion.Windows7: //Windows7的终端不支持Prompt，只能简单选择
                    {
                        AnsiConsole.WriteLine("检测到Windows 7");
                        AnsiConsole.WriteLine("按下Y更新数据，按其他任意键启动");
                        if (Console.ReadKey().Key == ConsoleKey.Y)
                        {
                            AnsiConsole.WriteLine("正在更新......");
                            await Update();
                        }
                        foreach (var i in Config.Configuration)
                        {
                            Config.Set(i.Key, true);
                        }
                        break;
                    }
                default:
                    {
                        string prompt;
                        do
                        {
                            prompt = await ShowMenu();
                        }
                        while (prompt != Resource.LaunchMenu_Start); //如果不是启动则重新显示主菜单
                        break;
                    }
            }

            if (File.Exists(DMM.DMM_CONFIG_FILEPATH)) //如果存在DMM的token文件则启用直接登录功能
                await RunUmamusume();

            Database.Initialize(); //初始化马娘相关数据
            Server.Start(); //启动HTTP服务器
            AnsiConsole.MarkupLine(Resource.LaunchMenu_Start_Started);

            while (true)
            {
                Console.ReadLine();
            }
        }
        static async Task<string> ShowMenu()
        {
            var prompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title(Resource.LaunchMenu)
                .PageSize(10)
                .AddChoices(new[]
                {
                        Resource.LaunchMenu_Start,
                        Resource.LaunchMenu_Options,
                        Resource.LaunchMenu_Update
                }
                ));
            if (prompt == Resource.LaunchMenu_Options)
            {
                var multiSelection = new MultiSelectionPrompt<string>()
                    .Title(Resource.LaunchMenu_Options)
                    .Mode(SelectionMode.Leaf)
                    .PageSize(10)
                    .InstructionsText(Resource.LaunchMenu_Options_Instruction);

                foreach (var i in Config.ConfigSet) //根据预设值添加选项
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

                foreach (var i in Config.Configuration) //复原配置文件的选择情况
                {
                    if (i.Value.GetType() == typeof(bool) && (bool)i.Value)
                    {
                        multiSelection.Select(i.Key);
                    }
                }

                foreach (var i in Config.ConfigSet) //如果配置文件中的某一组全被选中，则也选中对应的组
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
            else if (prompt == Resource.LaunchMenu_Update)
            {
                await Update();
            }
            Console.Clear();

            return prompt;
        }
        static async Task RunUmamusume()
        {
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
        }
        static async Task TryUpdateProgram(string[] args)
        {
            var path = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe");
            var exist = File.Exists(path);
            if (args?.Length > 1 && args[0] == "--update")
            {
                path = args[1];
                if (Environment.ProcessPath != default)
                    File.Copy(Environment.ProcessPath, path, true);

                using var Proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    }
                };
                Proc.Start(); //把新程序复制到原来的目录后就启动
                Environment.Exit(0);
            }
            else if (exist && !(Environment.ProcessPath != default && MD5.HashData(File.ReadAllBytes(Environment.ProcessPath)).SequenceEqual(MD5.HashData(File.ReadAllBytes(path))))) //临时目录与当前目录的不一致则认为未更新
            {
                CloseToUpdate();
            }

            if (exist) //删除临时文件
            {
                File.Delete(path);
                await Update(true); //既然有临时文件，那必然是刚更新过的，再执行一次更新保证数据即时
                AnsiConsole.Clear();
            }
#if RELEASE //只在Release里启用自动更新
            _ = Task.Run(async () =>
            {
                while (Config.Get(Resource.ConfigSet_AutoUpdate))
                {
                    await Task.Delay(5 * 60 * 1000); //5min * 60s * 1000ms
                    Console.Title = "正在检查更新......";

                    var client = new HttpClient(new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    });
                    var response = await client.GetAsync(GetDownloadUrl(Resource.LaunchMenu_Update_DownloadProgramInstruction), HttpCompletionOption.ResponseHeadersRead);
                    while (response.StatusCode == System.Net.HttpStatusCode.MovedPermanently || response.StatusCode == System.Net.HttpStatusCode.Found)
                    {
                        response = await client.GetAsync(response.Headers.Location, HttpCompletionOption.ResponseHeadersRead);
                    }
                    if (Environment.ProcessPath != default && response.Content.Headers.GetContentMD5().SequenceEqual(MD5.HashData(File.ReadAllBytes(Environment.ProcessPath)))
                    || File.Exists(path) && response.Content.Headers.GetContentMD5().SequenceEqual(MD5.HashData(File.ReadAllBytes(path)))) //服务器返回的hash和当前文件一致，即没有新的可用版本，直接返回
                    {
                        Console.Title = $"UmamusumeResponseAnalyzer v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
                        continue;
                    }
                    Console.Title = "正在下载更新......";
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                    var buffer = new byte[8192];
                    while (true)
                    {
                        var read = await contentStream.ReadAsync(buffer);
                        if (read == 0)
                            break;
                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    }
                    fileStream.Flush();
                    fileStream.Close();
                    Console.Title = $"重启程序后将进行自动更新";
                    break;
                }
            });
#endif
        }
        static void CloseToUpdate()
        {
            using var Proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe"),
                    Arguments = $"--update \"{Environment.ProcessPath}\"",
                    UseShellExecute = true
                }
            };
            Proc.Start();
            Environment.Exit(0);
        }
        static async Task Update(bool dataOnly = false)
        {
            await AnsiConsole.Progress()
                .Columns(new ProgressColumn[]
                {
                            new TaskDescriptionColumn(),
                            new ProgressBarColumn(),
                            new PercentageColumn(),
                            new RemainingTimeColumn(),
                            new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var tasks = new List<Task>();

                    var eventTask = DownloadAssets(ctx, Resource.LaunchMenu_Update_DownloadEventsInstruction, Database.EVENT_NAME_FILEPATH);
                    tasks.Add(eventTask);

                    var successEventTask = DownloadAssets(ctx, Resource.LaunchMenu_Update_DownloadSuccessEventsInstruction, Database.SUCCESS_EVENT_FILEPATH);
                    tasks.Add(successEventTask);

                    var idToNameTask = DownloadAssets(ctx, Resource.LaunchMenu_Update_DownloadIdToNameInstruction, Database.ID_TO_NAME_FILEPATH);
                    tasks.Add(idToNameTask);

                    var skillTask = DownloadAssets(ctx, Resource.LaunchMenu_Update_DownloadSkillDataInstruction, Database.SKILLS_FILEPATH);
                    tasks.Add(skillTask);

                    var translatedNameTask = DownloadAssets(ctx, Resource.LaunchMenu_Update_DownloadTranslatedNameInstruction, Database.SUPPORT_ID_SHORTNAME_FILEPATH);
                    tasks.Add(translatedNameTask);

                    var climaxItemTask = DownloadAssets(ctx, Resource.LaunchMenu_Update_DownloadClimaxItemInstruction, Database.CLIMAX_ITEM_FILEPATH);
                    tasks.Add(climaxItemTask);

                    if (!dataOnly)
                    {
                        var programTask = DownloadAssets(ctx, Resource.LaunchMenu_Update_DownloadProgramInstruction, Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe"));
                        tasks.Add(programTask);
                    }

                    await Task.WhenAll(tasks);
                });
            AnsiConsole.MarkupLine(Resource.LaunchMenu_Update_DownloadedInstruction);

            if (dataOnly) return;
            if (File.Exists(Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe"))) //如果临时目录里有这个文件说明找到了新版本，从这里启动临时目录中的程序更新
            {
                Console.WriteLine(Resource.LaunchMenu_Update_BeginUpdateProgramInstruction);
                Console.ReadKey();
                CloseToUpdate();
            }
            else
            {
                Console.WriteLine(Resource.LaunchMenu_Update_AlreadyLatestInstruction);
            }
            Console.WriteLine(Resource.LaunchMenu_Options_BackToMenuInstruction);
            Console.ReadKey();
        }
        static string GetDownloadUrl(string filepath)
        {
            const string CNHost = "https://assets.shuise.net";
            var isCN = RegionInfo.CurrentRegion.Name == "CN" || CultureInfo.CurrentUICulture.Name == "zh-CN";
            if (Resource.LaunchMenu_Update_DownloadEventsInstruction == filepath)
                return isCN ? $"{CNHost}/events.json" : "https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/events.json";
            if (Resource.LaunchMenu_Update_DownloadSuccessEventsInstruction == filepath)
                return isCN ? $"{CNHost}/successevents.json" : "https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/successevents.json";
            if (Resource.LaunchMenu_Update_DownloadIdToNameInstruction == filepath)
                return isCN ? $"{CNHost}/id.json" : "https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/id.json";
            if (Resource.LaunchMenu_Update_DownloadSkillDataInstruction == filepath)
                return isCN ? $"{CNHost}/skilldata.json" : "https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/skilldata.json";
            if (Resource.LaunchMenu_Update_DownloadTranslatedNameInstruction == filepath)
                return isCN ? $"{CNHost}/name_cn.json" : "https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/name_cn.json";
            if (Resource.LaunchMenu_Update_DownloadProgramInstruction == filepath)
                return isCN ? $"{CNHost}/UmamusumeResponseAnalyzer.exe" : "https://github.com/EtherealAO/UmamusumeResponseAnalyzer/releases/latest/download/UmamusumeResponseAnalyzer.exe";
            if (Resource.LaunchMenu_Update_DownloadClimaxItemInstruction == filepath)
                return isCN ? $"{CNHost}/climaxitems.json" : "https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master/climaxitems.json";
            throw new Exception("当前文件没有国内优化地址");
        }
        static async Task DownloadAssets(ProgressContext ctx, string instruction, string path)
        {
            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
            var task = ctx.AddTask(instruction, false);
            var response = await client.GetAsync(GetDownloadUrl(instruction), HttpCompletionOption.ResponseHeadersRead);
            while (response.StatusCode == System.Net.HttpStatusCode.MovedPermanently || response.StatusCode == System.Net.HttpStatusCode.Found)
            {
                response = await client.GetAsync(response.Headers.Location, HttpCompletionOption.ResponseHeadersRead);
            }
            task.MaxValue(response.Content.Headers.ContentLength ?? 0);
            task.StartTask();
            if (Path.GetExtension(path) == "exe" && Environment.ProcessPath != default && response.Content.Headers.GetContentMD5().SequenceEqual(MD5.HashData(File.ReadAllBytes(Environment.ProcessPath)))
                || File.Exists(path) && response.Content.Headers.ContentLength == new FileInfo(path).Length) //服务器返回的文件长度和当前文件大小一致，即没有新的可用版本，直接返回
            {
                task.Increment(response.Content.Headers.ContentLength ?? 0);
                return;
            }
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
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
    }
}