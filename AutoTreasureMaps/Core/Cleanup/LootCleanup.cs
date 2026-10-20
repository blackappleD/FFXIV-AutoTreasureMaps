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
// means whitelisted, a timeworn map, rare loot the player did not pick to discard, an item the game will not let go
// of, or (unless turned off) any other item of notable rarity, a unique item, equipment or an item that unlocks
// something. Only loot that landed in the bags is listed, so gil, crystals and currency never are. The game discards
// whole slots, so an item is listed only when the bags held none of it before the coffer opened (or it is listed
// already): every copy in the bags is then loot. A map or dungeon that was paused after its coffer opened is not
// listed at all, since what the player did meanwhile would read as loot. The list belongs to one character and one
// run: it is cleared when the run stops or ends and on logout, and a pass refuses to run for another character. A
// pass discards one slot at a time, a human-like moment apart, takes each discarded slot off the noted quantity, and
// empties the list once no listed item is left in the bags. An item looted in a larger quantity than the cleanup
// limit is kept instead. Normal and high quality are told apart throughout, as they never share a slot: loot of
// one quality is listed, counted against the limit and discarded whatever the bags hold of the other. Framework
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
    private const byte NotableRarity = 1;
    private const uint MedicineCategory = 44;
    private const uint MealCategory = 46;
    private const uint FurnishingCategory = 57;
    private const uint FirstHousingCategory = 65;
    private const uint LastHousingCategory = 80;
    private const uint MinionCategory = 81;
    private const uint CardCategory = 86;
    private const uint OrchestrionCategory = 94;
    private const uint PaintingCategory = 95;

    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly List<PendingLoot> pending = [];
    private static readonly List<PendingLoot> preview = [];

    private static bool running;
    private static ulong ownerContentId;
    private static long nextActionAtMs;
    private static int discardedThisPass;
    private static InventoryType lastContainer;
    private static ushort lastSlot;
    private static uint lastItemId;
    private static bool lastHighQuality;
    private static int lastAttempts;
    // The slot discarded last and its quantity, taken off the noted quantity once the slot has emptied.
    private static int lastQuantity;

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

        var contentId = Svc.PlayerState.ContentId;
        if (pending.Count > 0 && ownerContentId != contentId)
        {
            RunLog.Info("Cleanup: the noted loot was another character's; it is forgotten.");
            pending.Clear();
            Revision++;
        }

        ownerContentId = contentId;

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
        => IsAlwaysKept(itemId) || KeepsRare(itemId) || KeptAsNotable(itemId) || OnWhitelist(itemId);

    // Kept whatever the whitelist says: a map, or an item the game refuses to discard.
    public static bool IsAlwaysKept(uint itemId)
        => Plugin.Instance.Catalog.TryGetByTimewornItem(itemId, out _)
        || Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is not { IsIndisposable: false };

    // With the setting on (the default), anything that may be worth more than a material. A listed rare item follows
    // the player's pick instead, so one picked to discard is not kept back here.
    private static bool KeptAsNotable(uint itemId)
        => Plugin.Instance.Configuration.AutoDiscardKeepNotable
        && !RareLoot.IsRare(itemId)
        && Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } item
        && KeptByKind(item);

    // Notable rarity, unique, equipment, a furnishing, a minion, an orchestrion roll or a card, or an item with a use other
    // than a meal or a medicine (a mount, a hairstyle, facewear and the like unlock something). This covers the rare loot
    // of every map the plugin has no list for.
    private static bool KeptByKind(in Item item)
        => item.Rarity > NotableRarity
        || item.IsUnique
        || item.EquipSlotCategory.RowId != 0
        || KeptCategory(item.ItemUICategory.RowId)
        || (item.ItemAction.RowId != 0 && item.ItemUICategory.RowId is not (MealCategory or MedicineCategory));

    // ItemUICategory rows: Furnishing 57, the housing exterior and interior parts 65 to 80, Minion 81, Triple Triad Card
    // 86, Orchestrion Roll 94 and Painting 95.
    private static bool KeptCategory(uint category)
        => category is FurnishingCategory or (>= FirstHousingCategory and <= LastHousingCategory) or MinionCategory
            or CardCategory or OrchestrionCategory or PaintingCategory;

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
        if (running || pending.Count == 0 || !Plugin.Instance.Configuration.AutoDiscard)
        {
            return;
        }

        if (ownerContentId != Svc.PlayerState.ContentId)
        {
            RunLog.Info("Cleanup: the noted loot was another character's; it is forgotten and nothing is discarded.");
            Clear();
            return;
        }

        // The slot a pass cut short discarded last may have emptied since, so its copies come off the count first.
        ConsumeLastSlotIfGone();
        running = true;
        discardedThisPass = 0;
        lastItemId = 0;
        lastQuantity = 0;
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

        // A pass cut short by its time limit never reaches the tick that takes the last discarded slot off the count.
        ConsumeLastSlotIfGone();
        running = false;
        RunLog.Debug($"Cleanup: stopped after {discardedThisPass} discards");
    }

    public static void Tick()
    {
        if (!running)
        {
            return;
        }

        if (!Plugin.Instance.Configuration.AutoDiscard || ownerContentId != Svc.PlayerState.ContentId)
        {
            Clear();
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

        var found = TryFindNextSlot(out var container, out var slot, out var itemId, out var highQuality);
        if (!found || container != lastContainer || slot != lastSlot || itemId != lastItemId || highQuality != lastHighQuality)
        {
            ConsumeLastSlot();
        }

        if (!found)
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
            lastQuantity = SlotQuantity(container, slot);
            discardedThisPass++;
        }

        var inventory = InventoryManager.Instance();
        var result = inventory is null ? -1 : inventory->DiscardItem(container, slot);
        RunLog.Info($"Cleanup: discarding {NotableLoot.Name(itemId)} x{lastQuantity} (item {Describe(itemId, highQuality)}) from {container} slot {slot}, attempt {lastAttempts}, result {result}.");
        nextActionAtMs = now + Random.Shared.Next(DiscardDelayMinMs, DiscardDelayMaxMs + 1);
    }

    // The slot discarded last has emptied, so its copies no longer count as looted: later copies came another way.
    private static void ConsumeLastSlot()
    {
        if (lastItemId == 0 || lastQuantity <= 0)
        {
            return;
        }

        var at = IndexOf(lastItemId, lastHighQuality);
        if (at >= 0)
        {
            var left = pending[at].Quantity - lastQuantity;
            if (left > 0)
            {
                pending[at] = pending[at] with { Quantity = left };
            }
            else
            {
                pending.RemoveAt(at);
            }

            Revision++;
        }

        lastQuantity = 0;
    }

    private static void ConsumeLastSlotIfGone()
    {
        if (lastItemId != 0 && !SlotHolds(lastContainer, lastSlot, lastItemId, lastHighQuality))
        {
            ConsumeLastSlot();
        }
    }

    // The quality counts too: the slot may hold the item again in the other quality, which was never listed.
    private static bool SlotHolds(InventoryType container, ushort slot, uint itemId, bool highQuality)
    {
        var inventory = InventoryManager.Instance();
        var bag = inventory is null ? null : inventory->GetInventoryContainer(container);
        var item = bag is null ? null : bag->GetInventorySlot(slot);
        return item is not null && item->ItemId == itemId && item->IsHighQuality() == highQuality;
    }

    private static int SlotQuantity(InventoryType container, ushort slot)
    {
        var inventory = InventoryManager.Instance();
        var bag = inventory is null ? null : inventory->GetInventoryContainer(container);
        var item = bag is null ? null : bag->GetInventorySlot(slot);
        return item is null ? 0 : (int)item->Quantity;
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
