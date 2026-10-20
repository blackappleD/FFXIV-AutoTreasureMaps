using ECommons;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoTreasureMaps.Windows;

// The game's own item detail tooltip for an item the mouse rests on in our window: hung on _ScreenText every frame while
// the item is hovered, placed by the game, and open to other plugins that add lines to it (market prices and the like).
// Written against FFXIVClientStructs' AgentItemDetail and AtkUnitBase. Draw code calls Hover; the window calls Update once per frame before it
// draws, so the tooltip follows last frame's hover and goes away a frame after the mouse leaves.
internal static unsafe class ItemTooltip
{
    private const string HostAddon = "_ScreenText";

    private static uint hovered;
    private static bool shown;

    public static void Hover(uint itemId) => hovered = itemId;

    public static void Update()
    {
        if (hovered != 0)
        {
            Show(hovered);
        }
        else
        {
            Hide();
        }

        hovered = 0;
    }

    public static void Hide()
    {
        hovered = 0;
        if (!shown)
        {
            return;
        }

        shown = false;
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(HostAddon, out var host))
        {
            AtkStage.Instance()->TooltipManager.HideTooltip(host->Id);
        }
    }

    private static void Show(uint itemId)
    {
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(HostAddon, out var host))
        {
            return;
        }

        var args = stackalloc AtkTooltipManager.AtkTooltipArgs[1];
        args->Ctor();
        args->ItemArgs.Kind = DetailKind.ItemId;
        args->ItemArgs.ItemId = (int)itemId;
        AtkStage.Instance()->TooltipManager.ShowTooltip(AtkTooltipType.Item, host->Id, host->RootNode, args);
        shown = true;
    }
}
