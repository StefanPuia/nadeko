#nullable disable
namespace NadekoBot;

public interface IBotCredentials
{
    string Token { get; }
    string GoogleApiKey { get; }
    ICollection<ulong> OwnerIds { get; }
    bool UsePrivilegedIntents { get; }
    string RapidApiKey { get; }

    Creds.DbOptions Db { get; }
    string OsuApiKey { get; }
    int TotalShards { get; }
    Creds.PatreonSettings Patreon { get; }
    string CleverbotApiKey { get; }
    RestartConfig RestartCommand { get; }
    Creds.VotesSettings Votes { get; }
    string BotListToken { get; }
    string RedisOptions { get; }
    string LocationIqApiKey { get; }
    string TimezoneDbApiKey { get; }
    string CoinmarketcapApiKey { get; }
    string TrovoClientId { get; }
    string CoordinatorUrl { get; set; }
    string TwitchClientId { get; set; }
    string TwitchClientSecret { get; set; }
    string NetStatUrl { get; set; }
    RaidCompConfig RaidComp { get; set; }
}

public class RestartConfig
{
    public string Cmd { get; set; }
    public string Args { get; set; }
}

public class RaidCompConfig
{
    public string Api { get; set; }
    public string Web { get; set; }
    public ulong AutoChannel { get; set; }
    public ICollection<ulong> AllowedRaidCompBots { get; set; }
}