using FiresMiniGames.Modules.Nes;
using HarmonyLib;
using UnityEngine;

namespace FiresMiniGames.Managers
{
    /// <summary>
    /// Registers everything in <see cref="FiresMiniGames.piecePrefabs"/> with ZNetScene (client + server) and adds
    /// the buildable ones to the hammer's piece table. Prefabs must be bundle assets kept alive with
    /// DontDestroyOnLoad — never runtime GameObjects (the m_prefabs trap).
    /// </summary>
    internal static class PieceRegistration
    {
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake_Patch
        {
            private static void Postfix(ZNetScene __instance)
            {
                foreach (var prefab in FiresMiniGames.piecePrefabs) Register(__instance, prefab);
                foreach (var prefab in FiresMiniGames.itemPrefabs) Register(__instance, prefab);   // cartridges spawn as world drops
            }

            private static void Register(ZNetScene scene, GameObject prefab)
            {
                if (prefab == null) return;
                int hash = prefab.name.GetStableHashCode();
                if (scene.m_namedPrefabs.ContainsKey(hash)) return;
                scene.m_prefabs.Add(prefab);
                scene.m_namedPrefabs[hash] = prefab;
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake_Patch
        {
            private static void Postfix(ObjectDB __instance)
            {
                // Menu-scene ObjectDB has no items — the in-game Awake does the real add.
                var hammer = __instance.GetItemPrefab("Hammer");
                var table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces : null;
                if (table == null) return;   // menu ObjectDB — skip; the in-game one runs the real registration
                foreach (var prefab in FiresMiniGames.piecePrefabs)
                {
                    if (prefab == null || prefab.GetComponent<Piece>() == null) continue;
                    if (!table.m_pieces.Contains(prefab)) table.m_pieces.Add(prefab);
                }

                // Mint the cartridge item now (needs the display-cart pieces loaded + a vanilla item to base shared
                // data on), then register every queued item prefab so ObjectDB can hand them out / restore them.
                Cartridge.EnsureTemplate(__instance);
                bool added = false;
                foreach (var prefab in FiresMiniGames.itemPrefabs)
                {
                    if (prefab == null || prefab.GetComponent<ItemDrop>() == null) continue;
                    if (!__instance.m_items.Contains(prefab)) { __instance.m_items.Add(prefab); added = true; }

                    // If ZNetScene already woke, mirror the registration there too (Awake order isn't guaranteed).
                    int hash = prefab.name.GetStableHashCode();
                    if (ZNetScene.instance != null && !ZNetScene.instance.m_namedPrefabs.ContainsKey(hash))
                    {
                        ZNetScene.instance.m_prefabs.Add(prefab);
                        ZNetScene.instance.m_namedPrefabs[hash] = prefab;
                    }
                }
                // Rebuild ObjectDB's hash + shared-data lookups so GetItemPrefab(name)/AddItem see the new items.
                if (added) RebuildRegisters(__instance);
            }

            // ObjectDB.UpdateRegisters() is private (clears + rebuilds m_itemByHash and m_itemByData from m_items).
            // Invoke it by reflection so this doesn't depend on publicized-assembly runtime access.
            private static void RebuildRegisters(ObjectDB odb)
            {
                try
                {
                    typeof(ObjectDB).GetMethod("UpdateRegisters",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?.Invoke(odb, null);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"{FiresMiniGames.PluginName}: ObjectDB.UpdateRegisters failed — cartridges may not be craftable: {e}");
                }
            }
        }
    }
}
