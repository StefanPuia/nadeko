using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility.RaidComp;

public partial class Utility
{
    [Group]
    public partial class RaidCompCommands : NadekoModule<RaidCompService>
    {
        private readonly IBotCredentials _creds;
        private readonly IHttpClientFactory _httpFactory;

        public RaidCompCommands(
            IBotCredentials creds,
            IHttpClientFactory httpFactory,
            IEmbedBuilderService eb,
            RaidCompService raidCompService)
        {
            _creds = creds;
            _httpFactory = httpFactory;
            _eb = eb;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Ratelimit(5)]
        public async Task RaidComp(string csvLink)
        {
            try
            {
                var buildMessage = await _service.ConvertCsv(csvLink);
                await ctx.Channel.SendConfirmAsync(_eb, buildMessage);
            }
            catch (Exception e)
            {
                await ctx.Channel.SendErrorAsync(_eb, e.Message);
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task RaiderRoleCheck(IRole role)
        {
            try
            {
                await _service.CheckRole(role, ctx.Channel, ctx.Guild);
            }
            catch (Exception e)
            {
                await ctx.Channel.SendErrorAsync(_eb, e.Message);
            }
        }
    }
}