namespace NadekoBot.Common.ModuleBehaviors;

/// <summary>
///     Implemented by modules which block execution before anything is executed
/// </summary>
public interface IEarlyBehavior
{
    int Priority { get; }
    bool AllowBots { get; }
    Task<bool> RunBehavior(IGuild guild, IUserMessage msg);
}