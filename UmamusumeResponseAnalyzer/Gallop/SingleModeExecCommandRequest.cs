using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
    public class SingleModeExecCommandRequest
	{
		[Key("command_type")]
		public int command_type; // 0x70
		[Key("command_id")]
		public int command_id; // 0x74
		[Key("command_group_id")]
		public int command_group_id; // 0x78
		[Key("select_id")]
		public int select_id; // 0x7C
		[Key("current_turn")]
		public int current_turn; // 0x80
		[Key("current_vital")]
		public int current_vital; // 0x84
	}
}
