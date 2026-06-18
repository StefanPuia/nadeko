using Discord;
using JetBrains.Annotations;
using NadekoBot.Medusa;
using NadekoBot.Modules.Administration.Services;

namespace Medusa.Christenebot.Prune;

[UsedImplicitly]
public class PruneOldCommands([inject] PruneService service) : Snek
{
    [cmd("pruneold",
        desc =
            "Prune `x` messages if they are older than `y` minutes. You can use the `-s` / `--safe` parameter at the end to only prune messages that are not pinned.",
        args = ["x y [-s/--safe]", "50 10", "50 10 -s", "50 10 --safe"])]
    [user_perm(GuildPermission.SendMessages)]
    public async Task PruneOld(GuildContext ctx, int count = -1, int ageMinutes = 0,
        string parameter = null)
    {
        var currentCount = count + 1;
        switch (currentCount)
        {
            case < 1:
                return;
            case > 1000:
                currentCount = 1000;
                break;
        }

        await service.PruneWhere(ctx.User.Id,
                ctx.Channel,
                currentCount,
                message =>
                {
                    var pinned = false;
                    if (parameter is "-s" or "--safe")
                    {
                        pinned = message.IsPinned;
                    }

                    var isOld = message.Timestamp.AddMinutes(ageMinutes) < DateTimeOffset.Now;

                    return !pinned && isOld;
                },
                new Progress<(int deleted, int total)>())
            .ConfigureAwait(false);
        await ctx.Message.DeleteAsync().ConfigureAwait(false);
    }
}