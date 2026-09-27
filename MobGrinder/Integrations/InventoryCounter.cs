using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;

namespace MobGrinder;

public sealed class InventoryCounter(IGameInventory inventory)
{
    private static readonly GameInventoryType[] PlayerBags =
    [
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4,
        GameInventoryType.KeyItems,
    ];

    public int Count(uint itemId)
    {
        if (itemId == 0)
            return 0;
        int total = 0;
        foreach (GameInventoryType bag in PlayerBags)
        {
            foreach (ref readonly GameInventoryItem item in inventory.GetInventoryItems(bag))
            {
                if (!item.IsEmpty && item.BaseItemId == itemId)
                    total += item.Quantity;
            }
        }
        return total;
    }
}
