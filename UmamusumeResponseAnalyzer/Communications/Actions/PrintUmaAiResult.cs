﻿using Spectre.Console;
using System.Diagnostics;
using System.Text;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.Communications.Actions
{
    public class PrintUmaAiResult(string text) : ICommand
    {
        public CommandType CommandType => CommandType.Action;
        string Text { get; } = text;

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

            return null;
        }
    }
}