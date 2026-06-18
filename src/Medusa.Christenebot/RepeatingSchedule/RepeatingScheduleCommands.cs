using Discord;
using JetBrains.Annotations;
using Nadeko.Common;
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Extensions;
using NadekoBot.Medusa;

namespace Medusa.Christenebot.AutoReschedule;

[UsedImplicitly]
public class RepeatingScheduleCommands(RepeatingScheduleService service) : Snek
{
    public override async ValueTask InitializeAsync()
    {
        await service.Initialize();
        _ = service.ExecuteCommands();
    }


    [cmd("rscha",
        "repeatingscheduleadd",
        desc =
            "Schedules a command to be repeated every specified amount of time.",
        args = ["1m30s .say hello"])]
    [user_perm(GuildPermission.Administrator)]
    public async Task RepeatingScheduleAdd(GuildContext ctx, ParsedTimespan timeString,
        [leftover] string commandText)
    {
        if (timeString.Time < TimeSpan.FromMinutes(1))
        {
            await ctx.ErrorAsync();
            return;
        }

        await service.CreateScheduleAsync(ctx.User.Id,
            ctx.Message.Id,
            ctx.Channel.Id,
            ctx.Guild.Id,
            commandText,
            timeString.Time);
        await ctx.ConfirmAsync();
    }

    [cmd("rschd",
        "repeatingscheduledelete",
        desc =
            "Deletes a repeating scheduled command by its id.",
        args = ["1"])]
    [user_perm(GuildPermission.Administrator)]
    public async Task RepeatingScheduleDelete(GuildContext ctx, int id)
    {
        try
        {
            await service.DeleteScheduleAsync(id, ctx.User.Id, ctx.Guild.Id);
            await ctx.ConfirmAsync();
        }
        catch (Exception e)
        {
            await ctx.SendErrorAsync(e.Message);
        }
    }

    [cmd("rschl",
        "repeatingschedulelist",
        desc =
            "Lists all repeating schedules for the current guild.",
        args = ["1"])]
    [user_perm(GuildPermission.Administrator)]
    public async Task RepeatingScheduleList(GuildContext ctx)
    {
        var records = await service.ListScheduleAsync(ctx.User.Id, ctx.Guild.Id);

        if (records.Count == 0)
        {
            await ctx.SendConfirmAsync("No repeating schedules found.");
        }
        else
        {
            var itemsDisplay = records.Select(static x =>
                {
                    var id = $"**{x.Id}**";
                    var commandText = $"`{x.CommandText}`";
                    var delay = TimeSpan.FromMinutes(x.DelayInMinutes).ToPrettyStringHm();
                    var time =
                        $"every {delay} (next run {Utils.toRelativeDiscordTime(x.NextRunTime)})";
                    return $"{id}: {commandText} {time}";
                })
                .Join("\n");
            await ctx.SendConfirmAsync(
                $"**Repeating schedules for {ctx.User.Mention}**\n{itemsDisplay}");
        }
    }
}