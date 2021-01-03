using Discord;
using Discord.WebSocket;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Core.Services;
using NadekoBot.Core.Services.Database.Models;
using NadekoBot.Extensions;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NadekoBot.Core.Modules.Utility.Services
{
    class RaidCompService : IEarlyBehavior, INService
    {
        private readonly Logger _log;
        private readonly IBotCredentials _creds;
        private readonly IHttpClientFactory _httpFactory;
        private readonly DiscordSocketClient _client;
        public int Priority => -1;

        public ModuleBehaviorType BehaviorType => ModuleBehaviorType.Executor;
        public bool AllowBots => true;

        public RaidCompService(DiscordSocketClient client, IBotCredentials creds, IHttpClientFactory factory)
        {
            this._log = LogManager.GetCurrentClassLogger();
            this._creds = creds;
            this._httpFactory = factory;
            this._client = client;
        }

        public async Task<bool> RunBehavior(DiscordSocketClient client, IGuild guild, IUserMessage msg)
        {
            if (msg.Author.Id == 579155972115660803 && msg.Attachments.Count == 1)
            {
                try
                {
                    var channel = _client.GetChannel(_creds.RaidComp.AutoChannel);
                    string attachmentURL = msg.Attachments.First().Url;
                    if (attachmentURL.ToLowerInvariant().EndsWith(".csv"))
                    {
                        string buildString = await ConvertCSV(attachmentURL, true, _creds, _httpFactory, _log);
                        buildString += "\n\n" + await ConvertCSV(attachmentURL, false, _creds, _httpFactory, _log);
                        await IMessageChannelExtensions.SendConfirmAsync((IMessageChannel)channel, buildString);
                    }
                }
                catch (Exception e)
                {
                    await IMessageChannelExtensions.SendErrorAsync(msg.Channel, e.Message);
                }
            }
            return false;
        }

        public static async Task<string> ConvertCSV(string csvLink, bool useTeams, IBotCredentials _creds, IHttpClientFactory _httpFactory, Logger _log)
        {
            if (string.IsNullOrEmpty(csvLink))
            {
                throw new Exception("There was an error processing the CSV.");
            }

            try
            {
                using var http = _httpFactory.CreateClient();
                string csvContent = await http.GetStringAsync(csvLink).ConfigureAwait(false);

                var payload = JsonConvert.SerializeObject(new Dictionary<string, string> {
                        { "raw", csvContent }
                    });

                string importURL = $"{_creds.RaidComp.API}/build/import/raid-helper";
                if (useTeams)
                {
                    importURL = $"{importURL}/teams";
                }
                var response = await http.PostAsync(importURL, new StringContent(payload, Encoding.UTF8, "application/json"));
                if (response.IsSuccessStatusCode)
                {
                    List<string> buildLinks = new List<string>();
                    var builds = JsonConvert.DeserializeObject<RaidCompResult>(await response.Content.ReadAsStringAsync());
                    foreach (RaidCompResultBuild build in builds.builds)
                    {
                        buildLinks.Add($"{_creds.RaidComp.WEB}/build/{build.buildId}/{build.buildName}");
                    }
                    return string.Join("\n", buildLinks);
                }
                else
                {
                    throw new Exception("There was an error generating the build.");
                }
            }
            catch (Exception e)
            {
                _log.Error(e);
                throw new Exception("There was an error processing the CSV.");
            }
        }

        public static async Task WowAuditRefresh(IBotCredentials _creds, IHttpClientFactory _httpFactory, Logger _log)
        {
            try
            {
                using var http = _httpFactory.CreateClient();
                var payload = JsonConvert.SerializeObject(new Dictionary<string, string> {
                        { "key",  _creds.RaidComp.WowAuditKey }
                    });

                string updateURL = $"{_creds.RaidComp.API}/wowaudit/update";
                var response = await http.PostAsync(updateURL, new StringContent(payload, Encoding.UTF8, "application/json"));
                if (!response.IsSuccessStatusCode)
                {
                    _log.Error(response.ToString());
                    throw new Exception("There was an error running the update.");
                }
            }
            catch (Exception e)
            {
                _log.Error(e);
                throw new Exception("There was an error running the update. Try again later.");
            }
        }
    }
}
