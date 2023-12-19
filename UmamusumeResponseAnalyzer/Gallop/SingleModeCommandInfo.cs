using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class SingleModeCommandInfo
	{
		[Key("command_type")]
		public int command_type; // 0x10
		[Key("command_id")]
		public int command_id; // 0x14
		[Key("is_enable")]
		public int is_enable; // 0x18
		[Key("training_partner_array")]
		public int[] training_partner_array; // 0x20
		[Key("tips_event_partner_array")]
		public int[] tips_event_partner_array; // 0x28
		[Key("params_inc_dec_info_array")]
		public SingleModeParamsIncDecInfo[] params_inc_dec_info_array; // 0x30
		[Key("failure_rate")]
		public int failure_rate; // 0x38
		[Key("level")]
		public int level;
	}
}
