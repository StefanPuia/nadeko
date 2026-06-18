using Microsoft.EntityFrameworkCore;
using NadekoBot.Medusa;
using NadekoBot.Services;

namespace Medusa.Christenebot.AutoReschedule;

[svc(Lifetime.Singleton)]
public class RepeatingScheduleRepository(DbService db)
{
    public async Task SetUpDatabase()
    {
        await db.GetDbContext().Database.ExecuteSqlAsync(
            $"""
             CREATE TABLE IF NOT EXISTS
                 Medusa_CustomFunctionality_RepeatingSchedule
             (
                 Id             INTEGER  not null
                     constraint PK_ScheduledCommand
                         primary key autoincrement,
                 UserId         INTEGER  NOT NULL,
                 MessageId      INTEGER  NOT NULL,
                 ChannelId      INTEGER  NOT NULL,
                 GuildId        INTEGER  NOT NULL,
                 CommandText    TEXT     NOT NULL,
                 NextRunTime    DATETIME NOT NULL,
                 DelayInMinutes INTEGER  NOT NULL
             );

             """);
    }

    public async Task CreateSchedule(ulong userId, ulong messageId, ulong channelId, ulong guildId,
        string commandText, TimeSpan delayInMinutes)
    {
        var nextRunTime = DateTime.UtcNow.Add(delayInMinutes);
        await db.GetDbContext().Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO Medusa_CustomFunctionality_RepeatingSchedule
                    (UserId, MessageId, ChannelId, GuildId, CommandText, NextRunTime, DelayInMinutes)
                    VALUES ({userId}, {messageId}, {channelId}, {guildId}, {commandText}, {nextRunTime}, {delayInMinutes.TotalMinutes})
             """);
    }

    public async Task DeleteScheduleForUserAndGuild(int id, ulong userId, ulong guildId)
    {
        var deleted = await db.GetDbContext().Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM Medusa_CustomFunctionality_RepeatingSchedule WHERE Id = {id} AND UserId = {userId} AND GuildId = {guildId}");
        if (deleted == 0)
            throw new Exception("Schedule not found");
    }

    public async Task<List<RepeatingScheduleRecord>> ListScheduleForUserInGuild(ulong userId,
        ulong guildId)
        => await db.GetDbContext().Database.SqlQuery<RepeatingScheduleRecord>(
                $"SELECT * FROM Medusa_CustomFunctionality_RepeatingSchedule where UserId = {userId} and GuildId = {guildId}")
            .ToListAsync();

    public async Task<List<RepeatingScheduleRecord>> FindAllReady()
        => await db.GetDbContext().Database.SqlQuery<RepeatingScheduleRecord>(
                $"SELECT * FROM Medusa_CustomFunctionality_RepeatingSchedule where NextRunTime <= {DateTime.UtcNow}")
            .ToListAsync();

    public async Task UpdateNextRunTime(int id, DateTime nextRunTime)
    {
        var updated = await db.GetDbContext().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Medusa_CustomFunctionality_RepeatingSchedule SET NextRunTime = {nextRunTime} WHERE Id = {id}");
        if (updated == 0)
            throw new Exception("Schedule not found");
    }
}

public record RepeatingScheduleRecord(
    int Id,
    ulong UserId,
    ulong MessageId,
    ulong ChannelId,
    ulong GuildId,
    string CommandText,
    DateTime NextRunTime,
    int DelayInMinutes);