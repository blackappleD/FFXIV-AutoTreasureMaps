using ECommons;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoTreasureMaps.Core.Dungeon;

// The Gambler's Lure window a dungeon coffer can open with. Its open-the-coffer button takes the treasure as it stands
// and skips the high-or-low game; the window shows that button only on the character's own coffer. Read and clicked
// only on the framework thread.
internal static unsafe class LureDialog
{
    private const string AddonName = "TreasureHighLow";

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

    private static bool TryGet(out AtkUnitBase* addon)
        => GenericHelpers.TryGetAddonByName(AddonName, out addon) && GenericHelpers.IsAddonReady(addon);
}
