using UnityEngine;
using HarmonyLib;

namespace FiresMiniGames.Modules.DoomTv
{
    /// <summary>
    /// While a TV / arcade session owns the screen (<see cref="FiresCore.UI.MiniGameCamera.Active"/>), hide Valheim's
    /// world hover tooltip — otherwise the crosshair keeps showing "Television [E] Turn on" over the picture. The
    /// hover label is blanked from a postfix on Hud.UpdateCrosshair, which runs every frame, so it self-restores the
    /// instant the session ends. Client-only (a headless server has no Hud).
    /// </summary>
    [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
    internal static class TvHoverGuard
    {
        private static bool Prepare() => !Application.isBatchMode;

        private static void Postfix(Hud __instance)
        {
            if (!FiresCore.UI.MiniGameCamera.Active || __instance == null) return;
            if (__instance.m_hoverName != null) __instance.m_hoverName.text = "";
        }
    }
}
