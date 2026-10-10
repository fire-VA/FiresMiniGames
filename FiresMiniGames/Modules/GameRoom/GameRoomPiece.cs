using UnityEngine;

namespace FiresMiniGames.Modules.GameRoom
{
    /// <summary>
    /// Decorative game-room piece (TV, pool table, chair, arcade decor, the HD room, ...). On placement it rebinds the
    /// bundled Standard materials to the game's resident shader by name — curing the stripped-variant magenta the same
    /// way the arcade machines do. No game logic here; the pool/TV drivers (other work) attach their own behaviours.
    /// </summary>
    public class GameRoomPiece : MonoBehaviour
    {
        private void Awake()
        {
            try { Machines.MachinePieces.RebindBundleShaders(gameObject); } catch { }
        }
    }
}
