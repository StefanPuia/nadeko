namespace Medusa.Christenebot;

public class Utils
{
    public static string toRelativeDiscordTime(DateTime time)
        => $"<t:{(int) time.Subtract(DateTime.UnixEpoch).TotalSeconds}:R>";
}