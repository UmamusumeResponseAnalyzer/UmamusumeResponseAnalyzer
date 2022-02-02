using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer
{
    public class Event
    {
        public int StoryId { get; set; }
        public Choice[] Choices { get; set; }
    }
    public class Choice
    {
        public string Id { get; set; }
        public string Effect { get; set; }
    }
}
