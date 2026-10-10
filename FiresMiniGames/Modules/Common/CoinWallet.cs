using UnityEngine;

namespace FiresMiniGames.Modules.Common
{
    /// <summary>
    /// Wager purse for the machines — the player's inventory coins ARE the stake. Same inventory discipline as
    /// FiresRPGmaker's gambler suite: count by drop-prefab name, spend via Inventory.RemoveItem per stack, pay via
    /// AddItem with a ground-drop fallback. Drops are chunked at the item's max stack so a jackpot never bakes an
    /// over-cap stack into the world (those clamp silently on reload).
    /// </summary>
    public static class CoinWallet
    {
        public static string CoinPrefab
        {
            get
            {
                var v = MiniGamesConfig.CurrencyItem != null ? MiniGamesConfig.CurrencyItem.Value : null;
                return string.IsNullOrEmpty(v) ? "Coins" : v.Trim();
            }
        }

        public static int Count(Player player)
        {
            if (player == null) return 0;
            int total = 0;
            foreach (var it in player.GetInventory().GetAllItems())
                if (it?.m_dropPrefab != null && it.m_dropPrefab.name == CoinPrefab) total += it.m_stack;
            return total;
        }

        public static bool TrySpend(Player player, int amount)
        {
            if (player == null || amount <= 0) return false;
            var inv = player.GetInventory();
            if (Count(player) < amount) return false;
            int remaining = amount;
            foreach (var it in inv.GetAllItems().ToArray())
            {
                if (remaining <= 0) break;
                if (it?.m_dropPrefab == null || it.m_dropPrefab.name != CoinPrefab) continue;
                int take = Mathf.Min(it.m_stack, remaining);
                inv.RemoveItem(it, take);
                remaining -= take;
            }
            return remaining == 0;
        }

        public static void Pay(Player player, int amount)
        {
            if (player == null || amount <= 0) return;
            var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(CoinPrefab) : null;
            var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] unknown currency prefab '{CoinPrefab}' — payout lost");
                return;
            }
            var inv = player.GetInventory();
            if (inv.CanAddItem(drop.m_itemData, amount))
            {
                inv.AddItem(CoinPrefab, amount, 1, 0, 0L, "", false);
                return;
            }
            int maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            int remaining = amount;
            while (remaining > 0)
            {
                int chunk = Mathf.Min(maxStack, remaining);
                remaining -= chunk;
                var go = Object.Instantiate(pf,
                    player.transform.position + player.transform.forward * 1.2f + Vector3.up * 1.5f,
                    Quaternion.identity);
                var dr = go.GetComponent<ItemDrop>();
                if (dr != null) dr.m_itemData.m_stack = chunk;
            }
        }
    }
}
