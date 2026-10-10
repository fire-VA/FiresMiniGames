using System.Collections.Generic;

namespace FiresMiniGames.Modules.Zelda
{
    /// <summary>
    /// Original pixel art for the built-in top-down adventure — a Zelda-like, none of Nintendo's assets. Sprites
    /// are authored as rows of characters mapped to a small palette; <see cref="Compile"/> pads short rows so the
    /// hand-drawn art doesn't have to be counted to the pixel. Everything here is UnityEngine-free (bytes/ints only)
    /// so the whole game can be render-verified in a headless net harness before it ever runs in-game.
    /// </summary>
    internal static class ZeldaArt
    {
        // RGBA palette. Index 0 is transparent.
        public static readonly byte[][] Palette =
        {
            new byte[]{0,0,0,0},          // 0  transparent
            new byte[]{0,0,0,255},        // 1  black    x
            new byte[]{252,252,252,255},  // 2  white    W
            new byte[]{252,216,168,255},  // 3  skin     s
            new byte[]{176,112,32,255},   // 4  brown    b
            new byte[]{0,176,0,255},       // 5  green    g
            new byte[]{0,104,0,255},       // 6  dk green G
            new byte[]{104,168,72,255},   // 7  grass green #(field base)
            new byte[]{64,128,56,255},    // 8  grass dk    ,(tuft)
            new byte[]{120,120,128,255},  // 9  rock     e
            new byte[]{72,72,80,255},      // 10 rock dk  E
            new byte[]{56,104,224,255},   // 11 water    u
            new byte[]{24,56,152,255},     // 12 water dk U
            new byte[]{216,40,40,255},     // 13 red      r
            new byte[]{248,144,120,255},  // 14 lt red   R
            new byte[]{104,184,72,255},    // 15 tree     t
            new byte[]{32,96,32,255},       // 16 tree dk  T
            new byte[]{248,216,72,255},    // 17 yellow   y
            new byte[]{248,160,68,255},    // 18 orange   o
            new byte[]{176,80,32,255},      // 19 orange dk O
            new byte[]{144,220,248,255},  // 20 cyan     c
            new byte[]{200,80,160,255},    // 21 magenta  m
            new byte[]{142,80,30,255},     // 22 cave brown    n
            new byte[]{80,44,14,255},      // 23 cave brown dk N
        };

        private static readonly Dictionary<char, byte> Key = new Dictionary<char, byte>
        {
            ['.'] = 0, [' '] = 0, ['x'] = 1, ['W'] = 2, ['s'] = 3, ['b'] = 4, ['g'] = 5, ['G'] = 6,
            [','] = 8, ['e'] = 9, ['E'] = 10, ['u'] = 11, ['U'] = 12, ['r'] = 13, ['R'] = 14,
            ['t'] = 15, ['T'] = 16, ['y'] = 17, ['o'] = 18, ['O'] = 19, ['c'] = 20, ['m'] = 21, ['#'] = 7,
            ['n'] = 22, ['N'] = 23,
        };

        public sealed class Sprite
        {
            public readonly int W, H;
            public readonly byte[] Idx;
            public Sprite(int w, int h, byte[] idx) { W = w; H = h; Idx = idx; }
        }

        public static Sprite Compile(int w, int h, params string[] rows)
        {
            var idx = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                string row = y < rows.Length ? rows[y] : "";
                for (int x = 0; x < w; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    idx[y * w + x] = Key.TryGetValue(c, out var v) ? v : (byte)0;
                }
            }
            return new Sprite(w, h, idx);
        }

        // ── overworld tiles (16x16) ─────────────────────────────────────────────────────────────────────────────
        // Overworld ground — flat tan like the original, a couple of faint specks so large fields aren't dead-flat.
        public static readonly Sprite Grass = Compile(16, 16,
            "################", "################", "################", "############,###",
            "################", "################", "###,############", "################",
            "################", "################", "################", "#########,######",
            "################", "################", "################", "################");

        public static readonly Sprite Rock = Compile(16, 16,
            "..eeeeeeeeeeee..", ".eeEEEEEEEEEEEe.", "eeEEeeeeeeeeEEEe", "eEEeeeeeeeeeeEEe",
            "eEeeeeeWeeeeeeEe", "eEeeeeeeeeeeeeEe", "eEeeeeeeeeeeeeEe", "eEeeeeeeeeeWeeEe",
            "eEeeeeeeeeeeeeEe", "eEeeeeeeeeeeeeEe", "eEeeeeeWeeeeeeEe", "eEeeeeeeeeeeeeEe",
            "eEEeeeeeeeeeeEEe", "eeEEeeeeeeeeEEEe", ".eeEEEEEEEEEEEe.", "..eeeeeeeeeeee..");

        public static readonly Sprite Tree = Compile(16, 16,
            "...tt....tt.....", "..tTtt..ttTt....", ".tTttttttttTt...", "tTtttttttttttt..",
            "tTttttttttttttt.", "tTttttttttttttt.", "tTttttttttttttt.", ".tTtttttttttTt..",
            "..ttTtttttTtt...", "....ttbbbbtt....", ".....bbbbbb.....", ".....bTTTTb.....",
            ".....bTbbTb.....", ".....bTbbTb.....", "....bbTbbTbb....", "................");

        public static readonly Sprite Water = Compile(16, 16,
            "uuuuuuuuuuuuuuuu", "uuuuuuuuuuuuuuuu", "uuUuuuuuuUuuuuuu", "uucuuuuuucuuuuuu",
            "uuuuuuuuuuuuuuUu", "uuuuuUuuuuuuuucu", "uuuuuuuuuuuuuuuu", "Uuuuuuuuuuuuuuuu",
            "cuuuuuuuUuuuuuuu", "uuuuuuuucuuuuuuu", "uuuuuuuuuuuuUuuu", "uuUuuuuuuuuuucuu",
            "uucuuuuuuuuuuuuu", "uuuuuuuuuUuuuuuu", "uuuuuuuuucuuuuuu", "uuuuuuuuuuuuuuuu");

        public static readonly Sprite Sand = Compile(16, 16,
            "yyyyyyyyyyyyyyyy", "yyyyy,yyyyyyyyyy", "yyyyyyyyyyy,yyyy", "yy,yyyyyyyyyyyyy",
            "yyyyyyyyy,yyyyyy", "yyyyyyyyyyyyyyyy", "yyyy,yyyyyyyy,yy", "yyyyyyyyyyyyyyyy",
            "yyyyyyy,yyyyyyyy", "yy,yyyyyyyyyyyyy", "yyyyyyyyyyyyy,yy", "yyyyy,yyyyyyyyyy",
            "yyyyyyyyyy,yyyyy", "yyy,yyyyyyyyyyyy", "yyyyyyyyyyyyyyyy", "yyyyyyy,yyyyyyyy");

        public static readonly Sprite CaveMouth = Compile(16, 16,
            "eeeeeeeeeeeeeeee", "eEEEEEEEEEEEEEEe", "eEeeeeeeeeeeeeEe", "eEeeeexxxxeeeeEe",
            "eEeeexxxxxxeeeEe", "eEeexxxxxxxxeeEe", "eEeexxxxxxxxeeEe", "eEexxxxxxxxxxeEe",
            "eEexxxxxxxxxxeEe", "eEexxxxxxxxxxeEe", "eEexxxxxxxxxxeEe", "eEexxxxxxxxxxeEe",
            "eEexxxxxxxxxxeEe", "eEexxxxxxxxxxeEe", "eEexxxxxxxxxxeEe", "eeexxxxxxxxxxeee");

        public static readonly Sprite Ground = Compile(16, 16,
            "################", "################", "################", "################",
            "################", "################", "################", "################",
            "################", "################", "################", "################",
            "################", "################", "################", "################");

        // ── hero (16x16), green tunic + cap ─────────────────────────────────────────────────────────────────────
        public static readonly Sprite HeroDown0 = Compile(16, 16,
            "................", "....xxxxxxxx....", "...xGGGGGGGGx...", "..xGggggggggGx..",
            "..xgssssssssgx..", "..xssxsssxsssx..", "..xsssssssssx...", "...xsssssssx....",
            "...xxggggggxx...", "..xggxggggxggx..", "..xggxggggxggx..", "..sxgxggggxgxs..",
            "..sxxxbbbbxxxs..", "....xbbbbbbx....", "....xbx..xbx....", "....xx....xx....");

        public static readonly Sprite HeroDown1 = Compile(16, 16,
            "................", "....xxxxxxxx....", "...xGGGGGGGGx...", "..xGggggggggGx..",
            "..xgssssssssgx..", "..xssxsssxsssx..", "..xsssssssssx...", "...xsssssssx....",
            "...xxggggggxx...", "..xggxggggxggx..", "..sxgxggggxgxs..", "..sxxgggggg xs..",
            "...xbbbbbbbbx...", "...xbbx..xbbx...", "...xxx....xbx...", "..........xx....");

        public static readonly Sprite HeroUp0 = Compile(16, 16,
            "................", "....xxxxxxxx....", "...xGGGGGGGGx...", "..xGggggggggGx..",
            "..xgggggggggx...", "..xgggggggggx...", "..xgggggggggx...", "...xgggggggx....",
            "...xxggggggxx...", "..xggxggggxggx..", "..xggxggggxggx..", "..sxgxggggxgxs..",
            "..sxxxbbbbxxxs..", "....xbbbbbbx....", "....xbx..xbx....", "....xx....xx....");

        public static readonly Sprite HeroUp1 = Compile(16, 16,
            "................", "....xxxxxxxx....", "...xGGGGGGGGx...", "..xGggggggggGx..",
            "..xgggggggggx...", "..xgggggggggx...", "..xgggggggggx...", "...xgggggggx....",
            "...xxggggggxx...", "..xggxggggxggx..", "..sxgxggggxgxs..", "..sxxgggggg xs..",
            "...xbbbbbbbbx...", "...xbbx..xbbx...", "...xbx....xxx...", "....xx..........");

        // facing LEFT (right = flipX). sword arm/side profile.
        public static readonly Sprite HeroSide0 = Compile(16, 16,
            "................", "...xxxxxxx......", "..xGGGGGGGx.....", ".xGgggggggGx....",
            ".xgsssssgggx....", ".xssxssgggx.....", ".xsssssgggx.....", "..xsssgggx......",
            "..xxgggggxx.....", ".xggggggggx.....", ".xggggggggx.....", ".sxggggggxs.....",
            ".sxxbbbbxxs.....", "..xbbbbbbx......", "..xbx.xbx.......", "..xx...xx.......");

        public static readonly Sprite HeroSide1 = Compile(16, 16,
            "................", "...xxxxxxx......", "..xGGGGGGGx.....", ".xGgggggggGx....",
            ".xgsssssgggx....", ".xssxssgggx.....", ".xsssssgggx.....", "..xsssgggx......",
            "..xxgggggxx.....", ".xggggggggx.....", ".sxgggggggx.....", ".sxxgggggxs.....",
            "..xbbbbbbx......", "...xbbx.xbx.....", "..xbx....xx.....", "..xx............");

        // sword blade (horizontal, points right; rotate/flip per facing when blitting)
        public static readonly Sprite SwordH = Compile(16, 6,
            "..............yy", "cccccccccccccWWy", "WWWWWWWWWWWWWWyy", "cccccccccccccWWy",
            "..............yy", "................");
        public static readonly Sprite SwordV = Compile(6, 16,
            "..cWc.", ".cWWc.", ".cWWc.", ".cWWc.", ".cWWc.", ".cWWc.", ".cWWc.", ".cWWc.",
            ".cWWc.", ".cWWc.", ".cWWc.", ".cWWc.", "yWWWy", "yyWyy", ".yWy.", "..y..");

        // ── enemies ─────────────────────────────────────────────────────────────────────────────────────────────
        public static readonly Sprite Octorok0 = Compile(16, 16,
            "................", "....oooooo......", "...oooooooo.....", "..oooooooooo....",
            "..oOWooooWOo....", "..ooWWooWWoo....", "..oooooooooo....", "..oooo,,oooo....",
            "..oooooooooo....", "...oOooooOo.....", "..oOoOooOoOo....", ".oOo.oOo.oOo....",
            ".Oo...oo...Oo...", "................", "................", "................");
        public static readonly Sprite Octorok1 = Compile(16, 16,
            "................", "....oooooo......", "...oooooooo.....", "..oooooooooo....",
            "..oOWooooWOo....", "..ooWWooWWoo....", "..oooooooooo....", "..oooo,,oooo....",
            "..oooooooooo....", "...oOooooOo.....", "..oOooooooOo....", "..Oo.oOoo.oO....",
            "...Oo.oo.oO.....", "................", "................", "................");

        // enemy rock projectile
        public static readonly Sprite Pebble = Compile(8, 8,
            "..EEEE..", ".EeeeeE.", "EeeeWeeE", "EeeeeeeE", "EeeeeeeE", "EeeWeeeE", ".EeeeeE.", "..EEEE..");

        // ── items / HUD ─────────────────────────────────────────────────────────────────────────────────────────
        public static readonly Sprite HeartFull = Compile(8, 8,
            ".rr.rr..", "rRRrRRr.", "rRRRRRr.", "rRRRRRr.", ".rRRRr..", "..rRr...", "...r....", "........");
        public static readonly Sprite HeartHalf = Compile(8, 8,
            ".rr.x...", "rRRrx...", "rRRRx...", "rRRRx...", ".rRRx...", "..rx....", "...x....", "........");
        public static readonly Sprite HeartEmpty = Compile(8, 8,
            ".xx.xx..", "x..x..x.", "x.....x.", "x.....x.", ".x...x..", "..x.x...", "...x....", "........");
        public static readonly Sprite Rupee = Compile(8, 12,
            "...WW...", "..gWWg..", ".gWggWg.", "ggWggWgg", "ggggggWg", "ggWggggg", "gggggggg", ".ggWWgg.",
            ".gggggg.", "..gWWg..", "...gg...", "........");
        public static readonly Sprite ItemSword = Compile(8, 16,
            "...WW...", "...WW...", "...WW...", "...WW...", "..cWWc..", "..cWWc..", "..cWWc..", "..cWWc..",
            "..cWWc..", "..cWWc..", ".bbbbbb.", "...bb...", "...bb...", "...bb...", "..bbbb..", "........");
        public static readonly Sprite Bomb = Compile(8, 8,
            "....xx..", "...xWx..", "..xxxx..", ".xEEEEx.", "xEEEEEEx", "xEEEEEEx", ".xEEEEx.", "..xxxx..");

        // The old man of the caves — red robe, white beard, tan face (matches the original).
        public static readonly Sprite OldMan = Compile(16, 16,
            "................", ".....xxxxxx.....", "....xOOOOOOx....", "....xssssssx....",
            "....xsxssxsx....", "....xWWWWWWx....", "...xOWWWWWWOx...", "...xOOOOOOOOx...",
            "..xOOOOOOOOOOx..", "..xOOOOOOOOOOx..", "..xOOOOOOOOOOx..", "..xOOOOOOOOOOx..",
            "..xOOOOOOOOOOx..", "..xOOOOOOOOOOx..", "..xOOOOOOOOOOx..", "...xxOOOOxx....");

        // A bright fire-brazier flame (mostly yellow/orange) — the cave torches.
        public static readonly Sprite Flame = Compile(16, 16,
            "................", ".......y........", "......yoy.......", "......yoy.......",
            ".....yoooy......", ".....yooyy......", "....yooooy......", "...yyoooooy.....",
            "...yooyoooy.....", "..yoooooooy.....", "..yooyyoooy.....", ".yoooooooooy....",
            ".yoooooooooy....", "..rooooooor.....", "...roooooor.....", "....rrrrrr......");

        // Brown cave-wall texture — opaque; tiles across the cave/dungeon borders (dark-brown blobs on brown).
        public static readonly Sprite CaveWall = Compile(16, 16,
            "nnnnnnnnnnnnnnnn", "nNNnnnnNNnnnnNNn", "NNNnnnNNNnnnNNNn", "nNNnnnnNNnnnnNNn",
            "nnnnnnnnnnnnnnnn", "nnnnNNnnnnNNnnnn", "nnnNNNnnnNNNnnnn", "nnnnNNnnnnNNnnnn",
            "nnnnnnnnnnnnnnnn", "nNNnnnnNNnnnnNNn", "NNNnnnNNNnnnNNNn", "nNNnnnnNNnnnnNNn",
            "nnnnnnnnnnnnnnnn", "nnnnNNnnnnNNnnnn", "nnnNNNnnnNNNnnnn", "nnnnNNnnnnNNnnnn");

        // The gift sword shown in the cave — gold blade pointing down.
        public static readonly Sprite CaveSword = Compile(8, 16,
            "...bb...", "...bb...", ".bbbbbb.", "..yWWy..", "..yWWy..", "..yWWy..", "..yWWy..", "..yWWy..",
            "..yWWy..", "..yWWy..", "..yWWy..", "..yWWy..", "...WW...", "...WW...", "....W...", "........");

        // ── dungeon tiles + creatures ───────────────────────────────────────────────────────────────────────────
        public static readonly Sprite BlueBrick = Compile(16, 16,
            "uuuuuuuuuuuuuuuu", "uUUUUUUUUUUUUUUu", "uuuuuuuUuuuuuuuu", "uuuuuuuUuuuuuuuu",
            "uuuuuuuUuuuuuuuu", "uuuuuuuUuuuuuuuu", "uuuuuuuUuuuuuuuu", "uUUUUUUUUUUUUUUu",
            "uuuUuuuuuuuUuuuu", "uuuUuuuuuuuUuuuu", "uuuUuuuuuuuUuuuu", "uuuUuuuuuuuUuuuu",
            "uuuUuuuuuuuUuuuu", "uuuUuuuuuuuUuuuu", "uUUUUUUUUUUUUUUu", "uuuuuuuuuuuuuuuu");

        public static readonly Sprite Keese = Compile(16, 16,
            "................", "................", "..E.E......E.E..", ".EEEEE....EEEEE.",
            "EEEEEEEEEEEEEEE.", "EWEEEEEEEEEEEWE.", ".EEEEEEEEEEEEE..", "..E.EE...EE.E...",
            "................", "................", "................", "................",
            "................", "................", "................", "................");

        public static readonly Sprite Gel = Compile(16, 16,
            "................", "................", "................", ".....uuuuu......",
            "....uuuuuuu.....", "....uWuuuWu.....", "....uuuuuuu.....", "....uuuuuuu.....",
            ".....uuuuu......", "................", "................", "................",
            "................", "................", "................", "................");

        public static readonly Sprite Stalfos = Compile(16, 16,
            "................", "....WWWWWW......", "...WWWWWWWW.....", "...WxWWWWxW.....",
            "...WWWWWWWW.....", "....WWWWWW......", "...WWWWWWWW.....", "..W.WWWWWW.W....",
            "..W.WWWWWW.W....", "...WWWWWWWW.....", "...WW....WW.....", "...WW....WW.....",
            "................", "................", "................", "................");

        public static readonly Sprite Fireball = Compile(8, 8,
            "..oooo..", ".oooyoo.", "ooyyyoo.", "ooyyyoo.", "ooyyyoo.", ".oooooo.", "..oooo..", "........");

        public static readonly Sprite BowItem = Compile(8, 16,
            "...yy...", "..y..y..", ".y....y.", "y.....W.", "y....W..", "y...W...", "y..W....", "y.W.....",
            "y..W....", "y...W...", "y....W..", "y.....W.", ".y....y.", "..y..y..", "...yy...", "........");

        public static readonly Sprite HeartBig = Compile(16, 16,
            "................", "..rrr..rrr......", ".rRRRrrRRRr.....", "rRRRRRRRRRRr....",
            "rRRRRRRRRRRr....", "rRRRRRRRRRRr....", ".rRRRRRRRRr.....", "..rRRRRRRr......",
            "...rRRRRr.......", "....rRRr........", ".....rr.........", "................",
            "................", "................", "................", "................");

        public static readonly Sprite Triforce = Compile(16, 16,
            "................", ".......yy.......", "......yyyy......", ".....yy..yy.....",
            "....yyyyyyyy....", "...yy......yy...", "..yyyy....yyyy..", ".yy..yy..yy..yy.",
            "yyyyyyyyyyyyyyyy", "................", "................", "................",
            "................", "................", "................", "................");

        // Aquamentus — a green dragon with a horn, faces left, spits fireballs from its mouth.
        public static readonly Sprite Aquamentus = Compile(24, 24,
            "........................", "..........WW............", ".........WWW............", "........gggg............",
            ".......gggggg...........", "......ggggggg...........", ".....ggrggggggg.........", "....gggggggggggg........",
            "...xxgggggggggggg.......", "...xxgggggggggggggg.....", "....ggggggggggggggg.....", ".....gggggggggggggggg...",
            ".....gGGGggggggggggggg..", "......ggggggggggggggg...", "......gggggggggggggg....", ".......gggggggggggg.....",
            ".......ggggggggggg......", "......gg.ggg.ggg.gg.....", "......gg.ggg.ggg.gg.....", "......GG.GGG.GGG.GG.....",
            "........................", "........................", "........................", "........................");

        public static readonly Sprite HudRupee = Compile(8, 8,
            "...WW...", "..gWWg..", ".gWWWWg.", "ggWWWWgg", "ggWWWWgg", ".gWWWWg.", "..gWWg..", "...gg...");
        public static readonly Sprite HudKey = Compile(8, 8,
            ".yyy....", "y...y...", "y...y...", ".yyy....", "..y.....", "..y.....", "..yy....", "..y.y...");
        public static readonly Sprite HudBomb = Compile(8, 8,
            "...x....", "..xx....", ".xEEx...", "xEEEEx..", "xEEEEx..", "xEEEEx..", ".xEEx...", "..xx....");

        public static readonly Sprite BoomerangSpr = Compile(8, 8,
            "..bbb...", ".bWWWb..", "b.....b.", "b.....b.", "b.....b.", ".b...b..", "..bbb...", "........");

        // ── 8x8 font: A-Z, 0-9, and a few symbols. Row string per glyph. ────────────────────────────────────────
        public static readonly Dictionary<char, Sprite> Font = BuildFont();

        private static Sprite G(params string[] r) => Compile(8, 8, r);

        private static Dictionary<char, Sprite> BuildFont()
        {
            var f = new Dictionary<char, Sprite>();
            void A(char c, params string[] rows) => f[c] = G(rows);
            A(' ', "........", "........", "........", "........", "........", "........", "........", "........");
            A('A', ".WWWW...", "W....W..", "W....W..", "WWWWWW..", "W....W..", "W....W..", "W....W..", "........");
            A('B', "WWWWW...", "W....W..", "WWWWW...", "W....W..", "W....W..", "WWWWW...", "........", "........");
            A('C', ".WWWW...", "W....W..", "W.......", "W.......", "W....W..", ".WWWW...", "........", "........");
            A('D', "WWWWW...", "W....W..", "W....W..", "W....W..", "W....W..", "WWWWW...", "........", "........");
            A('E', "WWWWWW..", "W.......", "WWWW....", "W.......", "W.......", "WWWWWW..", "........", "........");
            A('F', "WWWWWW..", "W.......", "WWWW....", "W.......", "W.......", "W.......", "........", "........");
            A('G', ".WWWW...", "W.......", "W.WWW...", "W....W..", "W....W..", ".WWWW...", "........", "........");
            A('H', "W....W..", "W....W..", "WWWWWW..", "W....W..", "W....W..", "W....W..", "........", "........");
            A('I', "WWWWW...", "..W.....", "..W.....", "..W.....", "..W.....", "WWWWW...", "........", "........");
            A('J', "...WWW..", "....W...", "....W...", "....W...", "W...W...", ".WWW....", "........", "........");
            A('K', "W...W...", "W..W....", "WWW.....", "W..W....", "W...W...", "W....W..", "........", "........");
            A('L', "W.......", "W.......", "W.......", "W.......", "W.......", "WWWWWW..", "........", "........");
            A('M', "W....W..", "WW..WW..", "W.WW.W..", "W....W..", "W....W..", "W....W..", "........", "........");
            A('N', "W....W..", "WW...W..", "W.W..W..", "W..W.W..", "W...WW..", "W....W..", "........", "........");
            A('O', ".WWWW...", "W....W..", "W....W..", "W....W..", "W....W..", ".WWWW...", "........", "........");
            A('P', "WWWWW...", "W....W..", "WWWWW...", "W.......", "W.......", "W.......", "........", "........");
            A('Q', ".WWWW...", "W....W..", "W....W..", "W..W.W..", "W...W...", ".WWW.W..", "........", "........");
            A('R', "WWWWW...", "W....W..", "WWWWW...", "W..W....", "W...W...", "W....W..", "........", "........");
            A('S', ".WWWWW..", "W.......", ".WWWW...", ".....W..", ".....W..", "WWWWW...", "........", "........");
            A('T', "WWWWWWW.", "...W....", "...W....", "...W....", "...W....", "...W....", "........", "........");
            A('U', "W....W..", "W....W..", "W....W..", "W....W..", "W....W..", ".WWWW...", "........", "........");
            A('V', "W....W..", "W....W..", "W....W..", ".W..W...", ".W..W...", "..WW....", "........", "........");
            A('W', "W....W..", "W....W..", "W....W..", "W.WW.W..", "WW..WW..", "W....W..", "........", "........");
            A('X', "W....W..", ".W..W...", "..WW....", "..WW....", ".W..W...", "W....W..", "........", "........");
            A('Y', "W....W..", ".W..W...", "..WW....", "...W....", "...W....", "...W....", "........", "........");
            A('Z', "WWWWWW..", "....W...", "...W....", "..W.....", ".W......", "WWWWWW..", "........", "........");
            A('0', ".WWWW...", "W...WW..", "W..W.W..", "W.W..W..", "WW...W..", ".WWWW...", "........", "........");
            A('1', "..W.....", ".WW.....", "..W.....", "..W.....", "..W.....", ".WWW....", "........", "........");
            A('2', ".WWWW...", "W....W..", "...WW...", "..W.....", ".W......", "WWWWWW..", "........", "........");
            A('3', "WWWWW...", ".....W..", "..WWW...", ".....W..", "W....W..", ".WWWW...", "........", "........");
            A('4', "...WW...", "..W.W...", ".W..W...", "WWWWWW..", "....W...", "....W...", "........", "........");
            A('5', "WWWWWW..", "W.......", "WWWWW...", ".....W..", "W....W..", ".WWWW...", "........", "........");
            A('6', ".WWWW...", "W.......", "WWWWW...", "W....W..", "W....W..", ".WWWW...", "........", "........");
            A('7', "WWWWWW..", ".....W..", "....W...", "...W....", "..W.....", "..W.....", "........", "........");
            A('8', ".WWWW...", "W....W..", ".WWWW...", "W....W..", "W....W..", ".WWWW...", "........", "........");
            A('9', ".WWWW...", "W....W..", "W....W..", ".WWWWW..", ".....W..", ".WWWW...", "........", "........");
            A('-', "........", "........", ".WWWWW..", "........", "........", "........", "........", "........");
            A('<', "...W....", "..W.....", ".W......", ".W......", "..W.....", "...W....", "........", "........");
            A('>', ".W......", "..W.....", "...W....", "...W....", "..W.....", ".W......", "........", "........");
            A(':', "........", "..WW....", "..WW....", "........", "..WW....", "..WW....", "........", "........");
            A('!', "..WW....", "..WW....", "..WW....", "..WW....", "..WW....", "........", "..WW....", "........");
            A('\'', "..WW....", "..WW....", "..W.....", "........", "........", "........", "........", "........");
            A('.', "........", "........", "........", "........", "........", "..WW....", "..WW....", "........");
            A(',', "........", "........", "........", "........", "........", "..WW....", "..WW....", ".WW.....");
            return f;
        }
    }
}
