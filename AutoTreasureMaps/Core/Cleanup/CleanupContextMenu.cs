using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.Gui.ContextMenu;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoTreasureMaps.Core.Cleanup;

// An entry in the right-click menu of an inventory item or a chat log item link that puts the item on the loot cleanup
// whitelist, or takes it off. Shown only while auto-discard is on, and never for a map, rare loot (picked in the
// settings) or an item the game keeps.
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
        if (!Plugin.Instance.Configuration.AutoDiscard)
        {
            return;
        }

        var itemId = MenuItemId(args);
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

    private static uint MenuItemId(IMenuOpenedArgs args)
    {
        if (args.MenuType == ContextMenuType.Inventory)
        {
            return args.Target is MenuTargetInventory { TargetItem: { } item } ? item.BaseItemId : 0;
        }

        return args.AddonName == "ChatLog" ? ChatLinkItemId() : 0;
    }

    // The item behind a right-clicked chat item link. The uint 8 bytes after ContextItemId is 3 only when the menu opened on
    // an item link (GatherBuddy reads it the same way); otherwise ContextItemId still holds the last link's item. A link
    // carries the HQ or collectable offset, which the whitelist does not.
    private static unsafe uint ChatLinkItemId()
    {
        var agent = AgentChatLog.Instance();
        if (agent == null || (&agent->ContextItemId)[2] != 3)
        {
            return 0;
        }

        var itemId = agent->ContextItemId;
        return itemId switch
        {
            >= 2_000_000 => itemId,
            >= 1_000_000 => itemId - 1_000_000,
            >= 500_000 => itemId - 500_000,
            _ => itemId,
        };
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
