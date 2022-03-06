using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility.Maintenance;

public partial class Utility
{
    [Group]
    public partial class MaintenanceCommands : NadekoModule<MaintenanceService>
    {
        [Cmd]
        [UserPerm(GuildPerm.Administrator)]
        public async partial Task NetStat() => await _service.NetStat(ctx.Channel);
    }
}