using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System.Globalization;

namespace AutoTreasureMaps.Core.Cleanup;

internal readonly record struct PendingLoot(uint ItemId, int Quantity, bool HighQuality);

// The loot coffers and treasure dungeons gave that is not kept, and the pass that discards it from the four bags. Kept
// means whitelisted, a timeworn map, or rare loot the player did not pick to discard. Only loot that landed in the bags is listed, so gil,
// crystals and currency never are. The game discards whole slots, so an item is listed only when the bags held none of it
// before the coffer opened (or it is listed already): every copy in the bags is then loot, and nothing the player got
// another way is ever discarded. Normal and high quality are told apart throughout, as they never share a slot: loot of
// one quality is listed, counted against the limit and discarded whatever the bags hold of the other. A pass discards one slot at a time, a human-like moment apart, and empties the list once
// no listed item is left in the bags. An item looted in a larger quantity than the cleanup limit is kept instead. Framework
// thread only.
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
    private static readonly List<PendingLoot> preview = [];

    private static bool running;
    private static long nextActionAtMs;
    private static int discardedThisPass;
    private static InventoryType lastContainer;
    private static ushort lastSlot;
    private static uint lastItemId;
    private static bool lastHighQuality;
    private static int lastAttempts;

    // Bumped whenever the list changes, so text built from it knows when to rebuild.
    public static int Revision { get; private set; }

    public static IReadOnlyList<PendingLoot> Pending => pending;

    public static bool HasPending => pending.Count > 0;

    public static bool Running => running;

    // What a treasure dungeon still under way has given so far, shown with the list but never discarded: the dungeon's
    // loot is noted once it ends, which replaces this.
    public static IReadOnlyList<PendingLoot> Preview => preview;

    public static bool HasShown => pending.Count > 0 || preview.Count > 0;

    // One call per finished map or dungeon, once its loot has settled, with the bags as they were before its coffer
    // opened. Without that snapshot nothing is listed, since the loot could not be told from what the player already had.
    public static void Note(Dictionary<uint, int>? bagsBefore)
    {
        DropPreview();
        if (!Plugin.Instance.Configuration.AutoDiscard || bagsBefore is null)
        {
            return;
        }

        var limit = Plugin.Instance.Configuration.AutoDiscardMaxQuantity;
        foreach (var (key, quantity) in BagSnapshot.KeyedGains(bagsBefore, BagSnapshot.Capture()))
        {
            var gain = new PendingLoot(BagSnapshot.ItemIdOf(key), quantity, BagSnapshot.IsHighQualityKey(key));
            if (!Listable(gain, bagsBefore, log: true))
            {
                continue;
            }

            var at = IndexOf(gain.ItemId, gain.HighQuality);
            var total = (at < 0 ? 0 : pending[at].Quantity) + gain.Quantity;
            if (OverLimit(total, limit))
            {
                RunLog.Debug($"Cleanup: keeping item {Describe(gain.ItemId, gain.HighQuality)}; {total} looted is over the limit of {limit}");
                if (at >= 0)
                {
                    pending.RemoveAt(at);
                    Revision++;
                }

                continue;
            }

            if (at < 0)
            {
                pending.Add(gain with { Quantity = total });
            }
            else
            {
                pending[at] = gain with { Quantity = total };
            }

            Revision++;
        }
    }

    // Called while a treasure dungeon goes on, so everything it has given so far shows before it ends.
    public static void ShowPreview(Dictionary<uint, int> bagsBefore)
    {
        preview.Clear();
        if (Plugin.Instance.Configuration.AutoDiscard)
        {
            var limit = Plugin.Instance.Configuration.AutoDiscardMaxQuantity;
            foreach (var (key, quantity) in BagSnapshot.KeyedGains(bagsBefore, BagSnapshot.Capture()))
            {
                var gain = new PendingLoot(BagSnapshot.ItemIdOf(key), quantity, BagSnapshot.IsHighQualityKey(key));
                var at = IndexOf(gain.ItemId, gain.HighQuality);
                if (Listable(gain, bagsBefore, log: false) && !OverLimit((at < 0 ? 0 : pending[at].Quantity) + gain.Quantity, limit))
                {
                    preview.Add(gain);
                }
            }
        }

        Revision++;
    }

    public static void DropPreview()
    {
        if (preview.Count == 0)
        {
            return;
        }

        preview.Clear();
        Revision++;
    }

    // A lower limit takes the items now over it off the list.
    public static void ApplyLimit()
    {
        var limit = Plugin.Instance.Configuration.AutoDiscardMaxQuantity;
        var removed = pending.RemoveAll(item => OverLimit(item.Quantity, limit)) + preview.RemoveAll(item => OverLimit(item.Quantity, limit));
        if (removed > 0)
        {
            Revision++;
        }
    }

    // Every copy of that quality in the bags must be loot: one the bags held before the coffer is listed only if it is
    // already. Copies of the other quality do not count, as the game keeps them in slots of their own.
    private static bool Listable(in PendingLoot gain, Dictionary<uint, int> bagsBefore, bool log)
    {
        if (gain.Quantity <= 0 || IsKept(gain.ItemId) || CountInBags(gain.ItemId, gain.HighQuality) == 0)
        {
            return false;
        }

        var held = bagsBefore.GetValueOrDefault(BagSnapshot.Key(gain.ItemId, gain.HighQuality));
        if (IndexOf(gain.ItemId, gain.HighQuality) < 0 && held > 0)
        {
            if (log)
            {
                RunLog.Debug($"Cleanup: not listing item {Describe(gain.ItemId, gain.HighQuality)}; the bags held {held} before the coffer");
            }

            return false;
        }

        return true;
    }

    // A limit of 0 keeps nothing back for its quantity.
    private static bool OverLimit(int quantity, int limit) => limit > 0 && quantity > limit;

    public static bool IsKept(uint itemId)
        => IsAlwaysKept(itemId) || KeepsRare(itemId) || OnWhitelist(itemId);

    // Kept whatever the whitelist says: a map, or an item the game refuses to discard.
    public static bool IsAlwaysKept(uint itemId)
        => Plugin.Instance.Catalog.TryGetByTimewornItem(itemId, out _)
        || Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is not { IsIndisposable: false };

    public static bool OnWhitelist(uint itemId) => Plugin.Instance.Configuration.AutoDiscardKeep.Contains(itemId);

    // Rare loot is kept unless the player picked it to be discarded.
    public static bool KeepsRare(uint itemId)
        => RareLoot.IsRare(itemId) && !Plugin.Instance.Configuration.AutoDiscardRare.Contains(itemId);

    public static void SetRareKept(uint itemId, bool keep)
    {
        var configuration = Plugin.Instance.Configuration;
        if (!keep)
        {
            if (!configuration.AutoDiscardRare.Contains(itemId))
            {
                configuration.AutoDiscardRare.Add(itemId);
                configuration.SaveDebounced();
            }

            return;
        }

        if (configuration.AutoDiscardRare.Remove(itemId))
        {
            configuration.SaveDebounced();
            Unlist(itemId);
        }
    }

    public static void AddToWhitelist(uint itemId)
    {
        var configuration = Plugin.Instance.Configuration;
        if (configuration.AutoDiscardKeep.Contains(itemId))
        {
            return;
        }

        configuration.AutoDiscardKeep.Add(itemId);
        configuration.SaveDebounced();
        Unlist(itemId);
    }

    // An item kept from now on leaves the list and the preview.
    private static void Unlist(uint itemId)
    {
        if (pending.RemoveAll(item => item.ItemId == itemId) > 0)
        {
            Revision++;
        }

        if (preview.RemoveAll(item => item.ItemId == itemId) > 0)
        {
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
        DropPreview();
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

        if (!TryFindNextSlot(out var container, out var slot, out var itemId, out var highQuality))
        {
            Finish();
            return;
        }

        if (!AllLoot(itemId, highQuality))
        {
            return;
        }

        if (container == lastContainer && slot == lastSlot && itemId == lastItemId && highQuality == lastHighQuality)
        {
            if (++lastAttempts > MaxAttemptsPerSlot)
            {
                GiveUp(itemId, highQuality);
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
            lastHighQuality = highQuality;
            lastAttempts = 1;
            discardedThisPass++;
        }

        var inventory = InventoryManager.Instance();
        var result = inventory is null ? -1 : inventory->DiscardItem(container, slot);
        RunLog.Debug($"Cleanup: discarding item {Describe(itemId, highQuality)} from {container} slot {slot} (attempt {lastAttempts}, result {result})");
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

    private static void GiveUp(uint itemId, bool highQuality)
    {
        var at = IndexOf(itemId, highQuality);
        if (at >= 0)
        {
            pending.RemoveAt(at);
            Revision++;
        }

        lastItemId = 0;
        var name = NotableLoot.Name(itemId);
        RunLog.Warning($"Cleanup: item {Describe(itemId, highQuality)} ({name}) stayed in its slot after {MaxAttemptsPerSlot} discards; taken off the list");
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Cleanup.ChatGaveUp, name)}");
    }

    // More of the item in the bags than was looted, in the same quality, means some came another way since; the item is
    // dropped from the list rather than discarded, as a slot cannot be split.
    private static bool AllLoot(uint itemId, bool highQuality)
    {
        var at = IndexOf(itemId, highQuality);
        var looted = at < 0 ? 0 : pending[at].Quantity;
        var held = CountInBags(itemId, highQuality);
        if (held <= looted)
        {
            return true;
        }

        if (at >= 0)
        {
            pending.RemoveAt(at);
            Revision++;
        }

        RunLog.Debug($"Cleanup: not discarding item {Describe(itemId, highQuality)}; the bags hold {held} but only {looted} were looted");
        return false;
    }

    // The first bag slot holding a listed item that is not kept; the whitelist may have changed since it was listed.
    private static bool TryFindNextSlot(out InventoryType container, out ushort slot, out uint itemId, out bool highQuality)
    {
        container = default;
        slot = 0;
        itemId = 0;
        highQuality = false;
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
                if (item is null || item->ItemId == 0 || IndexOf(item->ItemId, item->IsHighQuality()) < 0 || IsKept(item->ItemId))
                {
                    continue;
                }

                container = Bags[bagIndex];
                slot = (ushort)slotIndex;
                itemId = item->ItemId;
                highQuality = item->IsHighQuality();
                return true;
            }
        }

        return false;
    }

    private static int CountInBags(uint itemId, bool highQuality)
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
                if (item is not null && item->ItemId == itemId && item->IsHighQuality() == highQuality)
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

    private static string Describe(uint itemId, bool highQuality)
        => highQuality ? $"{itemId} (HQ)" : itemId.ToString(CultureInfo.InvariantCulture);

    private static int IndexOf(uint itemId, bool highQuality)
    {
        for (var index = 0; index < pending.Count; index++)
        {
            if (pending[index].ItemId == itemId && pending[index].HighQuality == highQuality)
            {
                return index;
            }
        }

        return -1;
    }
}
