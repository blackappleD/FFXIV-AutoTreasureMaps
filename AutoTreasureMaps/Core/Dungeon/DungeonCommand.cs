using AutoTreasureMaps.Core.Localization;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Dungeon;

// /atm dungeon clears the treasure dungeon the character stands in; record, record stop and nearby capture what a
// dungeon shows, for its profile.
internal static class DungeonCommand
{
    private const string RecordWord = "record";
    private const string StopWord = "stop";
    private const string NearbyWord = "nearby";

    public static void Handle(string arguments, Plugin plugin)
    {
        if (arguments.Length == 0)
        {
            plugin.Controller.StartDungeon();
            return;
        }

        if (CommandWords.StartsWith(arguments, RecordWord))
        {
            var rest = CommandWords.After(arguments, RecordWord);
            if (rest.Equals(StopWord, StringComparison.OrdinalIgnoreCase))
            {
                StopRecording();
                return;
            }

            StartRecording();
            return;
        }

        if (arguments.Equals(NearbyWord, StringComparison.OrdinalIgnoreCase))
        {
            DungeonRecorder.DumpNearby();
            return;
        }

        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.CommandHelp)}");
    }

    private static void StartRecording()
    {
        var recorder = DungeonRecorder.Instance;
        if (recorder.Recording)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatRecording, recorder.FilePath ?? string.Empty)}");
            return;
        }

        recorder.Start();
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatRecordStarted, recorder.FilePath ?? string.Empty)}");
    }

    private static void StopRecording()
    {
        var recorder = DungeonRecorder.Instance;
        if (!recorder.Recording)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatNotRecording)}");
            return;
        }

        var path = recorder.FilePath ?? string.Empty;
        recorder.Stop();
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatRecordStopped, path)}");
    }
}
