using ManagedDoom;
using ManagedDoom.UserInput;
using UnityEngine;

namespace FiresMiniGames.Modules.DoomTv
{
    /// <summary>
    /// IUserInput reading held Unity keys into a TicCmd, replicating the reference SilkUserInput movement model
    /// (walk/run speeds, slow-turn ramp) with modern FPS handling: W/S (or up/down) move, A/D strafe (Q/E too),
    /// left/right arrows turn, the MOUSE turns (the marine has no vertical look — DOOM auto-aims), Ctrl/LMB fire,
    /// Space uses, Shift runs, 1-7 switch weapons. Discrete menu keys are posted separately by DoomRuntime.
    /// </summary>
    public sealed class UnityDoomInput : IUserInput
    {
        private int _turnHeld;

        public void BuildTicCmd(TicCmd cmd)
        {
            bool fwd = K(KeyCode.W) || K(KeyCode.UpArrow);
            bool back = K(KeyCode.S) || K(KeyCode.DownArrow);
            bool turnL = K(KeyCode.LeftArrow);        // arrows still turn (keyboard-only fallback)
            bool turnR = K(KeyCode.RightArrow);
            bool strafeL = K(KeyCode.A) || K(KeyCode.Q);   // A/D strafe — the mouse turns
            bool strafeR = K(KeyCode.D) || K(KeyCode.E);
            bool fire = K(KeyCode.LeftControl) || K(KeyCode.RightControl) || Input.GetMouseButton(0);
            bool use = K(KeyCode.Space);
            bool run = K(KeyCode.LeftShift) || K(KeyCode.RightShift);
            bool strafeMod = K(KeyCode.LeftAlt) || K(KeyCode.RightAlt);

            cmd.Clear();

            int speed = run ? 1 : 0;
            int forward = 0, side = 0;

            if (turnL || turnR) _turnHeld++; else _turnHeld = 0;
            int turnSpeed = _turnHeld < PlayerBehavior.SlowTurnTics ? 2 : speed;

            if (strafeMod)
            {
                if (turnR) side += PlayerBehavior.SideMove[speed];
                if (turnL) side -= PlayerBehavior.SideMove[speed];
            }
            else
            {
                if (turnR) cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
                if (turnL) cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
            }

            if (fwd) forward += PlayerBehavior.ForwardMove[speed];
            if (back) forward -= PlayerBehavior.ForwardMove[speed];
            if (strafeL) side -= PlayerBehavior.SideMove[speed];
            if (strafeR) side += PlayerBehavior.SideMove[speed];

            // Mouse turn — the marine turns with the mouse (the cursor is locked during play, but a locked cursor
            // still reports delta). Right = turn right (subtract, DOOM convention). Config sets the speed; 0 = off.
            float sens = Common.MiniGamesConfig.DoomMouseSensitivity != null ? Common.MiniGamesConfig.DoomMouseSensitivity.Value : 5f;
            if (sens > 0f)
            {
                float mx = Input.GetAxisRaw("Mouse X");
                if (!Mathf.Approximately(mx, 0f))
                {
                    // 80x gain (was 8x) — ~10x snappier so the default feels right; clamp raised to allow fast flicks.
                    int add = Mathf.Clamp((int)(mx * sens * 80f), -8000, 8000);
                    cmd.AngleTurn = (short)Mathf.Clamp(cmd.AngleTurn - add, short.MinValue, short.MaxValue);
                }
            }

            if (fire) cmd.Buttons |= TicCmdButtons.Attack;
            if (use) cmd.Buttons |= TicCmdButtons.Use;

            for (int i = 0; i < 7; i++)
                if (K(KeyCode.Alpha1 + i))
                {
                    cmd.Buttons |= TicCmdButtons.Change;
                    cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
                    break;
                }

            forward = Mathf.Clamp(forward, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
            side = Mathf.Clamp(side, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
            cmd.ForwardMove += (sbyte)forward;
            cmd.SideMove += (sbyte)side;
        }

        private static bool K(KeyCode c) => Input.GetKey(c);

        public void Reset() => _turnHeld = 0;
        public void GrabMouse() { }
        public void ReleaseMouse() { }
        public int MaxMouseSensitivity => 15;
        public int MouseSensitivity { get; set; } = 8;
    }
}
