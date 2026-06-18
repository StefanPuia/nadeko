using Nadeko.Common;
using NadekoBot.Common.Configs;
using NadekoBot.Medusa;
using NadekoBot.Services;

namespace Medusa.Christenebot.Config;

[svc(Lifetime.Singleton)]
public class ChristenebotConfigService : ConfigServiceBase<ChristenebotConfig>
{
    private static readonly TypedKey<ChristenebotConfig> _changeKey = new("config.bot.updated");
    public override string Name { get; } = "medusa.custom-functionality";

    public ChristenebotConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base("data/medusae/creds.Medusa.Christenebot.yml", serializer, pubSub, _changeKey)
    {
    }
}