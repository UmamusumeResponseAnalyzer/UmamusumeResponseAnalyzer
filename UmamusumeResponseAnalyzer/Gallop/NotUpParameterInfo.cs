using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class NotUpParameterInfo
	{
		[Key("status_type_array")]
		public int[] status_type_array; // 0x10
		[Key("chara_effect_id_array")]
		public int[] chara_effect_id_array; // 0x18
		[Key("skill_id_array")]
		public int[] skill_id_array; // 0x20
		[Key("skill_tips_array")]
		public SkillTips[] skill_tips_array; // 0x28
		[Key("skill_lv_id_array")]
		public int[] skill_lv_id_array; // 0x30
		[Key("evaluation_chara_id_array")]
		public int[] evaluation_chara_id_array; // 0x38
		[Key("command_lv_array")]
		public int[] command_lv_array; // 0x40
		[Key("has_chara_effect_id_array")]
		public int[] has_chara_effect_id_array; // 0x48
		[Key("unsupported_evaluation_chara_id_array")]
		public int[] unsupported_evaluation_chara_id_array; // 0x50
		[Key("not_gain_chara_effect_array")]
		public NotGainCharaEffect[] not_gain_chara_effect_array; // 0x58
	}
	[MessagePackObject]
	public class NotGainCharaEffect // TypeDefIndex: 8147
	{
		[Key("effected_chara_effect_id")]
		public int effected_chara_effect_id; // 0x10
		[Key("not_gain_chara_effect_id")]
		public int not_gain_chara_effect_id; // 0x14
	}
}
