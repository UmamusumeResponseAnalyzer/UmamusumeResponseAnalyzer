using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class SingleRaceStartInfo
	{
		[Key("program_id")]
		public int program_id; // 0x10
		[Key("random_seed")]
		public int random_seed; // 0x14
		[Key("weather")]
		public int weather; // 0x18
		[Key("ground_condition")]
		public int ground_condition; // 0x1C
		[Key("race_horse_data")]
		public RaceHorseData[] race_horse_data; // 0x20
		[Key("continue_num")]
		public int continue_num; // 0x28
	}
}
