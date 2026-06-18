using System.Text;
using System.Web;
using Discord;
using Discord.WebSocket;
using Medusa.Christenebot.Config;
using NadekoBot.Medusa;
using Newtonsoft.Json;
using Serilog;

namespace Medusa.Christenebot.RaidComp;

[svc(Lifetime.Singleton)]
public class RaidCompService(
    ChristenebotConfigService config,
    IHttpClientFactory httpFactory,
    DiscordSocketClient client)
{
    public static readonly string CsvPattern = @".+csv\?|$";

    public async Task<string> ConvertCsv(string csvLink)
    {
        if (string.IsNullOrEmpty(csvLink))
        {
            throw new("There was an error processing the CSV");
        }

        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            var csvContent = await http.GetStringAsync(csvLink).ConfigureAwait(false);

            var payload = JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                {"raw", csvContent}
            });

            var importUrl = $"{config.Data.RaidComp.Api}/build/import/raid-helper";
            Log.Information("Sending to: {ImportUrl}", importUrl);
            var response =
                await http.PostAsync(importUrl,
                    new StringContent(payload, Encoding.UTF8, "application/json"));
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new("There was an error generating the build: " + responseContent);

            var builds = JsonConvert.DeserializeObject<RaidCompResult>(responseContent);
            var buildLinks = builds
                                 ?.Builds.Select(build =>
                                     $"{config.Data.RaidComp.Web}/build/{build.BuildId}/{HttpUtility.UrlEncode(build.BuildName)}")
                                 .ToList()
                             ?? new List<string>();

            return string.Join("\n", buildLinks);
        }
        catch (Exception e)
        {
            Log.Error(e, "There was an error processing the CSV");
            throw new("There was an error processing the CSV");
        }
    }

    public async Task AutoConvertCsv(IGuild guild, string attachmentUrl)
    {
        var channel =
            client.GetGuild(guild.Id)?.GetChannel(config.Data.RaidComp.AutoChannel) as
                ISocketMessageChannel;
        if (channel is null)
        {
            Log.Warning("RaidComp AutoChannel not found");
            return;
        }

        if (attachmentUrl != null)
        {
            var buildString = await ConvertCsv(attachmentUrl);
            await ((ITextChannel) channel).SendMessageAsync(buildString);
        }
    }
}