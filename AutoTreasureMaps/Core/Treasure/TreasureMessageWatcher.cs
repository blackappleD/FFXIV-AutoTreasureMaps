using Dalamud.Game.Chat;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Treasure;

// Keeps the last few treasure messages, so a task can ask whether one arrived since it acted. Times are
// Environment.TickCount64, the clock the task waits run on. The ring is locked because the game raises log messages
// from its own hook while the window and the tasks read it.
internal sealed class TreasureMessageWatcher : IDisposable
{
    // A treasure dungeon sends a burst of its own messages per round, so the ring holds a whole round's worth.
    private const int Capacity = 128;
    private const string NotAnInteger = "-";

    private readonly object gate = new();
    private readonly TreasureMessage[] ring = new TreasureMessage[Capacity];
    private readonly TreasureCatalog catalog;
    private int next;
    private int count;

    public TreasureMessageWatcher(TreasureCatalog catalog)
    {
        this.catalog = catalog;
        Svc.Chat.LogMessage += OnLogMessage;
    }

    public static long Now => Environment.TickCount64;

    public void Dispose()
    {
        Svc.Chat.LogMessage -= OnLogMessage;
    }

    public bool ArrivedSince(uint logMessageId, long sinceMs) => TryGetLatest([logMessageId], sinceMs, out _);

    public bool TryGetLatest(uint logMessageId, long sinceMs, out TreasureMessage message)
        => TryGetLatest([logMessageId], sinceMs, out message);

    // The ring holds messages in arrival order, so the walk from the newest stops at the first one older than sinceMs.
    public bool TryGetLatest(ReadOnlySpan<uint> logMessageIds, long sinceMs, out TreasureMessage message)
    {
        lock (gate)
        {
            for (var offset = 1; offset <= count; offset++)
            {
                var candidate = ring[(next - offset + Capacity) % Capacity];
                if (candidate.AtMs < sinceMs)
                {
                    break;
                }

                if (logMessageIds.Contains(candidate.Id))
                {
                    message = candidate;
                    return true;
                }
            }
        }

        message = default;
        return false;
    }

    private void OnLogMessage(ILogMessage logMessage)
    {
        var logMessageId = logMessage.LogMessageId;
        var name = TreasureLogMessages.NameOf(logMessageId) ?? Dungeon.DungeonLogMessages.NameOf(logMessageId);
        if (name is null)
        {
            return;
        }

        var message = Capture(logMessage, logMessageId);
        if (TreasureLogMessages.NamesAnItem(logMessageId) && !NamesTimewornMap(message))
        {
            return;
        }

        lock (gate)
        {
            ring[next] = message;
            next = (next + 1) % Capacity;
            count = Math.Min(count + 1, Capacity);
        }

        RunLog.Debug($"Game message {logMessageId} {name}: parameters {Describe(message, 0)}, {Describe(message, 1)}, {Describe(message, 2)}.");
    }

    private bool NamesTimewornMap(in TreasureMessage message)
        => message.TryGetParameter(0, out var itemId) && catalog.TryGetByTimewornItem((uint)itemId, out _);

    private static TreasureMessage Capture(ILogMessage logMessage, uint logMessageId)
    {
        Span<int> values = stackalloc int[TreasureMessage.MaxParameters];
        var mask = 0;
        var parameterCount = Math.Min(logMessage.ParameterCount, TreasureMessage.MaxParameters);
        for (var index = 0; index < parameterCount; index++)
        {
            if (logMessage.TryGetIntParameter(index, out var value))
            {
                values[index] = value;
                mask |= 1 << index;
            }
        }

        return new TreasureMessage(logMessageId, Now, values[0], values[1], values[2], (byte)mask);
    }

    private static string Describe(in TreasureMessage message, int index)
        => message.TryGetParameter(index, out var value) ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : NotAnInteger;
}
