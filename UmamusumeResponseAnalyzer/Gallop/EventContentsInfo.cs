using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class EventContentsInfo
	{
		[Key("support_card_id")]
		public int support_card_id; // 0x10
		[Key("show_clear")]
		public int show_clear; // 0x14
		[Key("show_clear_sort_id")]
		public int show_clear_sort_id; // 0x18
		[Key("choice_array")]
		public ChoiceArray[] choice_array; // 0x20
		[Key("tips_training_partner_id")]
		public int tips_training_partner_id; // 0x28
	}
}
