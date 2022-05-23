using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test
{
    public static class SingleModeCheckEventResponseExtension
    {
        public static SingleModeCheckEventResponse WithStoryId(this SingleModeCheckEventResponse checkEventResponse, int storyId)
        {
            if (checkEventResponse == null) throw new ArgumentNullException(nameof(checkEventResponse));
            checkEventResponse.data.unchecked_event_array = new[]
            {
                    new SingleModeEventInfo
                    {
                        story_id=storyId,
                        chara_id=0,
                        event_id=0,
                        play_timing=0,
                        succession_event_info=null!,
                        minigame_result=null!
                    }
                };
            return checkEventResponse;
        }
        public static SingleModeCheckEventResponse AddChoices(this SingleModeCheckEventResponse checkEventResponse, params int[] selectIndex)
        {
            checkEventResponse.data.unchecked_event_array[0].event_contents_info = new EventContentsInfo
            {
                choice_array = selectIndex.Select(x => new ChoiceArray { select_index = x }).ToArray()
            };
            return checkEventResponse;
        }
        public static void Run(this SingleModeCheckEventResponse checkEventResponse)
        {
            UmamusumeResponseAnalyzer.Handler.Handlers.ParseSingleModeCheckEventResponse(checkEventResponse);
        }
    }
}
