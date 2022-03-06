using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility.RaidComp;

public partial class Utility
{
    public partial class RaidCompCommands : NadekoModule<RaidCompService>
    {
        private readonly IBotCredentials _creds;
        private readonly IHttpClientFactory _httpFactory;
        private readonly RaidCompService _raidCompService;

        public RaidCompCommands(
            IBotCredentials creds,
            IHttpClientFactory httpFactory,
            IEmbedBuilderService eb,
            RaidCompService raidCompService)
        {
            _creds = creds;
            _httpFactory = httpFactory;
            _raidCompService = raidCompService;
            _eb = eb;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Ratelimit(5)]
        public async partial Task RaidComp(string csvLink)
        {
            try
            {
                var buildMessage = await _raidCompService.ConvertCsv(csvLink);
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
        public async partial Task RaiderRoleCheck(IRole role)
        {
            try
            {
                await _raidCompService.CheckRole(role, ctx.Channel, ctx.Guild);
            }
            catch (Exception e)
            {
                await ctx.Channel.SendErrorAsync(_eb, e.Message);
            }
        }
    }
}