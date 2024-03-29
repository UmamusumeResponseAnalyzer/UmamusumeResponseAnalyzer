﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    /// <summary>
    /// master.mdb text_data的不同category
    /// </summary>
    public enum TextDataCategory
    {
        /// <summary>
        /// 不同复制人的全名 100703 [La mode 564]ゴールドシップ；用于翻译
        /// </summary>
        UmamusumeFullName = 4,
        /// <summary>
        /// 不同复制人的称号 100703 [La mode 564]
        /// </summary>
        CostumeName = 5,
        /// <summary>
        /// 马娘本体的名字 1025 曼城茶座
        /// </summary>
        CharacterName = 6,
        /// <summary>
        /// 技能名 100041 红焰齿轮/LP1211-M
        /// </summary>
        SkillName = 47,
        /// <summary>
        /// 胜鞍id对应的比赛/勋章名
        /// </summary>
        WinSaddleName = 111,
        /// <summary>
        /// 因子名
        /// </summary>
        FactorName = 147,
        /// <summary>
        /// 事件名
        /// </summary>
        EventName = 181,
        /// <summary>
        /// 巅峰杯道具名
        /// </summary>
        ClimaxItemName = 225,
    }
}
