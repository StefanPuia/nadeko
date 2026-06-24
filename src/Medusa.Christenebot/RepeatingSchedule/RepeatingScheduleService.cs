using Discord;
using Discord.WebSocket;
using NadekoBot.Medusa;
using NadekoBot.Modules.Administration;
using NadekoBot.Services;
using Serilog;

namespace Medusa.Christenebot.AutoReschedule;

[svc(Lifetime.Singleton)]
public class RepeatingScheduleService(
    RepeatingScheduleRepository repository,
    ICommandHandler cmdHandler,
    DiscordSocketClient client)
{
    public async Task Initialize()
    {
        await repository.SetUpDatabase();
    }

    public async Task CreateScheduleAsync(ulong userId, ulong messageId, ulong channelId,
        ulong guildId,
        string commandText, TimeSpan delayInMinutes)
    {
        await repository.CreateSchedule(userId,
            messageId,
            channelId,
            guildId,
            commandText,
            delayInMinutes);
    }

    public async Task ExecuteCommands()
    {
        while (true)
        {
            try
            {
                var readySchedules = await repository.FindAllReady();
                foreach (var schedule in readySchedules)
                {
                    try
                    {
                        var guild = client.GetGuild(schedule.GuildId);
                        var channel =
                            guild?.GetChannel(schedule.ChannelId) as ISocketMessageChannel;

                        if (guild is null || channel is null)
                        {
                            Log.Warning("Guild or channel not found for schedule {Schedule}",
                                schedule);
                            await UpdateScheduleNextRunTime(schedule);
                            continue;
                        }

                        var tempMessage = await channel.SendMessageAsync(".");
                        var user = tempMessage.Author;
                        await cmdHandler.TryRunCommand(guild,
                            channel,
                            new DoAsUserMessage(tempMessage, user, schedule.CommandText));
                        await UpdateScheduleNextRunTime(schedule);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e,
                            "Error executing repeating schedule command {Schedule}",
                            schedule);
                    }
                }

                async Task UpdateScheduleNextRunTime(RepeatingScheduleRecord schedule)
                {
                    await repository.UpdateNextRunTime(schedule.Id,
                        DateTime.UtcNow.AddMinutes(schedule.DelayInMinutes));
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error executing repeating schedule commands");
            }
            finally
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }
    }

    public async Task DeleteScheduleAsync(int id, ulong userId, ulong guildId)
        => await repository.DeleteScheduleForUserAndGuild(id, userId, guildId);

    public async Task<List<RepeatingScheduleRecord>> ListScheduleAsync(ulong userId, ulong guildId)
        => await repository.ListScheduleForUserInGuild(userId, guildId);
}