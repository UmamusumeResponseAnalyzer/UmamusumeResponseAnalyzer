using System.Reflection;
using Gallop;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    public class ExtensionsTests
    {
        [Fact]
        public void Replace_Hit_ReplacesMatch()
        {
            byte[] result = new byte[] { 0, 1, 2, 3 }.Replace([1, 2], [9]);

            Assert.Equal(new byte[] { 0, 9, 3 }, result);
        }

        [Fact]
        public void Replace_NoMatch_ReturnsOriginalContent()
        {
            byte[] input = [1, 2, 3, 4];
            byte[] result = input.Replace([7, 8], [9]);

            Assert.Equal(new byte[] { 1, 2, 3, 4 }, result);
        }

        [Fact]
        public void Replace_MultipleNonOverlappingHits_AllReplaced()
        {
            byte[] result = new byte[] { 1, 2, 1, 2 }.Replace([1, 2], [9]);

            Assert.Equal(new byte[] { 9, 9 }, result);
        }

        [Fact]
        public void Replace_EmptyPattern_ReturnsInputUnchanged()
        {
            byte[] input = [1, 2, 3];
            byte[] result = input.Replace([], [9]);

            Assert.Same(input, result);
        }

        [Fact]
        public void Replace_PatternLongerThanInput_ReturnsCopyOfInput()
        {
            byte[] input = [1, 2];
            byte[] result = input.Replace([1, 2, 3], [9]);

            Assert.Equal(new byte[] { 1, 2 }, result);
            Assert.NotSame(input, result);
        }

        sealed class Probe
        {
            public string Name { get; set; } = "abc";
            public int Count { get; set; } = 42;
            public string? Nothing { get; set; }
            public string Bracketed { get; set; } = "a[b]c";
            public IEnumerable<string> Tags { get; set; } = ["x", "y"];
            public IEnumerable<string> BracketTags { get; set; } = ["a[1]", "b[2]"];
        }

        static PropertyInfo Prop(string name) => typeof(Probe).GetProperty(name)!;

        [Fact]
        public void AppendValue_StringValue_NoDic_FormatsNameColonValue()
        {
            var result = Prop(nameof(Probe.Name)).AppendValue(new Probe());

            Assert.Equal("Name: abc", result);
        }

        [Fact]
        public void AppendValue_NonStringValue_UsesToString()
        {
            var result = Prop(nameof(Probe.Count)).AppendValue(new Probe());

            Assert.Equal("Count: 42", result);
        }

        [Fact]
        public void AppendValue_NullValue_YieldsEmptyValueString()
        {
            var result = Prop(nameof(Probe.Nothing)).AppendValue(new Probe());

            Assert.Equal("Nothing: ", result);
        }

        [Fact]
        public void AppendValue_ScalarBrackets_AreDoubled()
        {
            var result = Prop(nameof(Probe.Bracketed)).AppendValue(new Probe());

            Assert.Equal("Bracketed: a[[b]]c", result);
        }

        [Fact]
        public void AppendValue_StringEnumerable_JoinedByComma()
        {
            var result = Prop(nameof(Probe.Tags)).AppendValue(new Probe());

            Assert.Equal("Tags: x,y", result);
        }

        [Fact]
        public void AppendValue_StringEnumerable_EscapesBracketsPerElement()
        {
            var result = Prop(nameof(Probe.BracketTags)).AppendValue(new Probe());

            Assert.Equal("BracketTags: a[[1]],b[[2]]", result);
        }

        [Fact]
        public void AppendValue_WithTranslationDic_UsesTranslatedName()
        {
            var dic = new Dictionary<string, string> { ["Name"] = "名字" };
            var result = Prop(nameof(Probe.Name)).AppendValue(new Probe(), dic);

            Assert.Equal("名字: abc", result);
        }

        [Fact]
        public void AppendValue_WithDic_MissingKey_FallsBackToPropertyName()
        {
            var dic = new Dictionary<string, string> { ["Other"] = "别的" };
            var result = Prop(nameof(Probe.Name)).AppendValue(new Probe(), dic);

            Assert.Equal("Name: abc", result);
        }

        static SingleModeCheckEventResponse.CommonResponse StageData(int playingState, SingleModeEventInfo[]? events) => new()
        {
            chara_info = new SingleModeChara { playing_state = playingState },
            unchecked_event_array = events!,
        };

        [Fact]
        public void GetCommandInfoStage_PlayingState1_NullEvents_Returns2()
        {
            Assert.Equal(2, StageData(1, null).GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState1_EmptyEvents_Returns2()
        {
            Assert.Equal(2, StageData(1, []).GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState1_WithEvents_Returns0()
        {
            var data = StageData(1, [new SingleModeEventInfo { story_id = 1 }]);

            Assert.Equal(0, data.GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState5_BuffStory_Returns5()
        {
            var data = StageData(5, [new SingleModeEventInfo { story_id = 400010112 }]);

            Assert.Equal(5, data.GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState5_TeamCardStory_Returns3()
        {
            var data = StageData(5, [new SingleModeEventInfo { story_id = 830241003 }]);

            Assert.Equal(3, data.GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState5_OtherStory_Returns0()
        {
            var data = StageData(5, [new SingleModeEventInfo { story_id = 12345 }]);

            Assert.Equal(0, data.GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_EventOverload_UsesDataDirectly()
        {
            var response = new SingleModeCheckEventResponse
            {
                data = StageData(5, [new SingleModeEventInfo { story_id = 830241003 }]),
            };

            Assert.Equal(3, response.GetCommandInfoStage());
        }
    }
}
