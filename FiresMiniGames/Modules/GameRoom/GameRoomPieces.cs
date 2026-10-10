using System.Linq;
using UnityEngine;

namespace FiresMiniGames.Modules.GameRoom
{
    /// <summary>
    /// Loads the loose <c>vaitems_minigames</c> bundle (curated game-room furniture/decor + the HD room, imported from
    /// downloaded packs) and queues its buildable Pieces for ZNetScene + hammer registration. Kept LOOSE beside the DLL
    /// (the HD room texture is large) rather than embedded, to keep the DLL small.
    /// </summary>
    public static class GameRoomPieces
    {
        public static void Load(AssetBundle bundle)
        {
            if (bundle == null) return;
            int n = 0;
            foreach (var prefab in bundle.LoadAllAssets<GameObject>())
            {
                if (prefab == null || prefab.GetComponent<Piece>() == null) continue;
                prefab.SetActive(true);
                UnityEngine.Object.DontDestroyOnLoad(prefab);
                if (prefab.GetComponent<GameRoomPiece>() == null) prefab.AddComponent<GameRoomPiece>();
                if (!FiresMiniGames.piecePrefabs.Contains(prefab)) { FiresMiniGames.piecePrefabs.Add(prefab); n++; }
            }
            Debug.Log($"[FiresMiniGames] GameRoomPieces loaded {n} game-room piece(s)");
        }
    }
}
