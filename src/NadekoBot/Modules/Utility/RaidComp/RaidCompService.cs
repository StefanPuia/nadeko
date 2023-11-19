#nullable disable
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Utility.RaidComp;
using Newtonsoft.Json;
using System.Text;
using System.Web;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Utility.Services;

public class RaidCompService : IExecOnMessage, INService
{
    private static readonly string CSV_PATTERN = @".+csv\?|$";
    private readonly IBotCredentials _creds;
    private readonly DiscordSocketClient _client;
    private readonly IEmbedBuilderService _eb;
    private readonly IHttpClientFactory _httpFactory;

    public RaidCompService(
        IBotCredentials creds,
        IHttpClientFactory factory,
        IEmbedBuilderService eb,
        DiscordSocketClient client)
    {
        _creds = creds;
        _httpFactory = factory;
        _eb = eb;
        _client = client;
    }

    public int Priority => -1;
    public bool AllowBots => true;

    public async Task<bool> ExecOnMessageAsync(IGuild guild, IUserMessage msg)
    {
        if (!_creds.RaidComp.AllowedRaidCompBots.Contains(msg.Author.Id) || msg.Attachments.Count != 1) return false;
        try
        {
            var channel = _client.GetChannel(_creds.RaidComp.AutoChannel);
            var attachmentUrl = msg.Attachments.First().Url;
            var csvUrlMatch = Regex.Match(attachmentUrl, CSV_PATTERN, RegexOptions.IgnoreCase);
            if (csvUrlMatch.Success)
            {
                var buildString = await ConvertCsv(attachmentUrl);
                await ((ITextChannel)channel).SendMessageAsync(buildString);
            }
        }
        catch (Exception e)
        {
            await msg.Channel.SendErrorAsync(_eb, e.Message);
        }

        return false;
    }

    public async Task<string> ConvertCsv(string csvLink)
    {
        if (string.IsNullOrEmpty(csvLink))
        {
            throw new Exception("There was an error processing the CSV");
        }

        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            var csvContent = await http.GetStringAsync(csvLink).ConfigureAwait(false);

            var payload = JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                {"raw", csvContent}
            });

            var importUrl = $"{_creds.RaidComp.Api}/build/import/raid-helper";
            var response =
                await http.PostAsync(importUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception("There was an error generating the build: " + responseContent);

            var builds = JsonConvert.DeserializeObject<RaidCompResult>(responseContent);
            var buildLinks = builds
                             ?.Builds.Select(build =>
                                 $"{_creds.RaidComp.Web}/build/{build.BuildId}/{HttpUtility.UrlEncode(build.BuildName)}")
                             .ToList()
                             ?? new List<string>();

            return string.Join("\n", buildLinks);

        }
        catch (Exception e)
        {
            Log.Error(e, "There was an error processing the CSV");
            throw new Exception("There was an error processing the CSV");
        }
    }

    public async Task CheckRole(
        IRole role,
        IMessageChannel channel,
        IGuild guild)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            var roleId = role.Id;
            var channelId = channel.Id;
            var guildId = guild.Id;
            var roleCheckUrl =
                $"{_creds.RaidComp.Api}/discord/role-check?requiredRole={roleId}&channelId={channelId}&guildId={guildId}";
            await http.GetAsync(roleCheckUrl);
        }
        catch (Exception e)
        {
            Log.Error(e, "There was an error when doing the role check");
            throw new Exception("There was an error when doing the role check");
        }
    }
}