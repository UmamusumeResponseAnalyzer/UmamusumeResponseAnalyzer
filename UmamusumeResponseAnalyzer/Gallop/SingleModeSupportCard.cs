using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class SingleModeSupportCard
	{
		[Key("position")]
		public int position; // 0x10
		[Key("support_card_id")]
		public int support_card_id; // 0x14
		[Key("limit_break_count")]
		public int limit_break_count; // 0x18
		[Key("exp")]
		public int exp; // 0x1C
		[Key("training_partner_state")]
		public int training_partner_state; // 0x20
		[Key("owner_viewer_id")]
		public long owner_viewer_id; // 0x28
	}
}
