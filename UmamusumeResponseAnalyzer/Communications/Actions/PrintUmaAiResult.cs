using Spectre.Console;
using System.Diagnostics;
using System.Text;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.Communications.Actions
{
    public class PrintUmaAiResult(string text) : ICommand
    {
        public CommandType CommandType => CommandType.Action;
        string Text { get; } = text;

        public string fmtScore(string prefix, double x, double pivot)
        {
            var text = prefix + x.ToString("+#;-#;0");
            if (x < -10000 || x > 10000)
                text = $"[grey]{prefix}----[/]";
            else
            {
                if (x - pivot > -20)
                    text = $"[yellow on red]{text}[/]";
                else if (x - pivot > -100)
                    text = $"[green]{text}[/]";
                else if (x - pivot < -300)
                    text = $"[grey]{text}[/]";
            }
            return text;
        }

        public string fmtLuck(double x)
        {
            var text = x.ToString("+#;-#;0");
            var sigma = 1500; // 标准差?
            if (x < -10000 || x > 10000)
                text = "[grey]----[/]";
            else
            {
                if (x > sigma)
                    text = $"[lightgreen]{text}[/]";
                else if (x > 0)
                    text = $"[green]{text}[/]";
                else if (x > -sigma)
                    text = $"[yellow]{text}[/]";
                else
                    text = $"[red]{text}[/]";
            }
            return text;
        }
        /*
        public void printLuckChart(double roundLuck, double gameLuck, double scoreMean, double optScore)
        {
            var baseBar = 3000.0;
            if (roundLuck > 10000 || roundLuck < -10000) return;

            var text = new Markup($"评分预测: [green]{Math.Round(scoreMean)}[/] (乐观[cyan]+{Math.Round(optScore - scoreMean)}[/]) | 运气: 本局 {this.fmtLuck(gameLuck)} | 本回合 {this.fmtLuck(roundLuck)}");
            var gameBar = gameLuck;
            var gameBarColor = Color.LightGreen;
            var roundBar = roundLuck;
            var roundBarColor = Color.Yellow;
            var roundText = "本回合";
            var gameText = "本局";
            if (roundLuck < 0)
            {
                roundBarColor = Color.Red;
                roundText = "本回合 -";
                roundBar = -roundLuck;
                gameBar -= roundBar;
            }
            if (gameBar < 0)
            {
                gameBarColor = Color.Green;
                gameText = "本局 -";
                gameBar = -gameBar;
                baseBar -= gameBar;
            }
            var padding = 6000.0 - baseBar - gameBar - roundBar;

            var chart = new BreakdownChart()
                .Width(80)
                .AddItem("", baseBar, Color.Grey)
                .AddItem(gameText, gameBar, gameBarColor)
                .AddItem(roundText, roundBar, roundBarColor)
                .AddItem("", padding, Color.Black);
            var panel = new Panel(
                new Rows([text, chart])
            );
            
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine("");
        }
        */
        public WSResponse? Execute()
        {
            var parts = Text.Split(); // 分割字符串

            var scenarioName = parts[0];
            if (scenarioName == "larc")
            {
                var turn = int.Parse(parts[1]); // 回合数
                if (turn != GameStats.currentTurn - 1)
                    return null;//不是当前回合的计算结果，可能是上个回合还没算完就来到了下一个回合

                var scoreMean = float.Parse(parts[2]);
                var scoreFirstTurn = float.Parse(parts[3]);
                var scoreLastTurn = float.Parse(parts[4]);
                var optimisticScore = float.Parse(parts[5]);
                if (turn == 0 || scoreFirstTurn == 0)
                    AnsiConsole.MarkupLine($"预测分数:[cyan]{(int)Math.Round(scoreMean)}[/](乐观:+[cyan]{(int)Math.Round(optimisticScore - scoreMean)}[/])");
                else
                    AnsiConsole.MarkupLine($"本局运气:[cyan]{(int)Math.Round(scoreMean - scoreFirstTurn)}[/] 本回合运气:[cyan]{(int)Math.Round(scoreMean - scoreLastTurn)}[/] 预测分数:[cyan]{(int)Math.Round(scoreMean)}[/](乐观:+[cyan]{(int)Math.Round(optimisticScore - scoreMean)}[/])");

                var buyBuffChoiceNum = turn switch
                {
                    <= 35 or (>= 43 and <= 59) => 1,
                    41 => 2,
                    (>= 36 and <= 39) or >= 60 => 4,
                    _ => -1
                };

                var values = new float[10 * buyBuffChoiceNum];
                for (var i = 0; i < 10 * buyBuffChoiceNum; i++)
                {
                    values[i] = float.Parse(parts[i + 6]); // 解析并保存浮点数
                }

                var bestValue = values.Max();
                var restValue = Math.Max(Math.Max(values[6], values[7]), values[8]);

                var prefix = new string[] { "速:", "耐:", "力:", "根:", "智:", "| SS: ", "| 休息: ", "友人: ", "普通外出: ", "比赛: " };

                for (var buybuff = 0; buybuff < buyBuffChoiceNum; buybuff++)
                {
                    var toPrint = new StringBuilder();
                    if (buyBuffChoiceNum > 1 && buybuff == 0)
                        toPrint.Append("不买:              ");
                    if (buybuff == 1)
                        toPrint.Append("买+50%:            ");
                    if (buybuff == 2 && turn < 50)
                        toPrint.Append("买pt+10:           ");
                    if (buybuff == 2 && turn >= 50)
                        toPrint.Append("买体力-20%:        ");
                    if (buybuff == 3 && turn < 50)
                        toPrint.Append("买 +50%与pt+10:     ");
                    if (buybuff == 3 && turn >= 50)
                        toPrint.Append("买+50%与体力-20%:  ");
                    for (var i = 0; i < 10; i++)
                    {
                        var v = values[buybuff * 10 + i];
                        var dif = bestValue - v;
                        v -= restValue;
                        if (dif < 20)
                            toPrint.Append("[cyan on #c00000]");
                        else if (dif < 100)
                            toPrint.Append("[green]");
                        else if (v < -50000)
                            toPrint.Append("[white]");
                        else
                            toPrint.Append("[yellow]");

                        toPrint.Append(prefix[i]);
                        if (v < -50000)
                            toPrint.Append($"{"---",4}");
                        else
                            toPrint.Append($"{(int)Math.Round(v),4}");
                        toPrint.Append("[/] ");
                    }
                    AnsiConsole.MarkupLine(toPrint.ToString());
                }
            }
            else if (scenarioName == "UAF")
            {
                //Console.WriteLine(Text);
                var turn = int.Parse(parts[1]); // 回合数
                if (turn != GameStats.currentTurn - 1)
                    return null;//不是当前回合的计算结果，可能是上个回合还没算完就来到了下一个回合
                var scoreMean = float.Parse(parts[2]);
                var scoreFirstTurn = float.Parse(parts[3]);
                var scoreLastTurn = float.Parse(parts[4]);
                var optimisticScore = float.Parse(parts[5]);
                if (turn == 0 || scoreFirstTurn == 0)
                    AnsiConsole.MarkupLine($"评分预测:[green]{(int)Math.Round(scoreMean)}[/](乐观:+[cyan]{(int)Math.Round(optimisticScore - scoreMean)}[/])");
                else
                    AnsiConsole.MarkupLine($"运气指标 | 本局:[cyan]{(int)Math.Round(scoreMean - scoreFirstTurn)}[/] | 本回合:[cyan]{(int)Math.Round(scoreMean - scoreLastTurn)}[/] | 评分预测:[cyan]{(int)Math.Round(scoreMean)}[/](乐观:+[cyan]{(int)Math.Round(optimisticScore - scoreMean)}[/])");
                var regularprefix = new string[] { "速：", "耐：", "力：", "根：", "智：", "| 休息：", "外出：", "比赛：" };
                var xiangtanprefix = new string[] {
                    "    无",
                    "[aqua]蓝[/]->[red]红[/]",
                    "[aqua]蓝[/]->[yellow]黄[/]",
                    "[red]红[/]->[aqua]蓝[/]",
                    "[red]红[/]->[yellow]黄[/]",
                    "[yellow]黄[/]->[aqua]蓝[/]",
                    "[yellow]黄[/]->[red]红[/]",
                    "[aqua]  全蓝[/]",
                    "[red]  全红[/]",
                    "[yellow]  全黄[/]" };

                var totxtcount = int.Parse(parts[6]);
                var totpointer = 6;
                for (var i = 0; i < totxtcount; i++)
                {
                    var outstring = "| ";
                    totpointer++;

                    outstring += xiangtanprefix[int.Parse(parts[totpointer])] + "  ";
                    totpointer++;
                    var sheshicount = int.Parse(parts[totpointer]);
                    for (var j = 0; j < sheshicount; j++)
                    {
                        totpointer++;
                        var sheshiid = int.Parse(parts[totpointer]);
                        outstring += regularprefix[sheshiid];
                        totpointer++;
                        float trainbias = (int)Math.Round(float.Parse(parts[totpointer]));
                        totpointer++;
                        float traincol = (int)Math.Round(float.Parse(parts[totpointer]));
                        if (trainbias < -5000)
                        {
                            outstring += "---- ";
                        }
                        else
                        {
                            if (traincol - trainbias < 30)
                            {
                                outstring += $"[yellow on red]{$"*{trainbias}",-5}[/]";
                            }
                            else if (traincol - trainbias < 150)
                            {
                                outstring += $"[green]{$"{trainbias}",-5}[/]";
                            }
                            else
                            {
                                outstring += $"[gray]{$"{trainbias}",-5}[/]";
                            }
                        }

                        if (sheshiid == 7 && turn >= 14 && turn <= 72)
                        {
                            outstring = $"[yellow]自选比赛亏损：{traincol - trainbias}[/]" + Environment.NewLine + outstring;
                        }

                    }
                    AnsiConsole.MarkupLine(outstring);
                }
            }
            else
            {
                //AnsiConsole.WriteLine($"{Text}");
                var turn = int.Parse(parts[1]); // 回合数
                if (turn != GameStats.currentTurn - 1)
                    return null;//不是当前回合的计算结果，可能是上个回合还没算完就来到了下一个回合
                var scoreMean = float.Parse(parts[2]);
                var scoreFirstTurn = float.Parse(parts[3]);
                var scoreLastTurn = float.Parse(parts[4]);
                var optimisticScore = float.Parse(parts[5]);
                var luck = scoreMean - scoreFirstTurn;
                var luckRound = scoreMean - scoreLastTurn;

                AnsiConsole.MarkupLine($"运气: 本局 {this.fmtLuck(luck)} | 本回合 {this.fmtLuck(luckRound)} | 评分预测: [green]{Math.Round(scoreMean)}[/] (乐观[cyan]+{Math.Round(optimisticScore - scoreMean)}[/])");
                //this.printLuckChart(luckRound, luck, scoreMean, optimisticScore - scoreMean);
                AnsiConsole.WriteLine("----");
                // 6-29: 训练
                var regularprefix = new string[] { "速：", "耐：", "力：", "根：", "智：", "| 休息：", "外出：", "比赛：" };
                var i = 0;
                var bestTrain = 0.0;
                for (i=0; i<8; ++i)
                {
                    var atTrain = Int32.Parse(parts[6 + i * 3]);
                    var traincol = Double.Parse(parts[6 + i * 3 + 1]);
                    bestTrain = Double.Parse(parts[6 + i * 3 + 2]);
                    AnsiConsole.Markup($"{this.fmtScore(regularprefix[atTrain], traincol, bestTrain)}  ");
                    if (i==7)
                    {
                        AnsiConsole.Markup($" {this.fmtScore("自选比赛损失：", Math.Round(traincol - bestTrain), -50)}");
                    }
                }
                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine("----");
                // >=30: 种田杯额外信息
                i = 30;
                while (i < parts.Length)
                {
                    var op = Int32.Parse(parts[i]);
                    if (op >= 100)
                    {
                        // 升级田
                        var which = op - 100;
                        var lv = Int32.Parse(parts[i + 1]);
                        i += 2;
                        AnsiConsole.MarkupLine("");
                        AnsiConsole.MarkupLine($"[lightgreen]升级田 -> {regularprefix[which]} Lv {lv}[/]");
                    }
                    else if (op >= 8)
                    {
                        var which = GameGlobal.CookDishIdUmaAI.First(kv => kv.Value == op - 7).Key;
                        var score = Double.Parse(parts[i + 1]);
                        i += 2;
                        AnsiConsole.Markup($"{this.fmtScore(GameGlobal.CookDishName[which] + ": ", score, bestTrain)}  ");
                        if ((i-3) % 10 < 2)
                            AnsiConsole.MarkupLine("");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Invalid op: {op}[/]");
                    }
                } // while
                AnsiConsole.MarkupLine("");
            }
            return null;
        }
    }
}