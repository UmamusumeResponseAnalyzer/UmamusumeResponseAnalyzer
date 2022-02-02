using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class MinigameResultDetail
	{
		[Key("get_id")]
		public int get_id; // 0x10
		[Key("chara_id")]
		public int chara_id; // 0x14
		[Key("dress_id")]
		public int dress_id; // 0x18
		[Key("motion")]
		public string motion; // 0x20
		[Key("face")]
		public string face; // 0x28
	}
}
