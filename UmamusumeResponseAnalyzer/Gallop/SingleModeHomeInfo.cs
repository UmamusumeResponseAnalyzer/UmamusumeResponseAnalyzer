using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class SingleModeHomeInfo
	{
		[Key("command_info_array")]
		public SingleModeCommandInfo[] command_info_array; // 0x10
		[Key("race_entry_restriction")]
		public int race_entry_restriction; // 0x18
		[Key("disable_command_id_array")]
		public int[] disable_command_id_array; // 0x20
		[Key("available_continue_num")]
		public int available_continue_num; // 0x28
		[Key("free_continue_time")]
		public long free_continue_time; // 0x30
		[Key("shortened_race_state")]
		public int shortened_race_state; // 0x38
	}
}
