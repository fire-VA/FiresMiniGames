using UnityEngine;
using HarmonyLib;

namespace FiresMiniGames.Modules.DoomTv
{
    /// <summary>
    /// While a TV / arcade session owns the screen (<see cref="FiresCore.UI.MiniGameCamera.Active"/>), Valheim's chat
    /// must not steal keystrokes. It opens on a raw <c>Input.GetKeyDown(Return)</c>, which the shared FiresInputBlock
    /// can't gate (raw UnityEngine.Input is an unpatchable extern), so pressing Enter as the NES <b>Start</b> button
    /// would pop the chat box instead of registering. Skipping Chat.Update for the duration of a session keeps Enter
    /// a clean Start and, as a bonus, stops any other typed keys leaking into chat. Everything resumes the instant the
    /// session ends. Never patched on a headless dedicated server (no local session, and Chat there relays messages).
    /// </summary>
    [HarmonyPatch(typeof(Chat), "Update")]
    internal static class TvChatGuard
    {
        private static bool Prepare() => !Application.isBatchMode;   // client only — dedi has no minigame session
        private static bool Prefix() => !FiresCore.UI.MiniGameCamera.Active;
    }
}
