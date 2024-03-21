using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class SingleModeEventInfo
	{
		[Key("event_id")]
		public int event_id; // 0x10
		[Key("chara_id")]
		public int chara_id; // 0x14
		[Key("story_id")]
		public int story_id; // 0x18
		[Key("play_timing")]
		public int play_timing; // 0x1C
		[Key("event_contents_info")]
		public EventContentsInfo event_contents_info; // 0x20
		[Key("succession_event_info")]
		public SingleModeSuccessionEventInfo succession_event_info; // 0x28
		[Key("minigame_result")]
		public MinigameResult minigame_result; // 0x30
        [Key("tips_training_partner_id")]
        public int tips_training_partner_id;    // 红点对应的partner序号
	}
}
