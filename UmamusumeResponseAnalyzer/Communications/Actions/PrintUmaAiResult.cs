using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.Communications.Actions
{
    public class PrintUmaAiResult : ICommand
    {
        public CommandType CommandType => CommandType.Action;
        string Text { get; init; }

        public PrintUmaAiResult(string text) => Text = text;
        public WSResponse? Execute()
        {
            string[] parts = Text.Split(); // 分割字符串

            string scenarioName = parts[0];
            if (scenarioName == "larc")
            {

                int turn = int.Parse(parts[1]); // 回合数
                if (turn != GameStats.currentTurn - 1)
                    return null;//不是当前回合的计算结果，可能是上个回合还没算完就来到了下一个回合

                float scoreMean = float.Parse(parts[2]);
                float scoreFirstTurn = float.Parse(parts[3]);
                float scoreLastTurn = float.Parse(parts[4]);
                float optimisticScore = float.Parse(parts[5]);
                if (turn == 0 || scoreFirstTurn == 0)
                    AnsiConsole.MarkupLine($"预测分数:[cyan]{(int)Math.Round(scoreMean)}[/](乐观:+[cyan]{(int)Math.Round(optimisticScore - scoreMean)}[/])");
                else
                    AnsiConsole.MarkupLine($"本局运气:[cyan]{(int)Math.Round(scoreMean - scoreFirstTurn)}[/] 本回合运气:[cyan]{(int)Math.Round(scoreMean - scoreLastTurn)}[/] 预测分数:[cyan]{(int)Math.Round(scoreMean)}[/](乐观:+[cyan]{(int)Math.Round(optimisticScore - scoreMean)}[/])");

                int buyBuffChoiceNum = (turn <= 35 || (43 <= turn && turn <= 59)) ? 1 :
                        turn == 41 ? 2 :
                        ((36 <= turn && turn <= 39) || turn >= 60) ? 4 :
                        -1;

                Debug.Assert(parts.Length == 6 + 10 * buyBuffChoiceNum);

                float[] values = new float[10 * buyBuffChoiceNum];

                for (int i = 0; i < 10 * buyBuffChoiceNum; i++)
                {
                    values[i] = float.Parse(parts[i + 6]); // 解析并保存浮点数
                }


                float bestValue = values.Max();
                float restValue = Math.Max(Math.Max(values[6], values[7]), values[8]);

                string[] prefix =  { "速:", "耐:", "力:", "根:", "智:", "| SS: ", "| 休息: ", "友人: ", "普通外出: ", "比赛: " };

                for (int buybuff = 0; buybuff < buyBuffChoiceNum; buybuff++)
                {
                    string toprint = "";
                    if (buyBuffChoiceNum > 1 && buybuff == 0)
                        toprint += "不买:              ";
                    if (buybuff == 1)
                        toprint += "买+50%:            ";
                    if (buybuff == 2 && turn < 50)
                        toprint += "买pt+10:           ";
                    if (buybuff == 2 && turn >= 50)
                        toprint += "买体力-20%:        ";
                    if (buybuff == 3 && turn < 50)
                        toprint += "买+50%与pt+10:     ";
                    if (buybuff == 3 && turn >= 50)
                        toprint += "买+50%与体力-20%:  ";
                    for (int i = 0; i < 10; i++)
                    {
                        float v = values[buybuff * 10 + i];
                        float dif = bestValue - v;
                        v = v - restValue;
                        if (dif < 20)
                            toprint += "[cyan on #c00000]";
                        else if (dif < 100)
                            toprint += "[green]";
                        else if(v < -50000)
                            toprint += "[white]";
                        else
                            toprint += "[yellow]";

                        toprint += prefix[i];
                        if (v < -50000)
                            toprint += $"{"---",4}";
                        else
                            toprint += $"{(int)Math.Round(v),4}";
                        toprint += "[/] ";
                    }
                    AnsiConsole.MarkupLine(toprint);
                }

            }

            return null;
        }
    }
}
