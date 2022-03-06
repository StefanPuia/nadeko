#nullable disable
namespace NadekoBot.Modules.Utility.Services;

public class MaintenanceService: INService
{
    private readonly IBotCredentials _creds;
    public MaintenanceService(IBotCredentials creds) => _creds = creds;

    public async Task NetStat(IMessageChannel channel)
    {
        var currentUnix = DateTimeOffset.Now.ToUnixTimeSeconds();
        await channel.SendMessageAsync($"{_creds.NetStatUrl}?_={currentUnix}");
    }
}