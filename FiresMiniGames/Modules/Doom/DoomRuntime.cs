using System;
using System.Collections.Generic;
using ManagedDoom;
using ManagedDoom.Audio;
using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.DoomTv
{
    /// <summary>
    /// Owns one running DOOM: constructs the vendored engine against an IWAD found in <paramref name="wadDir"/>
    /// (Freedoom by default, or any doom1/doom2/etc. the player drops there), jumps straight into a new game, and
    /// ticks it at DOOM's native 35 Hz. Video renders to a Texture2D (see UnityDoomVideo); held keys drive the
    /// player through UnityDoomInput; discrete keys (menu, prompts) are posted here. Audio is null for now.
    /// </summary>
    public sealed class DoomRuntime : IScreenConsole
    {
        private const float TicRate = 35f;

        private Doom _doom;
        private UnityDoomVideo _video;
        private UnityDoomInput _input;
        private UnityDoomSound _sound;
        private float _acc;
        private readonly bool[] _down = new bool[512];

        public bool Available { get; private set; }
        public string Error { get; private set; }
        public Texture2D Texture => _video != null ? _video.Texture : null;

        public DoomRuntime(string wadDir, int skill)
        {
            try
            {
                ConfigUtilities.BaseDirOverride = wadDir;
                var args = new CommandLineArgs(new string[0]);
                var config = new Config();
                config.video_highresolution = false;   // 320x200 — authentic and cheap to blit each tic
                var content = new GameContent(args);
                _video = new UnityDoomVideo(config, content);
                _input = new UnityDoomInput();
                _sound = new UnityDoomSound(content);
                _doom = new Doom(args, config, content, _video, _sound, new NullMusic(), _input);
                _doom.NewGame((GameSkill)Mathf.Clamp(skill, 0, 4), 1, 1);   // straight into E1M1
                Available = true;
            }
            catch (Exception e)
            {
                Error = e.Message;
                Available = false;
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] DOOM init failed: {e.Message}");
            }
        }

        // Called every Unity frame; advances the sim in fixed 35 Hz tics and repaints the screen texture.
        public void Tick(float dt)
        {
            if (!Available) return;
            PostKeys();
            _acc += Mathf.Min(dt, 0.25f);
            float step = 1f / TicRate;
            bool ticked = false;
            while (_acc >= step)
            {
                _acc -= step;
                if (_doom.Update() == UpdateResult.Completed) { /* demo/quit — ignore, session owns exit */ }
                ticked = true;
            }
            if (ticked) _video.Render(_doom, Fixed.Zero);
        }

        // Discrete key edges → DOOM events (menus, prompts). Escape is deliberately NOT forwarded: the TV session
        // owns Escape to step back, and the game is already in progress so no menu is needed to start.
        private void PostKeys()
        {
            foreach (var kv in KeyMap)
            {
                int code = (int)kv.Key;
                if (code < 0 || code >= _down.Length) continue;
                bool now = Input.GetKey(kv.Key);
                if (now == _down[code]) continue;
                _down[code] = now;
                _doom.PostEvent(new DoomEvent(now ? ManagedDoom.EventType.KeyDown : ManagedDoom.EventType.KeyUp, kv.Value));
            }
        }

        private static readonly Dictionary<KeyCode, DoomKey> KeyMap = BuildKeyMap();

        private static Dictionary<KeyCode, DoomKey> BuildKeyMap()
        {
            var m = new Dictionary<KeyCode, DoomKey>();
            for (int i = 0; i < 26; i++) m[KeyCode.A + i] = DoomKey.A + i;
            m[KeyCode.Alpha0] = DoomKey.Num0;
            for (int i = 1; i <= 9; i++) m[KeyCode.Alpha0 + i] = DoomKey.Num1 + (i - 1);
            m[KeyCode.UpArrow] = DoomKey.Up; m[KeyCode.DownArrow] = DoomKey.Down;
            m[KeyCode.LeftArrow] = DoomKey.Left; m[KeyCode.RightArrow] = DoomKey.Right;
            m[KeyCode.Return] = DoomKey.Enter; m[KeyCode.KeypadEnter] = DoomKey.Enter;
            m[KeyCode.Space] = DoomKey.Space; m[KeyCode.Backspace] = DoomKey.Backspace;
            m[KeyCode.Tab] = DoomKey.Tab;
            m[KeyCode.LeftShift] = DoomKey.LShift; m[KeyCode.RightShift] = DoomKey.RShift;
            m[KeyCode.LeftControl] = DoomKey.LControl; m[KeyCode.RightControl] = DoomKey.RControl;
            m[KeyCode.LeftAlt] = DoomKey.LAlt; m[KeyCode.RightAlt] = DoomKey.RAlt;
            return m;
        }

        public void Dispose()
        {
            try { _doom?.Quit(); } catch { }
            if (_video != null && _video.Texture != null) UnityEngine.Object.Destroy(_video.Texture);
            _sound?.Dispose();
            _doom = null; _video = null; _input = null; _sound = null;
        }
    }
}
