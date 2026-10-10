using UnityEngine;
using HarmonyLib;

namespace FiresMiniGames.Modules.DoomTv
{
    /// <summary>
    /// Leaving a TV / arcade session with Escape must not pop Valheim's pause menu. The session reads Escape through
    /// raw <c>Input.GetKeyDown</c> and ends at once, but Valheim opens the pause menu on its own
    /// <c>ZInput.GetKeyDown(Escape)</c> in Menu.Update → <see cref="Menu.Show"/>. During play the shared FiresInputBlock
    /// gates that ZInput edge so the menu stays shut; on the exit frame EndSession releases the block <b>before</b>
    /// Menu.Update runs, so the same Escape leaks through and opens the pause menu. Once it is up, Menu.IsVisible()
    /// forces Player.TakeInput() false and the HUD loses its hammer tabs and inventory until the menu is toggled again
    /// — which is why leaving the arcade cabinet left the player with a dead HUD. Suppress Menu.Show while a session
    /// owns the screen (<see cref="FiresCore.UI.MiniGameCamera.Active"/>) and for the one frame it releases; a later,
    /// deliberate Escape (session long gone) opens it normally. Client only — the dedicated server has no session.
    /// </summary>
    [HarmonyPatch(typeof(Menu), "Show")]
    internal static class SessionPauseMenuGuard
    {
        internal static int EndedFrame = -10;
        private static bool Prepare() => !Application.isBatchMode;
        private static bool Prefix() =>
            !(FiresCore.UI.MiniGameCamera.Active || (Time.frameCount - EndedFrame) <= 1);
    }

    /// <summary>Stamps the frame a session's camera lock ends so <see cref="SessionPauseMenuGuard"/> also swallows the
    /// exact Escape that ends the session — the frame the input block releases, which the Active check alone misses if
    /// Menu.Update runs after EndSession that frame.</summary>
    [HarmonyPatch(typeof(FiresCore.UI.MiniGameCamera), "End")]
    internal static class SessionEndFrameMarker
    {
        private static bool Prepare() => !Application.isBatchMode;
        private static void Postfix() => SessionPauseMenuGuard.EndedFrame = Time.frameCount;
    }
}
