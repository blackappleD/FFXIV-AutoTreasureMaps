using AutoTreasureMaps.Core.Stats;
using ECommons;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace AutoTreasureMaps.Core.Dungeon;

// The Gambler's Lure window a dungeon coffer can open with. Its challenge button deals the cards for one more share of
// the treasure; its open-the-coffer button takes the treasure as it stands and ends the game. The window shows those
// buttons only on the character's own coffer. Read and clicked only on the framework thread.
internal static unsafe class LureDialog
{
    private const string AddonName = "TreasureHighLow";

    // The window's values list the treasure as it stands, then the treasure a won guess would bring. Each list starts
    // with how many entries it has, and each entry takes nine values: a kind, the item's name (in the colour of its
    // rarity), its icon and its quantity, then flags. An item that does not stack takes one entry for every piece, with quantity 0.
    private const int CurrentListIndex = 4;
    private const int EntryStride = 9;
    private const int MaxEntries = 12;
    private const int NameOffset = 2;

    private static Dictionary<string, uint>? rareByName;

    public static bool Open() => TryGet(out _);

    public static bool OpenCoffer()
    {
        if (!TryGet(out var addon))
        {
            return false;
        }

        var lure = new AddonMaster.TreasureHighLow(addon);
        if (lure.OpenChestButton is null || !lure.OpenChestButton->IsEnabled)
        {
            return false;
        }

        lure.OpenChest();
        return true;
    }

    public static bool Challenge()
    {
        if (!TryGet(out var addon))
        {
            return false;
        }

        var lure = new AddonMaster.TreasureHighLow(addon);
        if (lure.TryLuckButton is null || !lure.TryLuckButton->IsEnabled)
        {
            return false;
        }

        lure.TryLuck();
        return true;
    }

    // The rare loot in the treasure as it stands, by item id; false when the window is not up or its list unreadable.
    public static bool TryReadRareLoot(out List<uint> rare, out int entries)
    {
        rare = [];
        entries = 0;
        if (!TryGet(out var addon) || addon->AtkValues is null || addon->AtkValuesCount <= CurrentListIndex)
        {
            return false;
        }

        var values = addon->AtkValues;
        var count = values[CurrentListIndex];
        if (count.Type != AtkValueType.UInt || count.UInt > MaxEntries)
        {
            return false;
        }

        var lookup = RareByName();
        for (var entry = 0; entry < count.UInt; entry++)
        {
            var at = CurrentListIndex + (entry * EntryStride);
            if (at + NameOffset >= addon->AtkValuesCount)
            {
                return false;
            }

            var name = values[at + NameOffset];
            if (name.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString) || !name.String.HasValue)
            {
                continue;
            }

            var text = new ReadOnlySeStringSpan(name.String.AsSpan()).ExtractText();
            if (text.Length == 0)
            {
                continue;
            }

            entries++;
            if (lookup.TryGetValue(text, out var itemId) && !rare.Contains(itemId))
            {
                rare.Add(itemId);
            }
        }

        return entries > 0;
    }

    // The window names its items rather than giving their ids, so the rare loot is matched by name.
    private static Dictionary<string, uint> RareByName()
    {
        if (rareByName is { } built)
        {
            return built;
        }

        built = [];
        var items = Svc.Data.GetExcelSheet<Item>();
        foreach (var itemId in RareLoot.AllItems)
        {
            if (items.GetRowOrDefault(itemId) is { } item)
            {
                built.TryAdd(item.Name.ExtractText(), itemId);
            }
        }

        rareByName = built;
        return built;
    }

    private static bool TryGet(out AtkUnitBase* addon)
        => GenericHelpers.TryGetAddonByName(AddonName, out addon) && GenericHelpers.IsAddonReady(addon);
}
