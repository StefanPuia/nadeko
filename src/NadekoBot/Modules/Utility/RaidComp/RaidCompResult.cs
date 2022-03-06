namespace NadekoBot.Modules.Utility.RaidComp;

public class RaidCompResult
{
    public RaidCompResult(RaidCompResultBuild[] builds) => Builds = builds;

    public RaidCompResultBuild[] Builds { get; }
}

public class RaidCompResultBuild
{
    public RaidCompResultBuild(string buildId, string buildName)
    {
        BuildId = buildId;
        BuildName = buildName;
    }

    public string BuildId { get; }
    public string BuildName { get; }
}