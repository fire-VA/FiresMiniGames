using System;
using UnityEngine;

namespace FiresMiniGames.Modules.Common
{
    /// <summary>
    /// Something a television can display: a per-frame source of a screen texture with its own input pump. DOOM
    /// (DoomRuntime) and the NES (NesRuntime) both implement it, so a placed TV can host either — its own DOOM on
    /// direct interact, or a nearby NES console's game handed to it. The TV owns the session (camera, screen
    /// materials, teardown) and just asks the console for its <see cref="Texture"/> each frame and calls
    /// <see cref="Tick"/>.
    /// </summary>
    public interface IScreenConsole : IDisposable
    {
        Texture2D Texture { get; }
        bool Available { get; }
        void Tick(float dt);
    }
}
