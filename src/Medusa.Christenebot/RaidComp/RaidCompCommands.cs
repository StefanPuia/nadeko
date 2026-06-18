using System.Text.RegularExpressions;
using Discord;
using JetBrains.Annotations;
using Medusa.Christenebot.Config;
using NadekoBot.Medusa;
using Serilog;

namespace Medusa.Christenebot.RaidComp;

[UsedImplicitly]
public sealed class RaidCompCommands(
    RaidCompService service,
    ChristenebotConfigService config) : Snek
{
    [cmd("raidcomp",
        "createbuildfromcsvlink",
        desc =
            "Generate a raid composition from a <@579155972115660803> CSV export. Use `/export <event>` to generate the CSV",
        args = ["https://some-csv-link"])]
    [user_perm(GuildPermission.SendMessages)]
    public async Task CreateBuildFromCsvLink(GuildContext ctx, [leftover] string csvLink)
    {
        try
        {
            var buildMessage = await service.ConvertCsv(csvLink);
            await ctx.Channel.SendMessageAsync(buildMessage);
        }
        catch (Exception e)
        {
            await ctx.SendErrorAsync(e.Message);
        }
    }

    public override async ValueTask<bool> ExecOnMessageAsync(IGuild guild, IUserMessage msg)
    {
        var allowedUsers = config.Data.RaidComp.AllowedRaidCompBots ?? [];
        if (!allowedUsers.Contains(msg.Author.Id) || msg.Attachments.Count != 1)
            return await ValueTask.FromResult(false);

        try
        {
            var attachmentUrl = msg.Attachments.Select(static x => x.Url)
                .Where(static x => x.Contains(".csv"))
                .FirstOrDefault(static url =>
                    Regex.Match(url, RaidCompService.CsvPattern, RegexOptions.IgnoreCase).Success);
            await service.AutoConvertCsv(guild, attachmentUrl);
        }
        catch (Exception e)
        {
            Log.Error(e, "Error processing CSV");
            await msg.Channel.SendMessageAsync(e.Message);
        }

        return await ValueTask.FromResult(false);
    }
}