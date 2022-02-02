using MessagePack;
using System;

namespace Gallop
{
	[MessagePackObject]
	public sealed class SingleModeCheckEventResponse : ResponseCommon
	{
		[Key("data")]
		public CommonResponse data;

		[MessagePackObject]
		public class CommonResponse
		{
			[Key("chara_info")]
			public SingleModeChara chara_info;
			[Key("not_up_parameter_info")]
			public NotUpParameterInfo not_up_parameter_info;
			[Key("not_down_parameter_info")]
			public NotDownParameterInfo not_down_parameter_info;
			[Key("home_info")]
			public SingleModeHomeInfo home_info;
			[Key("unchecked_event_array")]
			public SingleModeEventInfo[] unchecked_event_array;
			[Key("event_effected_factor_array")]
			public SuccessionEffectedFactor[] event_effected_factor_array;
			[Key("race_condition_array")]
			public SingleModeRaceCondition[] race_condition_array;
			[Key("race_start_info")]
			public SingleRaceStartInfo race_start_info;
		}
	}
}
