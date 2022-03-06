using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility.Bots;

public class PreventEmptyBotMessages : IEarlyBehavior
{
    private readonly DiscordSocketClient _client;

    public PreventEmptyBotMessages(DiscordSocketClient client) => _client = client;

    public int Priority => int.MaxValue - 10;
    public bool AllowBots => true;

    public Task<bool> RunBehavior(IGuild guild, IUserMessage msg) =>
        Task.FromResult((msg.Author.IsBot && msg.Author.Id != _client.CurrentUser.Id)
                        || string.IsNullOrWhiteSpace(msg.Content));
}