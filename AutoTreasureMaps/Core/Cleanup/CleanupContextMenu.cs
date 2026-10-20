using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.Gui.ContextMenu;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Cleanup;

// An entry in the inventory's right-click menu that puts the item on the loot cleanup whitelist, or takes it off. Shown
// only while auto-discard is on, and never for a map, rare loot (picked in the settings) or an item the game keeps.
internal sealed class CleanupContextMenu : IDisposable
{
    private const char MenuPrefix = 'A';

    public CleanupContextMenu()
    {
        Svc.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose()
    {
        Svc.ContextMenu.OnMenuOpened -= OnMenuOpened;
    }

    private static void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Inventory
            || !Plugin.Instance.Configuration.AutoDiscard
            || args.Target is not MenuTargetInventory { TargetItem: { } item })
        {
            return;
        }

        var itemId = item.BaseItemId;
        if (itemId == 0 || LootCleanup.IsAlwaysKept(itemId) || LootCleanup.KeepsRare(itemId))
        {
            return;
        }

        var listed = LootCleanup.OnWhitelist(itemId);
        args.AddMenuItem(new MenuItem
        {
            Name = Loc.T(listed ? L.Cleanup.MenuRemove : L.Cleanup.MenuAdd),
            PrefixChar = MenuPrefix,
            OnClicked = _ => Toggle(itemId, listed),
        });
    }

    private static void Toggle(uint itemId, bool listed)
    {
        var name = NotableLoot.Name(itemId);
        if (listed)
        {
            LootCleanup.RemoveFromWhitelist(itemId);
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Cleanup.ChatUnkept, name)}");
            return;
        }

        LootCleanup.AddToWhitelist(itemId);
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Cleanup.ChatKept, name)}");
    }
}
