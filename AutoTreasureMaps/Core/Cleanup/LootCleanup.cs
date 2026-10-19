using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System.Collections.Immutable;

namespace AutoTreasureMaps.Core.Cleanup;

internal readonly record struct PendingLoot(uint ItemId, int Quantity);

// The loot coffers and treasure dungeons gave that is not kept, and the pass that discards it from the four bags. Kept
// means whitelisted, a timeworn map, or any map's high-value loot. Only loot that landed in the bags is listed, so gil,
// crystals and currency never are. The game discards whole slots, so an item is listed only when the bags held none of it
// before the coffer opened (or it is listed already): every copy in the bags is then loot, and nothing the player got
// another way is ever discarded. A pass discards one slot at a time, a human-like moment apart, and empties the list once
// no listed item is left in the bags. Framework thread only.
internal static unsafe class LootCleanup
{
    private const int DiscardDelayMinMs = 700;
    private const int DiscardDelayMaxMs = 1_400;
    private const int BusyRetryMs = 500;
    // A slot still holding its item after this many discards is given up on, so a locked item cannot stall the pass.
    private const int MaxAttemptsPerSlot = 3;
    // So a pass cannot empty the bags in one go; the rest stays listed for the next pass.
    private const int MaxDiscardsPerPass = 60;

    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly List<PendingLoot> pending = [];

    private static bool running;
    private static long nextActionAtMs;
    private static int discardedThisPass;
    private static InventoryType lastContainer;
    private static ushort lastSlot;
    private static uint lastItemId;
    private static int lastAttempts;

    // Bumped whenever the list changes, so text built from it knows when to rebuild.
    public static int Revision { get; private set; }

    public static IReadOnlyList<PendingLoot> Pending => pending;

    public static bool HasPending => pending.Count > 0;

    public static bool Running => running;

    // One call per finished map or dungeon, with what it gave and the bags as they were before its coffer opened. Without
    // that snapshot nothing is listed, since the loot could not be told from what the player already had.
    public static void Note(ImmutableArray<ItemGain> gains, Dictionary<uint, int>? bagsBefore)
    {
        if (!Plugin.Instance.Configuration.AutoDiscard || gains.IsDefaultOrEmpty || bagsBefore is null)
        {
            return;
        }

        var items = gains.AsSpan();
        for (var index = 0; index < items.Length; index++)
        {
            var gain = items[index];
            if (gain.Quantity <= 0 || IsKept(gain.ItemId) || !InBags(gain.ItemId))
            {
                continue;
            }

            var at = IndexOf(gain.ItemId);
            if (at < 0 && bagsBefore.GetValueOrDefault(gain.ItemId) > 0)
            {
                RunLog.Debug($"Cleanup: not listing item {gain.ItemId}; the bags held {bagsBefore[gain.ItemId]} before the coffer");
                continue;
            }

            if (at < 0)
            {
                pending.Add(new PendingLoot(gain.ItemId, gain.Quantity));
            }
            else
            {
                pending[at] = new PendingLoot(gain.ItemId, pending[at].Quantity + gain.Quantity);
            }

            Revision++;
        }
    }

    public static bool IsKept(uint itemId)
        => IsAlwaysKept(itemId) || OnWhitelist(itemId);

    // Kept whatever the whitelist says: a map, any map's high-value loot, or an item the game refuses to discard.
    public static bool IsAlwaysKept(uint itemId)
        => HighValueLoot.IsHighValue(itemId)
        || Plugin.Instance.Catalog.TryGetByTimewornItem(itemId, out _)
        || Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is not { IsIndisposable: false };

    public static bool OnWhitelist(uint itemId) => Plugin.Instance.Configuration.AutoDiscardKeep.Contains(itemId);

    public static void AddToWhitelist(uint itemId)
    {
        var configuration = Plugin.Instance.Configuration;
        if (configuration.AutoDiscardKeep.Contains(itemId))
        {
            return;
        }

        configuration.AutoDiscardKeep.Add(itemId);
        configuration.SaveDebounced();
        var at = IndexOf(itemId);
        if (at >= 0)
        {
            pending.RemoveAt(at);
            Revision++;
        }
    }

    public static void RemoveFromWhitelist(uint itemId)
    {
        var configuration = Plugin.Instance.Configuration;
        if (configuration.AutoDiscardKeep.Remove(itemId))
        {
            configuration.SaveDebounced();
        }
    }

    public static void Start()
    {
        if (running || pending.Count == 0)
        {
            return;
        }

        running = true;
        discardedThisPass = 0;
        lastItemId = 0;
        nextActionAtMs = 0;
        RunLog.Debug($"Cleanup: discarding {pending.Count} kinds of loot");
    }

    // Turning auto-discard off forgets what was noted, so turning it back on later never discards stale loot.
    public static void Clear()
    {
        Stop();
        if (pending.Count == 0)
        {
            return;
        }

        pending.Clear();
        Revision++;
    }

    public static void Stop()
    {
        if (!running)
        {
            return;
        }

        running = false;
        RunLog.Debug($"Cleanup: stopped after {discardedThisPass} discards");
    }

    public static void Tick()
    {
        if (!running)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now < nextActionAtMs)
        {
            return;
        }

        if (!CharacterFree())
        {
            nextActionAtMs = now + BusyRetryMs;
            return;
        }

        if (!TryFindNextSlot(out var container, out var slot, out var itemId))
        {
            Finish();
            return;
        }

        if (!AllLoot(itemId))
        {
            return;
        }

        if (container == lastContainer && slot == lastSlot && itemId == lastItemId)
        {
            if (++lastAttempts > MaxAttemptsPerSlot)
            {
                GiveUp(itemId);
                return;
            }
        }
        else
        {
            if (discardedThisPass >= MaxDiscardsPerPass)
            {
                running = false;
                RunLog.Warning($"Cleanup: stopped at the cap of {MaxDiscardsPerPass} discards; the rest waits for the next pass");
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Cleanup.ChatCapped, MaxDiscardsPerPass)}");
                return;
            }

            lastContainer = container;
            lastSlot = slot;
            lastItemId = itemId;
            lastAttempts = 1;
            discardedThisPass++;
        }

        var inventory = InventoryManager.Instance();
        var result = inventory is null ? -1 : inventory->DiscardItem(container, slot);
        RunLog.Debug($"Cleanup: discarding item {itemId} from {container} slot {slot} (attempt {lastAttempts}, result {result})");
        nextActionAtMs = now + Random.Shared.Next(DiscardDelayMinMs, DiscardDelayMaxMs + 1);
    }

    private static void Finish()
    {
        running = false;
        var discarded = discardedThisPass;
        pending.Clear();
        Revision++;
        RunLog.Debug($"Cleanup: done, {discarded} slots discarded");
        if (discarded > 0)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.Plural(L.Cleanup.ChatDone, discarded)}");
        }
    }

    private static void GiveUp(uint itemId)
    {
        var at = IndexOf(itemId);
        if (at >= 0)
        {
            pending.RemoveAt(at);
            Revision++;
        }

        lastItemId = 0;
        var name = NotableLoot.Name(itemId);
        RunLog.Warning($"Cleanup: item {itemId} ({name}) stayed in its slot after {MaxAttemptsPerSlot} discards; taken off the list");
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Cleanup.ChatGaveUp, name)}");
    }

    // More of the item in the bags than was looted means some came another way since; the item is dropped from the list
    // rather than discarded, as a slot cannot be split.
    private static bool AllLoot(uint itemId)
    {
        var at = IndexOf(itemId);
        var looted = at < 0 ? 0 : pending[at].Quantity;
        var held = CountInBags(itemId);
        if (held <= looted)
        {
            return true;
        }

        if (at >= 0)
        {
            pending.RemoveAt(at);
            Revision++;
        }

        RunLog.Debug($"Cleanup: not discarding item {itemId}; the bags hold {held} but only {looted} were looted");
        return false;
    }

    // The first bag slot holding a listed item that is not kept; the whitelist may have changed since it was listed.
    private static bool TryFindNextSlot(out InventoryType container, out ushort slot, out uint itemId)
    {
        container = default;
        slot = 0;
        itemId = 0;
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return false;
        }

        for (var bagIndex = 0; bagIndex < Bags.Length; bagIndex++)
        {
            var bag = inventory->GetInventoryContainer(Bags[bagIndex]);
            if (bag is null || !bag->IsLoaded)
            {
                continue;
            }

            for (var slotIndex = 0; slotIndex < bag->Size; slotIndex++)
            {
                var item = bag->GetInventorySlot(slotIndex);
                if (item is null || item->ItemId == 0 || IndexOf(item->ItemId) < 0 || IsKept(item->ItemId))
                {
                    continue;
                }

                container = Bags[bagIndex];
                slot = (ushort)slotIndex;
                itemId = item->ItemId;
                return true;
            }
        }

        return false;
    }

    private static bool InBags(uint itemId) => CountInBags(itemId) > 0;

    private static int CountInBags(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return 0;
        }

        var count = 0;
        for (var bagIndex = 0; bagIndex < Bags.Length; bagIndex++)
        {
            var bag = inventory->GetInventoryContainer(Bags[bagIndex]);
            if (bag is null || !bag->IsLoaded)
            {
                continue;
            }

            for (var slotIndex = 0; slotIndex < bag->Size; slotIndex++)
            {
                var item = bag->GetInventorySlot(slotIndex);
                if (item is not null && item->ItemId == itemId)
                {
                    count += (int)item->Quantity;
                }
            }
        }

        return count;
    }

    private static bool CharacterFree()
        => Svc.Objects.LocalPlayer is { IsDead: false }
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.BetweenAreas51]
        && !Svc.Condition[ConditionFlag.Casting]
        && !Svc.Condition[ConditionFlag.Occupied]
        && !Svc.Condition[ConditionFlag.OccupiedInEvent]
        && !Svc.Condition[ConditionFlag.OccupiedInQuestEvent];

    private static int IndexOf(uint itemId)
    {
        for (var index = 0; index < pending.Count; index++)
        {
            if (pending[index].ItemId == itemId)
            {
                return index;
            }
        }

        return -1;
    }
}
