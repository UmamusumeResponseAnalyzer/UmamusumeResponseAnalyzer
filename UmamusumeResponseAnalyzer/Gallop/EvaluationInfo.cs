using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class EvaluationInfo
	{
		[Key("target_id")]
		public int target_id; // 0x10
		[Key("training_partner_id")]
		public int training_partner_id;
        [Key("evaluation")]
		public int evaluation; // 0x14
		[Key("is_outing")]
		public int is_outing; // 0x18
		[Key("story_step")]
		public int story_step; // 0x1C
		[Key("is_appear")]
		public int is_appear; // 0x20
		[Key("group_outing_info_array")]
		public GroupOutingInfo[] group_outing_info_array;
	}
	[MessagePackObject]
	public class GroupOutingInfo // TypeDefIndex: 8112
	{
		[Key("chara_id")]
		public int chara_id; // 0x10
		[Key("is_outing")]
		public int is_outing; // 0x14
		[Key("story_step")]
		public int story_step; // 0x18
	}
}
