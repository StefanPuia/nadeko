using NadekoBot.Common.Yml;

namespace Medusa.Christenebot.Config;

public class ChristenebotConfig
{
    [Comment(@"Configuration for RaidComp integration")]
    public RaidCompConfig RaidComp { get; set; } = new();
}

public class RaidCompConfig
{
    public string Api { get; set; }
    public string Web { get; set; }
    public ulong AutoChannel { get; set; }
    public ICollection<ulong> AllowedRaidCompBots { get; set; }
}