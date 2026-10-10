using System;
using System.Collections.Generic;
using UnityEngine;

namespace FiresMiniGames.Modules.Pacman
{
    /// <summary>
    /// The whole arcade game in one sim: the classic 28x31 maze, dots and power pellets, four ghosts running the
    /// authentic personalities (chase target per ghost, scatter/chase waves, frightened, eyes home), fruit, lives
    /// and levels — rendered as procedural pixel art into a 224x288 Color32 buffer the cabinet screen displays.
    /// Fixed 60 Hz steps; all randomness from a private Random so a run is self-contained.
    /// </summary>
    public sealed class PacSim
    {
        public const int W = 224, H = 288;
        private const int Cols = 28, Rows = 31;
        private const int Tile = 8;
        private const int MazeTop = 24;                     // px; 3 HUD rows above, 2 below

        public int Score { get; private set; }
        public int HighScore;
        public bool Over { get; private set; }
        public int Level { get; private set; } = 1;

        private enum Phase { Ready, Play, Dying, LevelFlash, GameOver }
        private Phase _phase = Phase.Ready;
        private float _phaseT;

        private static readonly string[] MazeSrc =
        {
            "############################",
            "#............##............#",
            "#.####.#####.##.#####.####.#",
            "#o####.#####.##.#####.####o#",
            "#.####.#####.##.#####.####.#",
            "#..........................#",
            "#.####.##.########.##.####.#",
            "#.####.##.########.##.####.#",
            "#......##....##....##......#",
            "######.##### ## #####.######",
            "     #.##### ## #####.#     ",
            "     #.##          ##.#     ",
            "     #.## ###--### ##.#     ",
            "######.## #      # ##.######",
            "      .   #      #   .      ",
            "######.## #      # ##.######",
            "     #.##          ##.#     ",
            "     #.##          ##.#     ",
            "     #.## ######## ##.#     ",
            "######.## ######## ##.######",
            "#............##............#",
            "#.####.#####.##.#####.####.#",
            "#.####.#####.##.#####.####.#",
            "#o..##................##..o#",
            "###.##.##.########.##.##.###",
            "###.##.##.########.##.##.###",
            "#......##....##....##......#",
            "#.##########.##.##########.#",
            "#.##########.##.##########.#",
            "#..........................#",
            "############################",
        };

        private readonly char[,] _maze = new char[Cols, Rows];
        private int _dotsLeft;
        private int _dotsEaten;

        private struct Actor
        {
            public float X, Y;                              // tile units, centre of tile = +0.5
            public int Dx, Dy;
            public float Speed;                             // tiles/s
        }

        private Actor _pac;
        private int _wantDx, _wantDy;
        private float _mouth;                               // chomp phase
        private float _pacIdle;

        private enum GMode { InHouse, Leaving, Normal, Fright, Eyes }
        private struct Ghost
        {
            public Actor A;
            public GMode Mode;
            public float LeaveT;
            public int ScatterX, ScatterY;
        }
        private readonly Ghost[] _ghosts = new Ghost[4];
        private float _modeT;                               // scatter/chase wave clock
        private float _frightT;
        private int _frightCombo;
        private float _freezeT;                             // eat-ghost score pause
        private float _flashT;

        private float _fruitT;
        private bool _fruitUp;
        private int _fruitShown;

        public int Lives { get; private set; } = 3;
        private readonly System.Random _rng = new System.Random();
        private readonly Action<int> _onGameOver;
        private float _acc;

        public PacSim(int highScore, Action<int> onGameOver)
        {
            HighScore = highScore;
            _onGameOver = onGameOver;
            LoadMaze();
            ResetActors();
            _phase = Phase.Ready;
            _phaseT = 2.1f;
        }

        private void LoadMaze()
        {
            _dotsLeft = 0;
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    char c = MazeSrc[y][x];
                    _maze[x, y] = c;
                    if (c == '.' || c == 'o') _dotsLeft++;
                }
        }

        private void ResetActors()
        {
            _pac = new Actor { X = 14f, Y = 23.5f, Dx = -1, Dy = 0, Speed = PacSpeed() };
            _wantDx = -1; _wantDy = 0;
            _mouth = 0f;

            int[,] start = { { 14, 11 }, { 14, 14 }, { 12, 14 }, { 16, 14 } };
            int[,] corners = { { 26, 1 }, { 1, 1 }, { 26, 29 }, { 1, 29 } };
            float[] leave = { 0f, 1.2f, 4.5f, 8f };
            for (int i = 0; i < 4; i++)
            {
                _ghosts[i].A = new Actor { X = start[i, 0], Y = start[i, 1] + 0.5f, Dx = i == 0 ? -1 : 0, Dy = i == 0 ? 0 : -1, Speed = GhostSpeed() };
                _ghosts[i].A.X += 0f;
                _ghosts[i].Mode = i == 0 ? GMode.Normal : GMode.InHouse;
                _ghosts[i].LeaveT = leave[i] / Mathf.Max(1f, 1f + (Level - 1) * 0.12f);
                _ghosts[i].ScatterX = corners[i, 0];
                _ghosts[i].ScatterY = corners[i, 1];
            }
            _modeT = 0f;
            _frightT = 0f;
            _frightCombo = 0;
            _freezeT = 0f;
            _fruitUp = false;
        }

        private float PacSpeed() => Mathf.Min(7f + (Level - 1) * 0.25f, 8.8f);
        private float GhostSpeed() => Mathf.Min(6.4f + (Level - 1) * 0.28f, 8.4f);
        private float FrightDuration() => Mathf.Max(6.5f - (Level - 1) * 0.75f, 2f);

        // ── stepping ───────────────────────────────────────────────────────────────────────────────────────────
        public void Tick(float dt, int wantDx, int wantDy)
        {
            if (Over) return;
            if (wantDx != 0 || wantDy != 0) { _wantDx = wantDx; _wantDy = wantDy; }
            _acc += Mathf.Min(dt, 0.1f);
            const float step = 1f / 60f;
            while (_acc >= step)
            {
                _acc -= step;
                Step(step);
            }
        }

        private void Step(float dt)
        {
            _flashT += dt;
            switch (_phase)
            {
                case Phase.Ready:
                    _phaseT -= dt;
                    if (_phaseT <= 0f) _phase = Phase.Play;
                    return;
                case Phase.Dying:
                    _phaseT -= dt;
                    if (_phaseT <= 0f)
                    {
                        if (Lives <= 0) { _phase = Phase.GameOver; _phaseT = 3f; }
                        else { ResetActors(); _phase = Phase.Ready; _phaseT = 1.7f; }
                    }
                    return;
                case Phase.LevelFlash:
                    _phaseT -= dt;
                    if (_phaseT <= 0f)
                    {
                        Level++;
                        LoadMaze();
                        ResetActors();
                        _phase = Phase.Ready;
                        _phaseT = 1.7f;
                    }
                    return;
                case Phase.GameOver:
                    _phaseT -= dt;
                    if (_phaseT <= 0f)
                    {
                        Over = true;
                        _onGameOver?.Invoke(Score);
                    }
                    return;
            }

            if (_freezeT > 0f) { _freezeT -= dt; return; }

            if (_frightT > 0f)
            {
                _frightT -= dt;
                if (_frightT <= 0f)
                {
                    _frightCombo = 0;
                    for (int i = 0; i < 4; i++)
                        if (_ghosts[i].Mode == GMode.Fright) _ghosts[i].Mode = GMode.Normal;
                }
            }
            else _modeT += dt;

            MovePac(dt);
            EatHere();
            for (int i = 0; i < 4; i++) MoveGhost(i, dt);
            Collisions();
            Fruit(dt);
        }

        private static readonly float[] Waves = { 7f, 20f, 7f, 20f, 5f, 20f, 5f };

        private bool Scatter()
        {
            float t = _modeT;
            foreach (var span in Waves)
            {
                if (t < span) return span < 10f;
                t -= span;
            }
            return false;
        }

        private bool Open(int x, int y, bool door)
        {
            if (y == 14 && (x < 0 || x >= Cols)) return true;    // tunnel
            if (x < 0 || x >= Cols || y < 0 || y >= Rows) return false;
            char c = _maze[x, y];
            if (c == '#') return false;
            if (c == '-') return door;
            return true;
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        // Continuous move along the current axis; turning and wall stops resolve at tile centres.
        // Centres sit at n + 0.5; the next centre AHEAD along the travel direction is the decision point.
        private void MoveActor(ref Actor a, int wantDx, int wantDy, bool door)
        {
            float dist = a.Speed * (1f / 60f);
            int guard = 0;
            while (dist > 0f && guard++ < 8)
            {
                float centerX = a.Dx > 0 ? Mathf.Ceil(a.X - 0.5f) + 0.5f : a.Dx < 0 ? Mathf.Floor(a.X + 0.5f) - 0.5f : a.X;
                float centerY = a.Dy > 0 ? Mathf.Ceil(a.Y - 0.5f) + 0.5f : a.Dy < 0 ? Mathf.Floor(a.Y + 0.5f) - 0.5f : a.Y;
                float toCenter = a.Dx != 0 ? (centerX - a.X) * a.Dx : (centerY - a.Y) * a.Dy;

                if (toCenter > 0.0001f)
                {
                    float moved = Mathf.Min(dist, toCenter);
                    a.X += a.Dx * moved;
                    a.Y += a.Dy * moved;
                    dist -= moved;
                    Wrap(ref a);
                    if (dist <= 0f) break;
                }

                // at a centre: snap, then decide
                a.X = centerX; a.Y = centerY;
                int tx = Mathf.FloorToInt(a.X), ty = Mathf.FloorToInt(a.Y);
                if ((wantDx != a.Dx || wantDy != a.Dy) && (wantDx != 0 || wantDy != 0) && Open(tx + wantDx, ty + wantDy, door))
                {
                    a.Dx = wantDx; a.Dy = wantDy;
                }
                if (!Open(tx + a.Dx, ty + a.Dy, door)) return;   // wall ahead — parked at centre
                float step2 = Mathf.Min(dist, 0.5f);
                a.X += a.Dx * step2;
                a.Y += a.Dy * step2;
                dist -= step2;
                Wrap(ref a);
            }
        }

        private static void Wrap(ref Actor a)
        {
            if (a.Y < 14f || a.Y >= 15f) return;
            if (a.X < -0.5f) a.X += Cols;
            else if (a.X > Cols - 0.5f) a.X -= Cols;
        }

        private void MovePac(float dt)
        {
            _pac.Speed = PacSpeed() * (_pacIdle > 0f ? 0f : 1f);
            if (_pacIdle > 0f) _pacIdle -= dt;
            var before = new Vector2(_pac.X, _pac.Y);
            MoveActor(ref _pac, _wantDx, _wantDy, door: false);
            if ((before - new Vector2(_pac.X, _pac.Y)).sqrMagnitude > 1e-8f) _mouth += dt * 10f;
        }

        private void EatHere()
        {
            int tx = Mathf.FloorToInt(_pac.X), ty = Mathf.FloorToInt(_pac.Y);
            if (tx < 0 || tx >= Cols || ty < 0 || ty >= Rows) return;
            char c = _maze[tx, ty];
            if (c == '.')
            {
                _maze[tx, ty] = ' ';
                Score += 10;
                _dotsLeft--; _dotsEaten++;
                _pacIdle = 1f / 60f;
            }
            else if (c == 'o')
            {
                _maze[tx, ty] = ' ';
                Score += 50;
                _dotsLeft--; _dotsEaten++;
                _frightT = FrightDuration();
                _frightCombo = 0;
                for (int i = 0; i < 4; i++)
                    if (_ghosts[i].Mode == GMode.Normal)
                    {
                        _ghosts[i].Mode = GMode.Fright;
                        _ghosts[i].A.Dx = -_ghosts[i].A.Dx;
                        _ghosts[i].A.Dy = -_ghosts[i].A.Dy;
                    }
            }
            if (Score > HighScore) HighScore = Score;
            if (_dotsLeft <= 0)
            {
                _phase = Phase.LevelFlash;
                _phaseT = 2f;
            }
        }

        private void Fruit(float dt)
        {
            if (!_fruitUp && _fruitShown < 2 && (_dotsEaten >= 70 && _fruitShown == 0 || _dotsEaten >= 170 && _fruitShown == 1))
            {
                _fruitUp = true;
                _fruitT = 9f;
            }
            if (_fruitUp)
            {
                _fruitT -= dt;
                if (_fruitT <= 0f) { _fruitUp = false; _fruitShown++; }
                else if (Mathf.Abs(_pac.X - 14f) < 0.8f && Mathf.Abs(_pac.Y - 17.5f) < 0.8f)
                {
                    _fruitUp = false; _fruitShown++;
                    int bonus = Mathf.Min(100 * Level, 500);
                    Score += bonus;
                    _freezeT = 0.35f;
                }
            }
        }

        // ── ghosts ─────────────────────────────────────────────────────────────────────────────────────────────
        private void MoveGhost(int i, float dt)
        {
            ref var g = ref _ghosts[i];
            float baseSpeed = GhostSpeed();
            bool tunnel = g.A.Y >= 14f && g.A.Y < 15f && (g.A.X < 6f || g.A.X > 22f);
            g.A.Speed = g.Mode == GMode.Eyes ? 12f
                      : g.Mode == GMode.Fright ? baseSpeed * 0.62f
                      : tunnel ? baseSpeed * 0.62f
                      : baseSpeed;

            switch (g.Mode)
            {
                case GMode.InHouse:
                    g.LeaveT -= dt;
                    g.A.Y += g.A.Dy * 2.2f * dt;
                    if (g.A.Y < 13.6f) g.A.Dy = 1;
                    else if (g.A.Y > 14.4f) g.A.Dy = -1;
                    if (g.LeaveT <= 0f) { g.Mode = GMode.Leaving; }
                    return;
                case GMode.Leaving:
                    if (Mathf.Abs(g.A.X - 14f) > 0.06f)
                    {
                        g.A.X = Mathf.MoveTowards(g.A.X, 14f, 2.2f * dt);
                        return;
                    }
                    g.A.X = 14f;
                    g.A.Y = Mathf.MoveTowards(g.A.Y, 11.5f, 2.2f * dt);
                    if (g.A.Y <= 11.5f)
                    {
                        g.Mode = _frightT > 0f ? GMode.Fright : GMode.Normal;
                        g.A.Dx = _rng.Next(2) == 0 ? -1 : 1; g.A.Dy = 0;
                    }
                    return;
            }

            // steer at tile centres toward the mode target
            int tx = Mathf.FloorToInt(g.A.X), ty = Mathf.FloorToInt(g.A.Y);
            bool atCenter = Mathf.Abs(Frac(g.A.X) - 0.5f) < 0.08f && Mathf.Abs(Frac(g.A.Y) - 0.5f) < 0.08f;
            int wantDx = g.A.Dx, wantDy = g.A.Dy;
            if (atCenter)
            {
                Target(i, out float tgx, out float tgy);
                float best = float.MaxValue;
                int bdx = g.A.Dx, bdy = g.A.Dy;
                bool doorOk = g.Mode == GMode.Eyes;
                bool any = false;
                // classic tie-break priority: up, left, down, right
                int[,] dirs = { { 0, -1 }, { -1, 0 }, { 0, 1 }, { 1, 0 } };
                for (int d = 0; d < 4; d++)
                {
                    int dx = dirs[d, 0], dy = dirs[d, 1];
                    if (dx == -g.A.Dx && dy == -g.A.Dy) continue;
                    if (!Open(tx + dx, ty + dy, doorOk)) continue;
                    any = true;
                    float score;
                    if (g.Mode == GMode.Fright) score = (float)_rng.NextDouble();
                    else
                    {
                        float ddx = tx + dx + 0.5f - tgx, ddy = ty + dy + 0.5f - tgy;
                        score = ddx * ddx + ddy * ddy;
                    }
                    if (score < best) { best = score; bdx = dx; bdy = dy; }
                }
                if (!any) { bdx = -g.A.Dx; bdy = -g.A.Dy; }
                wantDx = bdx; wantDy = bdy;
            }
            var actor = g.A;
            MoveActor(ref actor, wantDx, wantDy, door: g.Mode == GMode.Eyes);
            g.A = actor;

            if (g.Mode == GMode.Eyes && Mathf.Abs(g.A.X - 14f) < 0.4f && Mathf.Abs(g.A.Y - 11.5f) < 0.4f)
            {
                g.A.X = 14f; g.A.Y = 14.5f;
                g.Mode = GMode.Leaving;
            }
        }

        private void Target(int i, out float x, out float y)
        {
            ref var g = ref _ghosts[i];
            if (g.Mode == GMode.Eyes) { x = 14f; y = 11.5f; return; }
            if (g.Mode != GMode.Fright && Scatter()) { x = g.ScatterX + 0.5f; y = g.ScatterY + 0.5f; return; }
            switch (i)
            {
                case 0: x = _pac.X; y = _pac.Y; return;
                case 1: x = _pac.X + _pac.Dx * 4f; y = _pac.Y + _pac.Dy * 4f; return;
                case 2:
                    float ax = _pac.X + _pac.Dx * 2f, ay = _pac.Y + _pac.Dy * 2f;
                    x = ax + (ax - _ghosts[0].A.X); y = ay + (ay - _ghosts[0].A.Y); return;
                default:
                    float dx = _pac.X - g.A.X, dy = _pac.Y - g.A.Y;
                    if (dx * dx + dy * dy > 64f) { x = _pac.X; y = _pac.Y; }
                    else { x = g.ScatterX + 0.5f; y = g.ScatterY + 0.5f; }
                    return;
            }
        }

        private void Collisions()
        {
            for (int i = 0; i < 4; i++)
            {
                ref var g = ref _ghosts[i];
                if (g.Mode == GMode.InHouse || g.Mode == GMode.Leaving || g.Mode == GMode.Eyes) continue;
                float dx = g.A.X - _pac.X, dy = g.A.Y - _pac.Y;
                if (dx * dx + dy * dy > 0.55f) continue;
                if (g.Mode == GMode.Fright)
                {
                    int pts = 200 << Mathf.Min(_frightCombo, 3);
                    _frightCombo++;
                    Score += pts;
                    if (Score > HighScore) HighScore = Score;
                    g.Mode = GMode.Eyes;
                    _freezeT = 0.5f;
                }
                else
                {
                    Lives--;
                    _phase = Phase.Dying;
                    _phaseT = 1.9f;
                    return;
                }
            }
        }

        // ── rendering ──────────────────────────────────────────────────────────────────────────────────────────
        private static readonly Color32 ColBlack = new Color32(2, 2, 6, 255);
        private static readonly Color32 ColWall = new Color32(33, 33, 222, 255);
        private static readonly Color32 ColDot = new Color32(255, 183, 174, 255);
        private static readonly Color32 ColPac = new Color32(255, 238, 0, 255);
        private static readonly Color32 ColText = new Color32(222, 222, 222, 255);
        private static readonly Color32 ColReady = new Color32(255, 238, 0, 255);
        private static readonly Color32 ColOver = new Color32(255, 60, 60, 255);
        private static readonly Color32 ColDoor = new Color32(255, 183, 222, 255);
        private static readonly Color32 ColFrightBody = new Color32(33, 33, 255, 255);
        private static readonly Color32[] GhostCols =
        {
            new Color32(255, 30, 30, 255), new Color32(255, 184, 222, 255),
            new Color32(0, 255, 222, 255), new Color32(255, 184, 71, 255),
        };

        public void Render(Color32[] buf)
        {
            for (int i = 0; i < buf.Length; i++) buf[i] = ColBlack;

            bool flashWhite = _phase == Phase.LevelFlash && ((int)(_phaseT * 8f) & 1) == 0;
            var wall = flashWhite ? new Color32(222, 222, 255, 255) : ColWall;

            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    char c = _maze[x, y];
                    int px = x * Tile, py = MazeTop + y * Tile;
                    if (c == '#')
                    {
                        // outline only where a path neighbours the wall — reads like the classic line maze
                        if (!IsWall(x, y - 1)) Rect(buf, px, py, Tile, 2, wall);
                        if (!IsWall(x, y + 1)) Rect(buf, px, py + Tile - 2, Tile, 2, wall);
                        if (!IsWall(x - 1, y)) Rect(buf, px, py, 2, Tile, wall);
                        if (!IsWall(x + 1, y)) Rect(buf, px + Tile - 2, py, 2, Tile, wall);
                    }
                    else if (c == '-') Rect(buf, px, py + 3, Tile, 2, ColDoor);
                    else if (c == '.') Rect(buf, px + 3, py + 3, 2, 2, ColDot);
                    else if (c == 'o' && ((int)(_flashT * 5f) & 1) == 0) Disc(buf, px + 4, py + 4, 3.4f, ColDot);
                }

            if (_fruitUp) DrawFruit(buf, 14f * Tile, MazeTop + 17.5f * Tile);

            if (_phase != Phase.Dying || _phaseT > 1.4f) DrawPac(buf, _phase == Phase.Dying);
            else DrawPacDeath(buf, 1f - _phaseT / 1.4f);

            if (_phase == Phase.Play || _phase == Phase.Ready)
                for (int i = 0; i < 4; i++) DrawGhost(buf, i);

            DrawText(buf, 8, 8, "SCORE", ColText);
            DrawText(buf, 56, 8, Score.ToString("000000"), ColText);
            DrawText(buf, 128, 8, "HI", ColText);
            DrawText(buf, 152, 8, HighScore.ToString("000000"), ColText);

            for (int l = 0; l < Mathf.Min(Lives - 1, 4); l++)
                DiscMouth(buf, 12 + l * 14, H - 8, 5f, 1, 0, 0.5f, ColPac);
            DrawText(buf, W - 40, H - 11, "LV" + Level.ToString("00"), ColText);

            if (_phase == Phase.Ready) DrawText(buf, 88, MazeTop + 17 * Tile + 2, "READY!", ColReady);
            else if (_phase == Phase.GameOver) DrawText(buf, 76, MazeTop + 17 * Tile + 2, "GAME  OVER", ColOver);
        }

        private bool IsWall(int x, int y)
        {
            if (x < 0 || x >= Cols || y < 0 || y >= Rows) return true;
            return _maze[x, y] == '#';
        }

        private void DrawPac(Color32[] buf, bool frozen)
        {
            float px = _pac.X * Tile, py = MazeTop + _pac.Y * Tile;
            float open = frozen ? 0.15f : 0.12f + 0.38f * Mathf.Abs(Mathf.Sin(_mouth));
            DiscMouth(buf, px, py, 6.2f, _pac.Dx, _pac.Dy, open, ColPac);
        }

        private void DrawPacDeath(Color32[] buf, float u)
        {
            float px = _pac.X * Tile, py = MazeTop + _pac.Y * Tile;
            float open = Mathf.Lerp(0.2f, 1f, u);
            DiscMouth(buf, px, py, 6.2f * (1f - u * 0.55f), 0, -1, open, ColPac);
        }

        private void DrawGhost(Color32[] buf, int i)
        {
            ref var g = ref _ghosts[i];
            float px = g.A.X * Tile, py = MazeTop + g.A.Y * Tile;
            bool eyes = g.Mode == GMode.Eyes;
            bool fright = g.Mode == GMode.Fright;
            bool flash = fright && _frightT < 2f && ((int)(_frightT * 5f) & 1) == 0;

            if (!eyes)
            {
                var body = fright ? (flash ? new Color32(222, 222, 255, 255) : ColFrightBody) : GhostCols[i];
                Disc(buf, px, py - 1.5f, 6.2f, body, topHalfOnly: true);
                Rect(buf, (int)(px - 6f), (int)(py - 1.5f), 13, 6, body);
                for (int s = 0; s < 4; s++)
                {
                    int fx = (int)(px - 6f) + s * 4 + (((int)(_flashT * 8f) + i) & 1);
                    Rect(buf, fx, (int)(py + 4.5f), 2, 2, body);
                }
            }
            if (fright && !eyes)
            {
                var face = flash ? new Color32(255, 60, 60, 255) : new Color32(255, 222, 200, 255);
                Rect(buf, (int)px - 4, (int)py - 3, 2, 2, face);
                Rect(buf, (int)px + 2, (int)py - 3, 2, 2, face);
                for (int s = 0; s < 3; s++) Rect(buf, (int)px - 4 + s * 3, (int)py + 1, 2, 1, face);
            }
            else
            {
                int ex = g.A.Dx, ey = g.A.Dy;
                Rect(buf, (int)px - 4 + ex, (int)py - 3 + ey, 3, 4, new Color32(240, 240, 240, 255));
                Rect(buf, (int)px + 1 + ex, (int)py - 3 + ey, 3, 4, new Color32(240, 240, 240, 255));
                Rect(buf, (int)px - 3 + ex * 2, (int)py - 2 + ey * 2, 2, 2, new Color32(30, 30, 200, 255));
                Rect(buf, (int)px + 2 + ex * 2, (int)py - 2 + ey * 2, 2, 2, new Color32(30, 30, 200, 255));
            }
        }

        private void DrawFruit(Color32[] buf, float px, float py)
        {
            var red = new Color32(255, 30, 30, 255);
            var stem = new Color32(120, 200, 60, 255);
            Disc(buf, px - 2f, py + 1.5f, 3f, red);
            Disc(buf, px + 2.5f, py + 2.5f, 3f, red);
            Rect(buf, (int)px - 1, (int)py - 5, 2, 3, stem);
            Rect(buf, (int)px + 1, (int)py - 4, 2, 2, stem);
        }

        // ── low-level pixels (origin top-left; buffer row 0 is texture bottom, so flip on write) ────────────────
        private static void Put(Color32[] buf, int x, int y, Color32 c)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            buf[(H - 1 - y) * W + x] = c;
        }

        private static void Rect(Color32[] buf, int x, int y, int w, int h, Color32 c)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    Put(buf, xx, yy, c);
        }

        private static void Disc(Color32[] buf, float cx, float cy, float r, Color32 c, bool topHalfOnly = false)
        {
            for (int yy = (int)(cy - r); yy <= (int)(cy + r); yy++)
            {
                if (topHalfOnly && yy > cy) continue;
                for (int xx = (int)(cx - r); xx <= (int)(cx + r); xx++)
                {
                    float dx = xx - cx, dy = yy - cy;
                    if (dx * dx + dy * dy <= r * r) Put(buf, xx, yy, c);
                }
            }
        }

        private static void DiscMouth(Color32[] buf, float cx, float cy, float r, int dx, int dy, float open, Color32 c)
        {
            float dirA = Mathf.Atan2(-dy, dx == 0 && dy == 0 ? 1 : dx);
            float half = open * Mathf.PI * 0.5f;
            for (int yy = (int)(cy - r); yy <= (int)(cy + r); yy++)
                for (int xx = (int)(cx - r); xx <= (int)(cx + r); xx++)
                {
                    float ddx = xx - cx, ddy = yy - cy;
                    if (ddx * ddx + ddy * ddy > r * r) continue;
                    float a = Mathf.Atan2(-ddy, ddx);
                    float diff = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, dirA * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    if (diff < half) continue;
                    Put(buf, xx, yy, c);
                }
        }

        // 3x5 pixel font, row-packed strings
        private static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            { '0', new[] { "111", "101", "101", "101", "111" } },
            { '1', new[] { "010", "110", "010", "010", "111" } },
            { '2', new[] { "111", "001", "111", "100", "111" } },
            { '3', new[] { "111", "001", "011", "001", "111" } },
            { '4', new[] { "101", "101", "111", "001", "001" } },
            { '5', new[] { "111", "100", "111", "001", "111" } },
            { '6', new[] { "111", "100", "111", "101", "111" } },
            { '7', new[] { "111", "001", "010", "010", "010" } },
            { '8', new[] { "111", "101", "111", "101", "111" } },
            { '9', new[] { "111", "101", "111", "001", "111" } },
            { 'S', new[] { "011", "100", "010", "001", "110" } },
            { 'C', new[] { "011", "100", "100", "100", "011" } },
            { 'O', new[] { "010", "101", "101", "101", "010" } },
            { 'R', new[] { "110", "101", "110", "101", "101" } },
            { 'E', new[] { "111", "100", "110", "100", "111" } },
            { 'H', new[] { "101", "101", "111", "101", "101" } },
            { 'I', new[] { "111", "010", "010", "010", "111" } },
            { 'A', new[] { "010", "101", "111", "101", "101" } },
            { 'D', new[] { "110", "101", "101", "101", "110" } },
            { 'Y', new[] { "101", "101", "010", "010", "010" } },
            { 'G', new[] { "011", "100", "101", "101", "011" } },
            { 'M', new[] { "101", "111", "111", "101", "101" } },
            { 'V', new[] { "101", "101", "101", "101", "010" } },
            { 'L', new[] { "100", "100", "100", "100", "111" } },
            { '!', new[] { "010", "010", "010", "000", "010" } },
            { ' ', new[] { "000", "000", "000", "000", "000" } },
        };

        private static void DrawText(Color32[] buf, int x, int y, string text, Color32 c)
        {
            foreach (char ch in text)
            {
                if (Font.TryGetValue(char.ToUpperInvariant(ch), out var rows))
                    for (int r = 0; r < 5; r++)
                        for (int cc = 0; cc < 3; cc++)
                            if (rows[r][cc] == '1')
                            {
                                Put(buf, x + cc * 2, y + r * 2, c);
                                Put(buf, x + cc * 2 + 1, y + r * 2, c);
                                Put(buf, x + cc * 2, y + r * 2 + 1, c);
                                Put(buf, x + cc * 2 + 1, y + r * 2 + 1, c);
                            }
                x += 8;
            }
        }
    }
}
