using System.Collections.Generic;

namespace NadekoBot.Core.Services.Database.Models
{
    public class RaidCompResult
    {
        public RaidCompResultBuild[] builds { get; set; }
    }
    public class RaidCompResultBuild
    {
        public string team { get; set; }
        public string buildId { get; set; }
        public string buildName { get; set; }
    }
}
