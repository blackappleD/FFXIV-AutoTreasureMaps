using Dalamud.Game.Chat;
using ECommons.DalamudServices;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace AutoTreasureMaps.Core.Dungeon;

// Writes one bonus round down as it is walked: the character's position ten times a second, every sack and charger as it
// comes, moves, casts or goes, each plan the walk picks and every log message, so a round that turns up rarely can be
// read and played back afterwards. One plain text file per round in the plugin's config folder.
internal sealed class SackRoundCapture : IDisposable
{
    private const string CapturesFolder = "bonus-captures";
    private const int PositionIntervalMs = 100;

    private readonly StreamWriter? writer;
    private readonly long startedAtMs = Environment.TickCount64;
    private long nextPositionAtMs;
    private string statuses = string.Empty;

    public SackRoundCapture(DungeonProfile profile)
    {
        try
        {
            var folder = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), CapturesFolder);
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, CaptureFiles.Name(DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
            writer = new StreamWriter(FilePath, append: false, Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Sack capture: the file could not be opened; the round is not written down.");
            return;
        }

        Svc.Chat.LogMessage += OnLogMessage;
        Write($"start: {profile.Key}, plugin {Plugin.PluginInterface.Manifest.AssemblyVersion}");
    }

    public string? FilePath { get; }

    public void Record(long nowMs, SackField field, float speed)
    {
        for (var index = 0; index < field.Changes.Count; index++)
        {
            Write(field.Changes[index]);
        }

        if (nowMs < nextPositionAtMs || Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        nextPositionAtMs = nowMs + PositionIntervalMs;
        var at = SackField.Flat(player.Position);
        Write(string.Create(CultureInfo.InvariantCulture, $"at ({at.X:F2}, {at.Y:F2}) speed {speed:F1}, {field.Sacks.Count} sacks, {field.Lanes.Count} lanes"));
        var now = string.Join(",", player.StatusList.Where(status => status.StatusId != 0).Select(status => status.StatusId));
        if (now != statuses)
        {
            statuses = now;
            Write($"statuses [{now}]");
        }
    }

    public void Note(string text) => Write(text);

    public static string Format(Vector2 at)
        => string.Create(CultureInfo.InvariantCulture, $"({at.X:F1}, {at.Y:F1})");

    public void Dispose()
    {
        if (writer is null)
        {
            return;
        }

        Svc.Chat.LogMessage -= OnLogMessage;
        Write("stop");
        writer.Dispose();
    }

    private void OnLogMessage(ILogMessage message)
        => Write($"log {message.LogMessageId}: \"{message.FormatLogMessageForDebugging().ExtractText().Replace('\n', ' ')}\"");

    private void Write(string text)
    {
        try
        {
            writer?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{(Environment.TickCount64 - startedAtMs) / 1000.0:F2}] {text}"));
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Sack capture: the file could not be written.");
        }
    }
}
