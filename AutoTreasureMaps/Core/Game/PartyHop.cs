using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Travel;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Text.RegularExpressions;

namespace AutoTreasureMaps.Core.Game;

// A party member as the leader invites it back: by content id, name and home world.
internal readonly record struct PartyMate(ulong ContentId, string Name, ushort HomeWorldId);

// The leader's word to the party that it is buying on another world.
internal readonly record struct WorldHop(string WorldName, long ReceivedAtMs, string LeaderName);

// A party on its own world cannot visit another, and a party formed across worlds cannot dig together (its members
// cannot touch the guards the leader's dig calls). So when the leader buys on another world it tells the party where in
// party chat, breaks the party up, and every member travels there; once the leader has bought, it invites the members
// back on that world, and the run goes on there. This holds both sides of that: the members the leader is to invite
// back, and the world a member was told to go to. Read and written only on the framework thread.
internal static partial class PartyHop
{
    private const string MessagePrefix = "[ATM]跨服：";

    // The members the leader broke the party up with, until they are back in it.
    public static IReadOnlyList<PartyMate> Partners { get; private set; } = [];

    public static bool Away => Partners.Count > 0;

    // The trip a member was told about, until it is back in the leader's party.
    public static WorldHop? Pending { get; private set; }

    // The name a member waiting on another world accepts an invite from, so the invite watcher leaves that one alone.
    public static string? AwaitedInviter { get; set; }

    public static string Message(string worldName) => $"{MessagePrefix}[{worldName}]";

    public static void RememberPartners()
    {
        var mates = new List<PartyMate>();
        var party = Svc.Party;
        for (var index = 0; index < party.Length; index++)
        {
            if (party[index] is { } member && !PartyPresence.IsLocal(member))
            {
                mates.Add(new PartyMate((ulong)member.ContentId, member.Name.TextValue, (ushort)member.World.RowId));
            }
        }

        Partners = mates;
    }

    public static void ForgetPartners() => Partners = [];

    public static void ForgetPending()
    {
        Pending = null;
        AwaitedInviter = null;
    }

    // A party chat line from the leader: true when it named a world to go to.
    public static bool TryNote(string text, string leaderName)
    {
        var match = HopLine().Match(text);
        if (!match.Success)
        {
            return false;
        }

        Pending = new WorldHop(match.Groups[1].Value, Environment.TickCount64, leaderName);
        return true;
    }

    // The world of the data centre the name stands for, as the game spells it or as the plugin prints it.
    public static uint WorldIdOf(string name)
    {
        var dataCenter = Worlds.DataCenterOf(Worlds.Current);
        foreach (var world in Svc.Data.GetExcelSheet<World>())
        {
            if (world.DataCenter.ValueNullable?.Name.ExtractText() == dataCenter
                && (world.Name.ExtractText() == name || Worlds.NameOf(world.RowId) == name))
            {
                return world.RowId;
            }
        }

        return 0;
    }

    [GeneratedRegex(@"\[ATM\]跨服：\[(.+?)\]")]
    private static partial Regex HopLine();
}
