using System;
using System.Collections.Generic;

namespace FiresMiniGames.Modules.Zelda
{
    /// <summary>
    /// "The Legend of Fire" — an original top-down adventure in the spirit of the 1986 classic, written from
    /// scratch (no emulator, no Nintendo ROM or assets). It renders to a 256x240 RGBA buffer and takes an NES-style
    /// controller byte, so a headless harness can photograph it and the in-game host can blit it to a TV screen. The
    /// StrategyWiki/Wikibooks guide (CC-BY-SA) is the design bible for the world, items, enemies and bosses; this is
    /// slice 1 — overworld exploration, sword combat, hearts/rupees, a few enemies and the sword cave.
    /// </summary>
    internal sealed class ZeldaGame
    {
        public const int Width = 256, Height = 240;
        public const int HudH = 64;                 // top status bar; play field is the 176px below
        public const int Cols = 16, Rows = 11, Tile = 16;

        private readonly byte[] _rgba = new byte[Width * Height * 4];
        public byte[] Rgba => _rgba;

        public enum Sfx { Sword, Beam, Hurt, EnemyHit, EnemyDie, Pickup, Rupee, Heart, Secret, Text, Start, Bomb, Boomerang, Arrow, Flame }
        public readonly Queue<Sfx> Sounds = new Queue<Sfx>();

        private enum State { Title, Play, Cave, Sub }
        private State _state = State.Title;

        internal enum Item { None, Bomb, Boomerang, Bow, Candle }

        private uint _rng = 0x1234abcdu;
        private int Rand(int n) { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (int)(_rng % (uint)n); }

        private long _tick;
        private byte _pad, _prev;
        private bool Down(int b) => (_pad & (1 << b)) != 0;
        private bool Pressed(int b) => (_pad & (1 << b)) != 0 && (_prev & (1 << b)) == 0;
        // pad bits: 0 A(sword) 1 B(item) 2 Select 3 Start 4 Up 5 Down 6 Left 7 Right
        private const int A = 0, B = 1, SEL = 2, ST = 3, UP = 4, DN = 5, LT = 6, RT = 7;

        private enum Dir { Down, Up, Left, Right }

        // ── hero ───────────────────────────────────────────────────────────────────────────────────────────────
        private float _hx = 112, _hy = 64 + 9 * 16;
        private Dir _hdir = Dir.Up;
        private int _hp = 6, _maxHp = 6;           // half-hearts
        private int _rupees, _keys, _bombs = 0;
        private bool _hasSword, _hasBow;
        private readonly HashSet<Item> _items = new HashSet<Item>();
        private Item _bItem = Item.None;
        private int _subCursor;
        private int _walkAnim, _invuln, _attack, _knock; private Dir _knockDir;

        // dungeon
        private bool _inDungeon; private int _dungeonLevel, _dc, _dr;
        private Dictionary<int, DungeonRoom> _dungeon;
        private readonly HashSet<int> _roomsCleared = new HashSet<int>();
        private readonly HashSet<int> _roomsVisited = new HashSet<int>();
        private bool _bossAlive; private float _bossX, _bossY; private int _bossHp, _bossCool, _bossDir;
        private bool _hasTriforce1; private int _winFlash;
        private DungeonRoom Room => _dungeon != null && _dungeon.TryGetValue(_dc << 8 | _dr, out var r) ? r : null;

        // ── world ──────────────────────────────────────────────────────────────────────────────────────────────
        private int _wc = 3, _wr = 5;               // current overworld screen (col,row) — start is (3,5)
        private readonly Dictionary<int, string[]> _screens = ZeldaWorld.Build();
        private string[] Screen => _screens.TryGetValue(_wc << 8 | _wr, out var s) ? s : ZeldaWorld.Empty;
        private int _scrollT; private int _scrollDX, _scrollDY; private string[] _scrollFrom;

        private sealed class Mob { public float X, Y; public Dir D; public int Hp, Cool, Move, Kind, Anim; public bool Dead; }
        private readonly List<Mob> _mobs = new List<Mob>();
        private sealed class Shot { public float X, Y, Vx, Vy; public int Kind; public bool Hero; public bool Dead; public float Dist; public bool Ret; public int Life; } // 0 pebble,1 beam,2 boomerang,3 arrow,4 flame
        private readonly List<Shot> _shots = new List<Shot>();
        private sealed class LiveBomb { public float X, Y; public int Fuse; }
        private readonly List<LiveBomb> _liveBombs = new List<LiveBomb>();
        private sealed class Blast { public float X, Y; public int Life; }
        private readonly List<Blast> _blasts = new List<Blast>();
        private sealed class Drop { public float X, Y; public int Kind, Life; } // 0 rupee,1 heart,2 bomb
        private readonly List<Drop> _drops = new List<Drop>();
        private readonly HashSet<int> _clearedCaves = new HashSet<int>();

        public ZeldaGame() { Render(); }

        // Advance one 60Hz frame with the given controller state, then repaint.
        public void Step(byte pad)
        {
            _prev = _pad; _pad = pad; _tick++;
            switch (_state)
            {
                case State.Title: StepTitle(); break;
                case State.Play: StepPlay(); break;
                case State.Cave: StepCave(); break;
                case State.Sub: StepSub(); break;
            }
            Render();
        }

        private void StepTitle()
        {
            if (Pressed(ST) || Pressed(A)) { _state = State.Play; Sounds.Enqueue(Sfx.Start); LoadScreen(); }
        }

        // ── inventory subscreen (Start) ──────────────────────────────────────────────────────────────────────────
        private void StepSub()
        {
            if (Pressed(ST)) { _state = State.Play; Sounds.Enqueue(Sfx.Text); return; }
            var list = OwnedBItems();
            if (list.Count > 0)
            {
                if (Pressed(LT)) { _subCursor = (_subCursor + list.Count - 1) % list.Count; Sounds.Enqueue(Sfx.Text); }
                if (Pressed(RT)) { _subCursor = (_subCursor + 1) % list.Count; Sounds.Enqueue(Sfx.Text); }
                _subCursor = Math.Min(_subCursor, list.Count - 1);
                _bItem = list[_subCursor];
            }
        }

        private List<Item> OwnedBItems()
        {
            var l = new List<Item>();
            if (_items.Contains(Item.Bomb)) l.Add(Item.Bomb);
            if (_items.Contains(Item.Boomerang)) l.Add(Item.Boomerang);
            if (_items.Contains(Item.Bow)) l.Add(Item.Bow);
            if (_items.Contains(Item.Candle)) l.Add(Item.Candle);
            return l;
        }

        private ZeldaArt.Sprite ItemIcon(Item it)
        {
            switch (it)
            {
                case Item.Bomb: return ZeldaArt.Bomb;
                case Item.Boomerang: return ZeldaArt.BoomerangSpr;
                case Item.Bow: return ZeldaArt.ItemSword;   // TODO bow sprite
                case Item.Candle: return ZeldaArt.Flame;
                default: return null;
            }
        }

        // ── overworld play ───────────────────────────────────────────────────────────────────────────────────────
        private void StepPlay()
        {
            if (_scrollT > 0) { _scrollT--; if (_scrollT == 0) FinishScroll(); return; }
            if (Pressed(ST)) { _state = State.Sub; _subCursor = 0; Sounds.Enqueue(Sfx.Text); return; }
            if (_invuln > 0) _invuln--;

            if (_attack > 0)
            {
                _attack--;
                if (_attack == 8 && _hp == _maxHp && _hasSword) FireBeam();  // full-health sword beam
            }
            else if (_knock > 0) { _knock--; StepKnock(); }
            else MoveHero();

            if (_hasSword && _attack == 0 && Pressed(A)) { _attack = 12; Sounds.Enqueue(Sfx.Sword); }
            if (_attack == 0 && _knock == 0 && Pressed(B)) UseItem();

            StepMobs();
            if (_inDungeon) StepBoss();
            StepShots();
            StepBombs();
            StepBlasts();
            StepDrops();
            SwordHits();
            if (_inDungeon) { CheckDoors(); CheckBowPickup(); }
            else { CheckExit(); CheckCaveEntry(); }
            if (_winFlash > 0) _winFlash--;
            if (_hp <= 0) { _inDungeon = false; _dungeon = null; _hp = _maxHp; _wc = 3; _wr = 5; _hx = 112; _hy = 64 + 9 * 16; LoadScreen(); Sounds.Enqueue(Sfx.Hurt); }
        }

        private void MoveHero()
        {
            float sp = 1.4f;
            float nx = _hx, ny = _hy; bool moving = false;
            if (Down(UP)) { _hdir = Dir.Up; ny -= sp; moving = true; }
            else if (Down(DN)) { _hdir = Dir.Down; ny += sp; moving = true; }
            else if (Down(LT)) { _hdir = Dir.Left; nx -= sp; moving = true; }
            else if (Down(RT)) { _hdir = Dir.Right; nx += sp; moving = true; }

            if (moving)
            {
                if (CanWalk(nx, _hy)) _hx = nx;
                if (CanWalk(_hx, ny)) _hy = ny;
                _walkAnim = (_walkAnim + 1) % 16;
            }
            _hx = Clamp(_hx, 0, Width - 16);
            _hy = Clamp(_hy, HudH, Height - 16);
        }

        private void StepKnock()
        {
            float k = 3f; float nx = _hx, ny = _hy;
            switch (_knockDir) { case Dir.Up: ny -= k; break; case Dir.Down: ny += k; break; case Dir.Left: nx -= k; break; default: nx += k; break; }
            if (CanWalk(nx, _hy)) _hx = nx;
            if (CanWalk(_hx, ny)) _hy = ny;
            _hx = Clamp(_hx, 0, Width - 16); _hy = Clamp(_hy, HudH, Height - 16);
        }

        // Hero collision box = feet region (so you can overlap tree tops a little, Zelda-style).
        private bool CanWalk(float px, float py)
        {
            int y0 = (int)py + 8, y1 = (int)py + 15;
            int x0 = (int)px + 2, x1 = (int)px + 13;
            return Walk(x0, y0) && Walk(x1, y0) && Walk(x0, y1) && Walk(x1, y1);
        }

        private bool Walk(int px, int py)
        {
            if (_inDungeon) return DungeonWalk(px, py);
            if (py < HudH) return true;
            int col = px / Tile, row = (py - HudH) / Tile;
            if (col < 0 || col >= Cols || row < 0 || row >= Rows) return true; // edges handled by CheckExit
            char c = TileAt(Screen, col, row);
            return ZeldaWorld.Walkable(c);
        }

        private bool DungeonWalk(int px, int py)
        {
            if (py < HudH) return true;
            int col = px / Tile, row = (py - HudH) / Tile;
            if (col < 0 || col >= Cols || row < 0 || row >= Rows) return true;
            var rm = Room; if (rm == null) return false;
            bool topW = row <= 1, botW = row >= 9, leftW = col <= 1, rightW = col >= 14;
            if (!topW && !botW && !leftW && !rightW)
            {
                char c = TileAt(rm.Tiles, col, row);
                return c != 'B' && c != 'W';
            }
            if (topW && col >= 7 && col <= 8) return DoorOpen(rm.N);
            if (botW && col >= 7 && col <= 8) return rm.Entrance || DoorOpen(rm.S);
            if (leftW && row >= 5 && row <= 6) return DoorOpen(rm.W);
            if (rightW && row >= 5 && row <= 6) return DoorOpen(rm.E);
            return false;
        }

        private bool DoorOpen(byte type) => type == ZeldaDungeon.Open || (type == ZeldaDungeon.Shutter && RoomCleared());
        private bool RoomCleared() => _roomsCleared.Contains(_dc << 8 | _dr);

        private void EnterDungeon(int level)
        {
            _inDungeon = true; _dungeonLevel = level;
            _dungeon = ZeldaDungeon.Level1();
            _dc = 2; _dr = 3;
            _hx = 116; _hy = HudH + 8 * 16; _hdir = Dir.Up;
            LoadRoom();
            Sounds.Enqueue(Sfx.Secret);
        }

        private void ExitDungeon()
        {
            _inDungeon = false; _dungeon = null;
            _hx = 116; _hy = HudH + 4 * 16; _hdir = Dir.Down;
            LoadScreen();
        }

        private void LoadRoom()
        {
            _mobs.Clear(); _shots.Clear(); _drops.Clear(); _liveBombs.Clear(); _blasts.Clear();
            var rm = Room; if (rm == null) return;
            int key = _dc << 8 | _dr;
            _roomsVisited.Add(key);
            _bossAlive = false;
            if (rm.Boss && !_roomsCleared.Contains(key)) SpawnBoss();
            else SpawnDungeonMobs(rm);
            if (_mobs.Count == 0 && !_bossAlive) _roomsCleared.Add(key);
        }

        private void SpawnBoss() { _bossAlive = true; _bossHp = 6; _bossCool = 70; _bossDir = 0; _bossX = Width - 64; _bossY = HudH + 80; }

        private void SpawnDungeonMobs(DungeonRoom rm)
        {
            if (_roomsCleared.Contains(_dc << 8 | _dr)) return;
            for (int row = 0; row < rm.Tiles.Length; row++)
                for (int col = 0; col < rm.Tiles[row].Length; col++)
                {
                    char c = rm.Tiles[row][col];
                    int kind = c == 'k' ? 2 : c == 'l' ? 3 : c == 'z' ? 4 : -1;
                    if (kind >= 0) _mobs.Add(new Mob { X = col * 16, Y = HudH + row * 16, D = (Dir)Rand(4), Hp = kind == 4 ? 2 : 1, Kind = kind, Cool = 20 + Rand(50) });
                }
        }

        private void CheckDoors()
        {
            var rm = Room; if (rm == null) return;
            if (rm.Entrance && _hy >= Height - 16 && InCols(7, 8)) { ExitDungeon(); return; }
            if (_hy <= HudH && DoorOpen(rm.N) && InCols(7, 8)) { GoRoom(0, -1, Dir.Up); return; }
            if (_hy >= Height - 16 && DoorOpen(rm.S) && InCols(7, 8)) { GoRoom(0, 1, Dir.Down); return; }
            if (_hx >= Width - 16 && DoorOpen(rm.E) && InRows(5, 6)) { GoRoom(1, 0, Dir.Right); return; }
            if (_hx <= 0 && DoorOpen(rm.W) && InRows(5, 6)) { GoRoom(-1, 0, Dir.Left); return; }
        }

        private bool InCols(int a, int b) { int c = ((int)_hx + 8) / Tile; return c >= a && c <= b; }
        private bool InRows(int a, int b) { int r = ((int)_hy + 8 - HudH) / Tile; return r >= a && r <= b; }

        private void GoRoom(int dcol, int drow, Dir entering)
        {
            int nk = (_dc + dcol) << 8 | (_dr + drow);
            if (_dungeon == null || !_dungeon.ContainsKey(nk)) return;
            _dc += dcol; _dr += drow;
            switch (entering)
            {
                case Dir.Up: _hx = 116; _hy = Height - 22; break;
                case Dir.Down: _hx = 116; _hy = HudH + 6; break;
                case Dir.Right: _hx = 6; _hy = HudH + 80; break;
                default: _hx = Width - 22; _hy = HudH + 80; break;
            }
            LoadRoom();
        }

        private void StepBoss()
        {
            if (!_bossAlive) return;
            _bossY += _bossDir == 0 ? -0.6f : 0.6f;
            if (_bossY < HudH + 36) _bossDir = 1; else if (_bossY > Height - 74) _bossDir = 0;
            if (--_bossCool <= 0)
            {
                _bossCool = 100;
                for (int i = -1; i <= 1; i++) _shots.Add(new Shot { X = _bossX, Y = _bossY + 8 + i * 6, Vx = -2.2f, Vy = i * 0.7f, Kind = 0, Hero = false });
                Sounds.Enqueue(Sfx.EnemyHit);
            }
            if (_invuln == 0 && _attack == 0 && Overlap(_hx, _hy, 16, 16, _bossX, _bossY, 24, 24)) HurtHero(2, DirAwayFrom(_bossX + 12, _bossY + 12));
        }

        private void HitBoss(int dmg)
        {
            _bossHp -= dmg; Sounds.Enqueue(Sfx.EnemyHit);
            if (_bossHp <= 0)
            {
                _bossAlive = false; _roomsCleared.Add(_dc << 8 | _dr);
                _maxHp = Math.Min(_maxHp + 2, 32); _hp = _maxHp; _hasTriforce1 = true; _winFlash = 150;
                Sounds.Enqueue(Sfx.EnemyDie); Sounds.Enqueue(Sfx.Secret);
            }
        }

        private void CheckBowPickup()
        {
            var rm = Room; if (rm == null || !rm.GivesBow || _hasBow) return;
            if (Overlap(_hx, _hy, 16, 16, Width / 2 - 4, HudH + 80, 12, 16))
            { _hasBow = true; _items.Add(Item.Bow); if (_bItem == Item.None) _bItem = Item.Bow; Sounds.Enqueue(Sfx.Secret); }
        }

        private static char TileAt(string[] s, int col, int row)
        {
            if (row < 0 || row >= s.Length) return '.';
            string line = s[row];
            return col < line.Length ? line[col] : 'R';
        }

        // ── screen transitions ───────────────────────────────────────────────────────────────────────────────────
        private void CheckExit()
        {
            int nc = _wc, nr = _wr, dx = 0, dy = 0;
            if (_hy <= HudH && Has(_wc, _wr - 1)) { nr--; dy = 1; }
            else if (_hy >= Height - 16 && Has(_wc, _wr + 1)) { nr++; dy = -1; }
            else if (_hx <= 0 && Has(_wc - 1, _wr)) { nc--; dx = 1; }
            else if (_hx >= Width - 16 && Has(_wc + 1, _wr)) { nc++; dx = -1; }
            if (nc == _wc && nr == _wr) return;
            _scrollFrom = Screen; _wc = nc; _wr = nr; _scrollDX = dx; _scrollDY = dy; _scrollT = 24;
            _mobs.Clear(); _shots.Clear(); _drops.Clear();
        }

        private void FinishScroll()
        {
            if (_scrollDX > 0) _hx = Width - 18; else if (_scrollDX < 0) _hx = 2;
            if (_scrollDY > 0) _hy = Height - 18; else if (_scrollDY < 0) _hy = HudH + 2;
            SpawnMobs();
        }

        private bool Has(int c, int r) => _screens.ContainsKey(c << 8 | r);

        private void LoadScreen() { _mobs.Clear(); _shots.Clear(); _drops.Clear(); SpawnMobs(); }

        // ── cave (get the sword) ─────────────────────────────────────────────────────────────────────────────────
        private void CheckCaveEntry()
        {
            int col = ((int)_hx + 8) / Tile, row = ((int)_hy + 8 - HudH) / Tile;
            if (row < 0 || row >= Rows) return;
            char t = TileAt(Screen, col, row);
            if (t == 'C' && _hdir == Dir.Up && Down(UP))
            { _state = State.Cave; Sounds.Enqueue(Sfx.Secret); _hy = Height - 40; _hx = 120; _hdir = Dir.Up; }
            else if (t == 'L' && _hdir == Dir.Up && Down(UP)) EnterDungeon(1);
        }

        private void StepCave()
        {
            // one dark room; the old man offers the sword, take it by walking into it.
            float sp = 1.4f;
            if (Down(LT)) { _hx -= sp; _hdir = Dir.Left; } else if (Down(RT)) { _hx += sp; _hdir = Dir.Right; }
            if (Down(UP)) { _hy -= sp; _hdir = Dir.Up; } else if (Down(DN)) { _hy += sp; _hdir = Dir.Down; }
            _hx = Clamp(_hx, 40, Width - 56);
            _hy = Clamp(_hy, HudH + 88, Height - 18);     // can walk up to the gift, or down to leave
            if (!CaveCleared && Overlap(_hx, _hy, 16, 16, Width / 2 - 4, HudH + 84, 8, 16)) GiveCaveItem();
            if (_hy >= Height - 22) { _state = State.Play; _hy = HudH + 9 * 16; _hx = 112; _hdir = Dir.Down; }  // walk out the bottom
        }

        private static readonly Dictionary<int, Item> CaveBItems = new Dictionary<int, Item>
        { { (5 << 8) | 4, Item.Boomerang }, { (6 << 8) | 1, Item.Candle } };
        private bool CaveIsSword => _wc == 3 && _wr == 5;
        private Item CaveBItem => CaveBItems.TryGetValue(_wc << 8 | _wr, out var it) ? it : Item.None;
        private bool CaveCleared => _clearedCaves.Contains(_wc << 8 | _wr);

        private void GiveCaveItem()
        {
            _clearedCaves.Add(_wc << 8 | _wr);
            if (CaveIsSword) _hasSword = true;
            else { var it = CaveBItem; if (it != Item.None) { _items.Add(it); if (it == Item.Bomb) _bombs += 4; if (_bItem == Item.None) _bItem = it; } }
            Sounds.Enqueue(Sfx.Secret);
        }

        // ── enemies ──────────────────────────────────────────────────────────────────────────────────────────────
        private void SpawnMobs()
        {
            var s = Screen;
            // spawn markers 'o' octorok, 'k' tektite in the map data
            for (int row = 0; row < s.Length; row++)
                for (int col = 0; col < s[row].Length; col++)
                {
                    char c = s[row][col];
                    if (c == 'o') _mobs.Add(new Mob { X = col * 16, Y = HudH + row * 16, D = (Dir)Rand(4), Hp = 1, Kind = 0, Cool = 30 + Rand(60) });
                    else if (c == 'k') _mobs.Add(new Mob { X = col * 16, Y = HudH + row * 16, D = (Dir)Rand(4), Hp = 1, Kind = 1, Cool = Rand(40) });
                }
        }

        private void StepMobs()
        {
            foreach (var m in _mobs)
            {
                if (m.Dead) continue;
                m.Anim++;
                switch (m.Kind)
                {
                    case 0: StepOctorok(m); break;
                    case 1: StepTektite(m); break;
                    case 2: StepKeese(m); break;
                    default: StepWander(m, m.Kind == 3 ? 0.5f : 0.9f); break;   // gel / stalfos
                }
                if (_invuln == 0 && _attack == 0 && Overlap(_hx, _hy, 16, 16, m.X, m.Y, 16, 16)) HurtHero(2, DirAwayFrom(m.X, m.Y));
            }
            _mobs.RemoveAll(m => m.Dead);
            if (_inDungeon && _mobs.Count == 0 && !_bossAlive && !RoomCleared())
            { _roomsCleared.Add(_dc << 8 | _dr); Sounds.Enqueue(Sfx.Secret); }
        }

        private void StepOctorok(Mob m)
        {
            m.Move--;
            if (m.Move <= 0) { m.D = (Dir)Rand(4); m.Move = 24 + Rand(48); }
            float sp = 0.8f; float nx = m.X, ny = m.Y;
            switch (m.D) { case Dir.Up: ny -= sp; break; case Dir.Down: ny += sp; break; case Dir.Left: nx -= sp; break; default: nx += sp; break; }
            if (MobWalk(nx, m.Y)) m.X = nx; else m.Move = 0;
            if (MobWalk(m.X, ny)) m.Y = ny; else m.Move = 0;
            m.X = Clamp(m.X, 0, Width - 16); m.Y = Clamp(m.Y, HudH, Height - 16);
            m.Cool--;
            if (m.Cool <= 0)
            {
                m.Cool = 70 + Rand(70);
                float vx = 0, vy = 0; switch (m.D) { case Dir.Up: vy = -2; break; case Dir.Down: vy = 2; break; case Dir.Left: vx = -2; break; default: vx = 2; break; }
                _shots.Add(new Shot { X = m.X + 4, Y = m.Y + 4, Vx = vx, Vy = vy, Kind = 0, Hero = false });
            }
        }

        private void StepTektite(Mob m)
        {
            // hops: Cool counts down to a leap; during a leap it arcs to a random nearby spot.
            m.Cool--;
            if (m.Cool <= 0) { m.Cool = 40 + Rand(40); m.Move = 24; m.D = (Dir)Rand(4); }
            if (m.Move > 0)
            {
                m.Move--; float sp = 1.6f; float nx = m.X, ny = m.Y;
                switch (m.D) { case Dir.Up: ny -= sp; break; case Dir.Down: ny += sp; break; case Dir.Left: nx -= sp; break; default: nx += sp; break; }
                if (nx > 0 && nx < Width - 16) m.X = nx; if (ny > HudH && ny < Height - 16) m.Y = ny;
            }
        }

        private bool MobWalk(float px, float py)
        {
            if (_inDungeon) return DungeonWalk((int)px + 8, (int)py + 12);
            int col = ((int)px + 8) / Tile, row = ((int)py + 12 - HudH) / Tile;
            if (col < 0 || col >= Cols || row < 0 || row >= Rows) return false;
            return ZeldaWorld.Walkable(TileAt(Screen, col, row));
        }

        private void StepKeese(Mob m)
        {
            if (--m.Move <= 0) { m.D = (Dir)Rand(4); m.Move = 10 + Rand(20); }
            float sp = 1.8f;
            switch (m.D) { case Dir.Up: m.Y -= sp; break; case Dir.Down: m.Y += sp; break; case Dir.Left: m.X -= sp; break; default: m.X += sp; break; }
            m.X = Clamp(m.X, 24, Width - 40); m.Y = Clamp(m.Y, HudH + 24, Height - 40);   // flies over walls
        }

        private void StepWander(Mob m, float sp)
        {
            if (--m.Move <= 0) { m.D = (Dir)Rand(4); m.Move = 24 + Rand(40); }
            float nx = m.X, ny = m.Y;
            switch (m.D) { case Dir.Up: ny -= sp; break; case Dir.Down: ny += sp; break; case Dir.Left: nx -= sp; break; default: nx += sp; break; }
            if (MobWalk(nx, m.Y)) m.X = nx; else m.Move = 0;
            if (MobWalk(m.X, ny)) m.Y = ny; else m.Move = 0;
        }

        // ── combat ───────────────────────────────────────────────────────────────────────────────────────────────
        private void SwordHits()
        {
            if (_attack < 4 || _attack > 11) return;   // active swing window
            float sx = _hx, sy = _hy, sw = 16, sh = 16;
            switch (_hdir) { case Dir.Up: sy -= 14; sh = 16; break; case Dir.Down: sy += 14; break; case Dir.Left: sx -= 14; break; case Dir.Right: sx += 14; break; }
            foreach (var m in _mobs)
                if (!m.Dead && Overlap(sx, sy, sw, sh, m.X, m.Y, 16, 16)) DamageMob(m);
            if (_attack == 8 && _bossAlive && Overlap(sx, sy, sw, sh, _bossX, _bossY, 24, 24)) HitBoss(1);
        }

        private void FireBeam()
        {
            float vx = 0, vy = 0; switch (_hdir) { case Dir.Up: vy = -3.4f; break; case Dir.Down: vy = 3.4f; break; case Dir.Left: vx = -3.4f; break; default: vx = 3.4f; break; }
            _shots.Add(new Shot { X = _hx + 4, Y = _hy + 4, Vx = vx, Vy = vy, Kind = 1, Hero = true });
            Sounds.Enqueue(Sfx.Beam);
        }

        // ── B-item use ───────────────────────────────────────────────────────────────────────────────────────────
        private void UseItem()
        {
            switch (_bItem)
            {
                case Item.Bomb: if (_bombs > 0 && _liveBombs.Count < 2) { PlaceBomb(); _bombs--; } break;
                case Item.Boomerang: if (!BoomerangActive()) ThrowBoomerang(); break;
                case Item.Bow: if (_hasBow && _rupees > 0) { FireArrow(); _rupees--; } break;
                case Item.Candle: FireFlame(); break;
            }
        }

        private void DirVec(out float vx, out float vy, float sp)
        {
            vx = 0; vy = 0;
            switch (_hdir) { case Dir.Up: vy = -sp; break; case Dir.Down: vy = sp; break; case Dir.Left: vx = -sp; break; default: vx = sp; break; }
        }

        private void PlaceBomb()
        {
            float bx = _hx, by = _hy;
            switch (_hdir) { case Dir.Up: by -= 16; break; case Dir.Down: by += 16; break; case Dir.Left: bx -= 16; break; case Dir.Right: bx += 16; break; }
            _liveBombs.Add(new LiveBomb { X = bx, Y = by, Fuse = 90 });
            Sounds.Enqueue(Sfx.Pickup);
        }

        private void StepBombs()
        {
            foreach (var b in _liveBombs) { b.Fuse--; if (b.Fuse <= 0) Explode(b.X + 8, b.Y + 8); }
            _liveBombs.RemoveAll(b => b.Fuse <= 0);
        }

        private void Explode(float cx, float cy)
        {
            Sounds.Enqueue(Sfx.Bomb);
            _blasts.Add(new Blast { X = cx, Y = cy, Life = 18 });
            foreach (var m in _mobs)
                if (!m.Dead && Dist2(m.X + 8, m.Y + 8, cx, cy) < 26 * 26)
                { m.Hp -= 2; if (m.Hp <= 0) { m.Dead = true; Sounds.Enqueue(Sfx.EnemyDie); MaybeDrop(m.X, m.Y); } }
        }

        private void StepBlasts()
        {
            foreach (var b in _blasts) b.Life--;
            _blasts.RemoveAll(b => b.Life <= 0);
        }

        private bool BoomerangActive() { foreach (var s in _shots) if (s.Kind == 2) return true; return false; }

        private void ThrowBoomerang()
        {
            DirVec(out float vx, out float vy, 3.2f);
            _shots.Add(new Shot { X = _hx + 4, Y = _hy + 4, Vx = vx, Vy = vy, Kind = 2, Hero = true });
            Sounds.Enqueue(Sfx.Boomerang);
        }

        private void FireArrow()
        {
            DirVec(out float vx, out float vy, 4f);
            _shots.Add(new Shot { X = _hx + 4, Y = _hy + 4, Vx = vx, Vy = vy, Kind = 3, Hero = true });
            Sounds.Enqueue(Sfx.Arrow);
        }

        private void FireFlame()
        {
            DirVec(out float vx, out float vy, 1.6f);
            _shots.Add(new Shot { X = _hx + 4, Y = _hy + 4, Vx = vx, Vy = vy, Kind = 4, Hero = true, Life = 40 });
            Sounds.Enqueue(Sfx.Flame);
        }

        private static float Dist2(float ax, float ay, float bx, float by) { float dx = ax - bx, dy = ay - by; return dx * dx + dy * dy; }

        private void DamageMob(Mob m)
        {
            m.Hp--; Sounds.Enqueue(Sfx.EnemyHit);
            if (m.Hp <= 0) { m.Dead = true; Sounds.Enqueue(Sfx.EnemyDie); MaybeDrop(m.X, m.Y); }
        }

        private void MaybeDrop(float x, float y)
        {
            int r = Rand(10);
            if (r < 4) _drops.Add(new Drop { X = x, Y = y, Kind = 0, Life = 400 });       // rupee
            else if (r < 6) _drops.Add(new Drop { X = x, Y = y, Kind = 1, Life = 400 });  // heart
            else if (r < 7) _drops.Add(new Drop { X = x, Y = y, Kind = 2, Life = 400 });  // bombs
        }

        private void StepShots()
        {
            foreach (var sh in _shots)
            {
                if (sh.Kind == 2) { StepBoomerang(sh); continue; }

                sh.X += sh.Vx; sh.Y += sh.Vy;
                if (sh.Kind == 4) { sh.Life--; if (sh.Life <= 0) { sh.Dead = true; continue; } BurnBush(sh.X + 3, sh.Y + 3); }
                if (sh.X < 0 || sh.X > Width || sh.Y < HudH || sh.Y > Height) { sh.Dead = true; continue; }

                if (sh.Hero)
                {
                    if ((sh.Kind == 1 || sh.Kind == 3) && !MobWalk(sh.X, sh.Y)) sh.Dead = true;
                    foreach (var m in _mobs)
                        if (!m.Dead && Overlap(sh.X, sh.Y, 8, 8, m.X, m.Y, 16, 16)) { DamageMob(m); if (sh.Kind != 4) sh.Dead = true; }
                    if (_bossAlive && Overlap(sh.X, sh.Y, 8, 8, _bossX, _bossY, 24, 24)) { HitBoss(sh.Kind == 3 ? 2 : 1); sh.Dead = true; }
                }
                else if (_invuln == 0 && _attack == 0 && Overlap(sh.X, sh.Y, 8, 8, _hx + 2, _hy + 2, 12, 12))
                { HurtHero(2, DirAwayFrom(sh.X, sh.Y)); sh.Dead = true; }
            }
            _shots.RemoveAll(s => s.Dead);
        }

        // Boomerang — flies out, then homes back to the hero; stuns most enemies (kills the weakest) and drags
        // loose drops back to you, like the original.
        private void StepBoomerang(Shot sh)
        {
            if (!sh.Ret)
            {
                sh.X += sh.Vx; sh.Y += sh.Vy; sh.Dist += 3.2f;
                if (sh.Dist > 88f || !MobWalk(sh.X, sh.Y)) sh.Ret = true;
            }
            else
            {
                float dx = (_hx + 4) - sh.X, dy = (_hy + 4) - sh.Y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < 6f) { sh.Dead = true; return; }
                sh.X += dx / d * 3.6f; sh.Y += dy / d * 3.6f;
            }
            foreach (var m in _mobs)
                if (!m.Dead && Overlap(sh.X, sh.Y, 10, 10, m.X, m.Y, 16, 16))
                {
                    if (m.Kind == 1) DamageMob(m);                      // weakest die
                    else { Sounds.Enqueue(Sfx.EnemyHit); m.Cool = Math.Max(m.Cool, 30); m.Move = 0; }   // others stun
                }
            foreach (var dp in _drops) if (Overlap(sh.X, sh.Y, 12, 12, dp.X, dp.Y, 12, 12)) { dp.X = _hx; dp.Y = _hy; }
        }

        // Candle flame burns a bush/tree tile on the current screen into open ground (reveals passages, Zelda-style).
        private void BurnBush(float x, float y)
        {
            int col = (int)x / Tile, row = ((int)y - HudH) / Tile;
            if (col < 0 || col >= Cols || row < 0 || row >= Rows) return;
            var s = Screen;
            if (row < s.Length && col < s[row].Length && s[row][col] == 'T')
            {
                var chars = s[row].ToCharArray(); chars[col] = '.';
                _screens[_wc << 8 | _wr][row] = new string(chars);
            }
        }

        private void StepDrops()
        {
            foreach (var d in _drops)
            {
                d.Life--;
                if (Overlap(_hx, _hy, 16, 16, d.X, d.Y, 12, 12))
                {
                    d.Life = 0;
                    if (d.Kind == 0) { _rupees++; Sounds.Enqueue(Sfx.Rupee); }
                    else if (d.Kind == 1) { _hp = Math.Min(_maxHp, _hp + 2); Sounds.Enqueue(Sfx.Heart); }
                    else if (d.Kind == 2) { _bombs += 4; _items.Add(Item.Bomb); if (_bItem == Item.None) _bItem = Item.Bomb; Sounds.Enqueue(Sfx.Pickup); }
                }
            }
            _drops.RemoveAll(d => d.Life <= 0);
        }

        private void HurtHero(int dmg, Dir away)
        {
            if (_invuln > 0) return;
            _hp -= dmg; _invuln = 60; _knock = 10; _knockDir = away; Sounds.Enqueue(Sfx.Hurt);
            if (_hp < 0) _hp = 0;
        }

        private Dir DirAwayFrom(float x, float y)
        {
            float dx = _hx - x, dy = _hy - y;
            if (System.Math.Abs(dx) > System.Math.Abs(dy)) return dx > 0 ? Dir.Right : Dir.Left;
            return dy > 0 ? Dir.Down : Dir.Up;
        }

        private static bool Overlap(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh)
            => ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        // ═══ rendering ═══════════════════════════════════════════════════════════════════════════════════════════
        private void Render()
        {
            Clear(1);   // wipe the whole frame first so nothing from the previous frame can bleed through
            switch (_state)
            {
                case State.Title: RenderTitle(); break;
                case State.Cave: RenderCave(); DrawHud(); break;
                case State.Sub: RenderSub(); DrawHud(); break;
                default: RenderPlay(); DrawHud(); break;
            }
        }

        private void RenderTitle()
        {
            Clear(1);
            FillRect(0, 40, 256, 60, 6);
            Text("THE  LEGEND", 78, 56, 17);
            Text("OF  FIRE", 96, 72, 17);
            Blit(ZeldaArt.ItemSword, 120, 100, false);
            Text("PRESS  START", 76, 176, 2);
            if ((_tick / 24) % 2 == 0) Text("A  OR  ENTER", 76, 190, 20);
        }

        private void RenderSub()
        {
            Clear(1);
            FillRect(8, HudH + 8, 116, 92, 12);       // B-item selection panel
            Text("INVENTORY", 24, HudH + 12, 13);
            Text("B", 16, HudH + 32, 2);
            var list = OwnedBItems();
            for (int i = 0; i < list.Count; i++)
            {
                int ix = 30 + (i % 4) * 24, iy = HudH + 44 + (i / 4) * 24;
                if (list[i] == _bItem) FillRect(ix - 3, iy - 3, 22, 22, 17);
                Blit(ItemIcon(list[i]), ix, iy, false);
            }
            if (list.Count == 0) Text("EMPTY", 44, HudH + 52, 2);

            FillRect(132, HudH + 8, 116, 92, 12);      // map panel
            Text("MAP", 176, HudH + 12, 2);
            DrawMinimap(146, HudH + 30, 88, 60);

            Text("HAVE-", 16, HudH + 112, 2);
            if (_hasSword) Blit(ZeldaArt.ItemSword, 60, HudH + 108, false);
        }

        private void RenderPlay()
        {
            if (_scrollT > 0) { RenderScroll(); return; }
            if (_inDungeon) { RenderDungeon(); return; }
            FillRect(0, HudH, Width, Height - HudH, 7);     // opaque grass base beneath transparent tile corners
            DrawScreenTiles(Screen, 0, HudH);
            DrawEntities(0, 0);
        }

        private void RenderDungeon()
        {
            var rm = Room;
            FillRect(0, HudH, Width, Height - HudH, 1);   // black floor
            if (rm == null) return;
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Cols; col++)
                    if (row <= 1 || row >= 9 || col <= 1 || col >= 14)
                        Blit(ZeldaArt.BlueBrick, col * 16, HudH + row * 16, false);
            CarveDoor(rm.N, 0, false); CarveDoor(rm.S, 1, rm.Entrance); CarveDoor(rm.E, 2, false); CarveDoor(rm.W, 3, false);
            for (int row = 2; row <= 8; row++)
                for (int col = 2; col <= 13; col++)
                {
                    char c = TileAt(rm.Tiles, col, row);
                    if (c == 'B') Blit(ZeldaArt.BlueBrick, col * 16, HudH + row * 16, false);
                    else if (c == 'W') Blit(ZeldaArt.Water, col * 16, HudH + row * 16, false);
                }
            if (rm.GivesBow && !_hasBow) Blit(ZeldaArt.BowItem, Width / 2 - 4, HudH + 80, false);
            if (rm.Boss && _roomsCleared.Contains(_dc << 8 | _dr)) Blit(ZeldaArt.Triforce, Width / 2 - 8, HudH + 72, false);
            if (_bossAlive) Blit(ZeldaArt.Aquamentus, (int)_bossX, (int)_bossY, false);
            DrawEntities(0, 0);
            if (_winFlash > 0 && (_winFlash / 6) % 2 == 0)
            { Text("YOU GOT A", 90, HudH + 28, 2); Text("TRIFORCE PIECE!", 68, HudH + 42, 17); }
        }

        // The full brick border is already drawn; this carves out a doorway only where a door actually exists.
        private void CarveDoor(byte type, int side, bool entrance)
        {
            if (type == ZeldaDungeon.Wall) return;   // no door — leave solid brick
            int c0, r0, c1, r1;
            switch (side)
            {
                case 0: c0 = 7; r0 = 0; c1 = 8; r1 = 1; break;
                case 1: c0 = 7; r0 = 9; c1 = 8; r1 = 10; break;
                case 2: c0 = 14; r0 = 5; c1 = 15; r1 = 6; break;
                default: c0 = 0; r0 = 5; c1 = 1; r1 = 6; break;
            }
            bool open = (side == 1 && entrance) || DoorOpen(type);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    if (open) FillRect(c * 16, HudH + r * 16, 16, 16, 1);            // open = black gap
                    else { FillRect(c * 16, HudH + r * 16, 16, 16, 12); FillRect(c * 16 + 6, HudH + r * 16, 4, 16, 9); }   // closed bars
                }
        }

        private void RenderScroll()
        {
            FillRect(0, HudH, Width, Height - HudH, 7);
            int p = 24 - _scrollT;                          // 0..24
            float f = p / 24f;
            int ox = (int)(_scrollDX * f * 256), oy = (int)(_scrollDY * f * 176);
            // old screen sliding out, new sliding in (offset by full screen the other way)
            DrawScreenTiles(_scrollFrom, ox, HudH + oy);
            DrawScreenTiles(Screen, ox - _scrollDX * 256, HudH + oy - _scrollDY * 176);
        }

        private void RenderCave()
        {
            // Brown cave-wall texture everywhere, framing a black room. Old man top-centre between two flame
            // braziers, the gold gift-sword below him, the hero at the bottom — the classic cave scene.
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Cols; col++)
                    Blit(ZeldaArt.CaveWall, col * 16, HudH + row * 16, false);
            FillRect(32, HudH + 16, Width - 64, Height - HudH - 32, 1);   // black room interior

            Text("IT'S DANGEROUS TO GO", 44, HudH + 22, 2);
            Text("ALONE! TAKE THIS.", 44, HudH + 34, 2);

            int cx = Width / 2;
            Blit(ZeldaArt.Flame, cx - 44, HudH + 58, false);
            Blit(ZeldaArt.Flame, cx + 28, HudH + 58, false);
            Blit(ZeldaArt.OldMan, cx - 8, HudH + 58, false);
            if (!CaveCleared) { var giftSpr = CaveIsSword ? ZeldaArt.CaveSword : ItemIcon(CaveBItem); if (giftSpr != null) Blit(giftSpr, cx - 4, HudH + 84, false); }
            DrawHero((int)_hx, (int)_hy);
        }

        private void DrawScreenTiles(string[] s, int ox, int oy)
        {
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Cols; col++)
                {
                    char c = TileAt(s, col, row);
                    Blit(ZeldaWorld.TileSprite(c), ox + col * 16, oy + row * 16, false);
                }
        }

        private void DrawEntities(int ox, int oy)
        {
            foreach (var d in _drops)
            {
                if (((_tick / 8) % 2) == 0 && d.Life < 100) continue; // blink before expiry
                Blit(d.Kind == 0 ? ZeldaArt.Rupee : d.Kind == 1 ? ZeldaArt.HeartFull : ZeldaArt.Bomb, (int)d.X + 4, (int)d.Y + 2, false);
            }
            foreach (var m in _mobs) Blit(MobSprite(m), (int)m.X, (int)m.Y, false);
            foreach (var b in _liveBombs) Blit(ZeldaArt.Bomb, (int)b.X + 4, (int)b.Y + 4, false);
            foreach (var sh in _shots)
            {
                switch (sh.Kind)
                {
                    case 0: Blit(_inDungeon ? ZeldaArt.Fireball : ZeldaArt.Pebble, (int)sh.X, (int)sh.Y, false); break;
                    case 1: FillRect((int)sh.X, (int)sh.Y, 6, 6, 20); break;                                    // sword beam
                    case 2: Blit(ZeldaArt.BoomerangSpr, (int)sh.X, (int)sh.Y, ((_tick / 3) % 2) == 0); break;   // boomerang
                    case 3: FillRect((int)sh.X, (int)sh.Y, sh.Vx != 0 ? 8 : 3, sh.Vx != 0 ? 3 : 8, 2); break;   // arrow
                    case 4: FillRect((int)sh.X, (int)sh.Y, 6, 6, 18); break;                                    // candle flame
                }
            }
            foreach (var bl in _blasts) DrawBlast((int)bl.X, (int)bl.Y, bl.Life);
            if (_invuln == 0 || (_tick / 4) % 2 == 0) DrawHero((int)_hx, (int)_hy);
            DrawSword();
        }

        private void DrawBlast(int cx, int cy, int life)
        {
            int r = 4 + (18 - life);
            int col = (life / 3) % 2 == 0 ? 17 : 2;   // yellow/white flicker
            FillRect(cx - r, cy - 3, r * 2, 6, col);
            FillRect(cx - 3, cy - r, 6, r * 2, col);
        }

        private ZeldaArt.Sprite MobSprite(Mob m)
        {
            switch (m.Kind)
            {
                case 2: return ZeldaArt.Keese;
                case 3: return ZeldaArt.Gel;
                case 4: return ZeldaArt.Stalfos;
                default: return (m.Anim / 8) % 2 == 0 ? ZeldaArt.Octorok0 : ZeldaArt.Octorok1;
            }
        }

        private void DrawHero(int x, int y)
        {
            bool step = (_walkAnim / 8) % 2 == 1;
            switch (_hdir)
            {
                case Dir.Down: Blit(step ? ZeldaArt.HeroDown1 : ZeldaArt.HeroDown0, x, y, false); break;
                case Dir.Up: Blit(step ? ZeldaArt.HeroUp1 : ZeldaArt.HeroUp0, x, y, false); break;
                case Dir.Left: Blit(step ? ZeldaArt.HeroSide1 : ZeldaArt.HeroSide0, x, y, false); break;
                case Dir.Right: Blit(step ? ZeldaArt.HeroSide1 : ZeldaArt.HeroSide0, x, y, true); break;
            }
        }

        private void DrawSword()
        {
            if (_attack == 0) return;
            int x = (int)_hx, y = (int)_hy;
            switch (_hdir)
            {
                case Dir.Up: Blit(ZeldaArt.SwordV, x + 5, y - 14, false); break;
                case Dir.Down: Blit(ZeldaArt.SwordV, x + 5, y + 14, true); break;
                case Dir.Left: Blit(ZeldaArt.SwordH, x - 14, y + 5, true); break;
                case Dir.Right: Blit(ZeldaArt.SwordH, x + 14, y + 5, false); break;
            }
        }

        // ── HUD ──────────────────────────────────────────────────────────────────────────────────────────────────
        private void DrawHud()
        {
            FillRect(0, 0, Width, HudH, 1);   // black status bar

            if (_inDungeon) { Text("LEVEL-" + _dungeonLevel, 8, 2, 2); DrawDungeonMap(8, 12, 60, 42); }
            else DrawMinimap(8, 10, 60, 44);

            // rupee / key / bomb counters (icon + count)
            Blit(ZeldaArt.HudRupee, 78, 10, false); Text("X" + _rupees.ToString("D3"), 88, 11, 2);
            Blit(ZeldaArt.HudKey, 78, 26, false); Text("X" + _keys.ToString("D2"), 88, 27, 2);
            Blit(ZeldaArt.HudBomb, 78, 42, false); Text("X" + _bombs.ToString("D2"), 88, 43, 2);

            // B (item) and A (sword) boxes
            Text("B", 150, 4, 2); Text("A", 178, 4, 2);
            FillRect(148, 14, 22, 34, 9); FillRect(150, 16, 18, 30, 1);
            if (_bItem != Item.None) { var bi = ItemIcon(_bItem); if (bi != null) Blit(bi, 154, 24, false); }
            FillRect(176, 14, 22, 34, 9); FillRect(178, 16, 18, 30, 1);
            if (_hasSword) Blit(ZeldaArt.ItemSword, 182, 22, false);

            // LIFE meter
            Text("-LIFE-", 204, 8, 13);
            for (int i = 0; i < _maxHp / 2; i++)
            {
                int hx = 202 + (i % 8) * 7, hy = 24 + (i / 8) * 10;
                var spr = _hp >= (i + 1) * 2 ? ZeldaArt.HeartFull : _hp == i * 2 + 1 ? ZeldaArt.HeartHalf : ZeldaArt.HeartEmpty;
                Blit(spr, hx, hy, false);
            }
        }

        // Overworld minimap — existing screens as cells, the current one blinking. Grows as the map does.
        private void DrawMinimap(int ox, int oy, int w, int h)
        {
            FillRect(ox, oy, w, h, 12);
            int minC = 99, maxC = -99, minR = 99, maxR = -99;
            foreach (var k in _screens.Keys) { int c = k >> 8, r = k & 0xff; if (c < minC) minC = c; if (c > maxC) maxC = c; if (r < minR) minR = r; if (r > maxR) maxR = r; }
            if (maxC < minC) return;
            int cols = Math.Max(maxC - minC + 1, 8), rows = Math.Max(maxR - minR + 1, 4);
            int cw = Math.Max(2, w / cols), ch = Math.Max(2, h / rows);
            foreach (var k in _screens.Keys)
            {
                int c = (k >> 8) - minC, r = (k & 0xff) - minR;
                FillRect(ox + c * cw, oy + r * ch, cw - 1, ch - 1, 20);
            }
            int col = ((_tick / 12) % 2 == 0) ? 2 : 13;
            FillRect(ox + (_wc - minC) * cw, oy + (_wr - minR) * ch, cw - 1, ch - 1, col);
        }

        private void DrawDungeonMap(int ox, int oy, int w, int h)
        {
            FillRect(ox, oy, w, h, 1);
            if (_dungeon == null) return;
            int minC = 99, maxC = -99, minR = 99, maxR = -99;
            foreach (var k in _dungeon.Keys) { int c = k >> 8, r = k & 0xff; if (c < minC) minC = c; if (c > maxC) maxC = c; if (r < minR) minR = r; if (r > maxR) maxR = r; }
            int cols = Math.Max(maxC - minC + 1, 6), rows = Math.Max(maxR - minR + 1, 6);
            int cw = Math.Max(3, w / cols), ch = Math.Max(3, h / rows);
            foreach (var k in _roomsVisited) { int c = (k >> 8) - minC, r = (k & 0xff) - minR; FillRect(ox + c * cw, oy + r * ch, cw - 1, ch - 1, 11); }
            int col = ((_tick / 12) % 2 == 0) ? 2 : 13;
            FillRect(ox + (_dc - minC) * cw, oy + (_dr - minR) * ch, cw - 1, ch - 1, col);
        }

        // ── low-level raster ─────────────────────────────────────────────────────────────────────────────────────
        private void Clear(int pi)
        {
            var c = ZeldaArt.Palette[pi];
            for (int i = 0; i < Width * Height; i++) { int o = i * 4; _rgba[o] = c[0]; _rgba[o + 1] = c[1]; _rgba[o + 2] = c[2]; _rgba[o + 3] = 255; }
        }

        private void Px2(int x, int y, int pi)
        {
            if ((uint)x >= Width || (uint)y >= Height) return;
            var c = ZeldaArt.Palette[pi]; int o = (y * Width + x) * 4;
            _rgba[o] = c[0]; _rgba[o + 1] = c[1]; _rgba[o + 2] = c[2]; _rgba[o + 3] = 255;
        }

        private void FillRect(int x, int y, int w, int h, int pi)
        {
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Px2(x + i, y + j, pi);
        }

        private void Blit(ZeldaArt.Sprite s, int dx, int dy, bool flipX)
        {
            if (s == null) return;
            for (int y = 0; y < s.H; y++)
                for (int x = 0; x < s.W; x++)
                {
                    byte pi = s.Idx[y * s.W + (flipX ? s.W - 1 - x : x)];
                    if (pi == 0) continue;
                    Px2(dx + x, dy + y, pi);
                }
        }

        private void Text(string t, int x, int y, int pi)
        {
            int cx = x;
            foreach (char raw in t)
            {
                char ch = char.ToUpperInvariant(raw);
                if (ZeldaArt.Font.TryGetValue(ch, out var g)) BlitTinted(g, cx, y, pi);
                cx += 8;
            }
        }

        // font glyphs are white; recolor 'W' pixels to the requested palette index.
        private void BlitTinted(ZeldaArt.Sprite s, int dx, int dy, int pi)
        {
            for (int y = 0; y < s.H; y++)
                for (int x = 0; x < s.W; x++)
                    if (s.Idx[y * s.W + x] != 0) Px2(dx + x, dy + y, pi);
        }
    }
}
