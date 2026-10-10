using System;

namespace sGBA;

// CPU software PPU for the vendored sGBA core. The original renderer was a s&box GPU/compute-shader pipeline that was
// intentionally NOT vendored; GbaVideo.cs keeps the PPU REGISTER state and, from its scanline/VBlank timing hooks,
// calls exactly three members that live here: CaptureScanline(y), SnapshotVram(), CommitFrame(). This partial renders
// GBA graphics on the CPU, one scanline at a time, straight from the LIVE registers + VRAM/palette (no GPU snapshot
// needed — we render synchronously as the emulator reaches each line). Output: a 240x160 RGBA8888 framebuffer, row
// 0 = top, that GbaRuntime paints to a TV (exactly like GbRuntime / NesRuntime).
//
// STAGE 1+2 scope (ports gba_bg_mode0.shader + gba_obj.shader + gba_finalize.shader, see
// Tools/FIRES_GBA_PPU_RENDERER.md):
//   * backdrop (BG palette entry 0)
//   * BG Mode 0 tiled text backgrounds, all four layers, priority-sorted
//   * OBJ sprites: regular + affine (rot/scale, double-size), 4/8bpp, H/V flip, mosaic, 1D/2D tile mapping, the
//     per-scanline cycle-budget dropout, and the OBJ-window write (captured, not yet consumed)
//   * the compositor: composite OBJ first then the BGs, keep the two frontmost layers by priority, then
//     alpha/brighten/darken blending (with the semi-transparent-OBJ forced-alpha path active)
// Also implemented: windows (win0/win1/obj-window build a per-pixel 6-bit mask that gates each layer + the colour
// effect), affine BGs (mode 1 BG2, mode 2 BG2/BG3, from the BgX/BgY reference point + PA/PC), and the BG2 bitmap
// modes 3/4/5. Colour is BGR555 -> RGBA8 via (v<<3)|(v>>2) per channel. That completes the PPU feature set.
public partial class GbaVideo
{
	// 240x160 RGBA8888, row 0 = top (GbaRuntime flips into Texture2D's bottom-up rows, exactly like GbRuntime).
	private readonly byte[] _framebuffer = new byte[GbaConstants.ScreenWidth * GbaConstants.ScreenHeight * 4];
	private int _frameCount;

	// Per-scanline BG layer buffers, reused every scanline (no per-pixel allocation). One 240-wide line per BG 0-3:
	// the raw BGR555 colour and an opaque flag (tile index 0 is transparent and leaves the flag clear). Colour is
	// kept as 5-bit-per-channel so the compositor can blend in the GBA's native 5-bit domain, then expand once.
	private readonly int[] _bgColor = new int[4 * GbaConstants.ScreenWidth];
	private readonly bool[] _bgOpaque = new bool[4 * GbaConstants.ScreenWidth];

	// Per-scanline OBJ (sprite) layer buffer — ports gba_obj.shader's output for one 240-wide line. Per pixel: the raw
	// BGR555 colour, an opaque flag (tile index 0 is transparent), the winning sprite's priority (0-3) and its
	// semi-transparent flag, plus the OBJ-window flag written by FLAG_OBJWINDOW sprites. Windows are still forced to
	// mask 0x3F this stage, so _objWindow is populated and ready for the windows sub-stage but not yet consumed.
	private readonly int[] _objColor = new int[GbaConstants.ScreenWidth];
	private readonly bool[] _objOpaque = new bool[GbaConstants.ScreenWidth];
	private readonly int[] _objPriority = new int[GbaConstants.ScreenWidth];
	private readonly bool[] _objSemi = new bool[GbaConstants.ScreenWidth];
	private readonly bool[] _objWindow = new bool[GbaConstants.ScreenWidth];

	// Per-scanline window mask (gba_window.shader): 6 bits/pixel — 0-3 BG0-3, 4 OBJ, 5 colour-effect; 0x3F when no
	// window is enabled. The compositor gates each layer + the blend per pixel with it.
	private readonly byte[] _winMask = new byte[GbaConstants.ScreenWidth];

	// OBJ cycle budget (gba_obj.shader): per scanline, sprite rendering stops once this many render cycles are charged,
	// which reproduces hardware sprite dropout. The HBlank-interval-free budget (DISPCNT bit 5, the 1D-mapping bit that
	// also frees the HBlank OBJ fetch window) is the smaller of the two.
	private const int ObjLength = 1210;
	private const int ObjHblankFreeLength = 954;

	// Decoded sprite list, rebuilt from live OAM whenever it changed (ports GbaVideo.Rendering.cs CleanOam +
	// GetSpriteSize). One entry per visible OAM sprite; the per-scanline OBJ pass walks these in OAM order.
	private readonly ObjSprite[] _sprites = new ObjSprite[128];
	private int _spriteCount;

	private struct ObjSprite
	{
		public int X, Y;
		public int Width, Height;
		public int RenderWidth, RenderHeight;
		public uint CharBase, Tile, Stride, Palette;
		public uint Flags;
		public int PA, PB, PC, PD;
		public int Cycles;
	}

	// 17-entry reciprocal table for the mosaic floor (gba_common.hlsl mosaicTable / MosaicFloor).
	private static readonly int[] MosaicTable =
	{
		0, 4096, 2048, 1366, 1024, 820, 683, 586,
		512, 456, 410, 373, 342, 316, 293, 274, 256
	};

	/// <summary>The current 240x160 frame as RGBA8888 bytes (row 0 = top). Length = 240*160*4.</summary>
	public byte[] GetFramebuffer() => _framebuffer;

	/// <summary>How many frames have been committed since construction.</summary>
	public int StubFrameCount => _frameCount;

	// Render scanline y synchronously from the LIVE registers + VRAM/palette. Called from GbaVideo.StartHBlank for the
	// line just finished (VCount 0..159). Fills framebuffer row y with 240 RGBA pixels.
	private void CaptureScanline( int y )
	{
		if ( (uint)y >= (uint)GbaConstants.ScreenHeight )
			return;

		byte[] fb = _framebuffer;
		int rowBase = y * GbaConstants.ScreenWidth * 4;

		// Forced blank (DISPCNT bit 7): the whole line is white.
		if ( (DispCnt & 0x0080) != 0 )
		{
			for ( int x = 0; x < GbaConstants.ScreenWidth; x++ )
			{
				int p = rowBase + x * 4;
				fb[p] = 255; fb[p + 1] = 255; fb[p + 2] = 255; fb[p + 3] = 255;
			}
			return;
		}

		int mode = DispCnt & 7;

		// Which BGs exist + how they draw depends on the mode (each still gated by _enabledAtY, gba_bg's EnabledAtYMask):
		// BG0/BG1 are text layers in modes 0-1 only; BG2 exists in EVERY mode (text in 0, affine in 1-2, a bitmap in
		// 3-5); BG3 exists as text in mode 0 and affine in mode 2.
		bool active0 = mode <= 1 && _enabledAtY[0] <= y;
		bool active1 = mode <= 1 && _enabledAtY[1] <= y;
		bool active2 = _enabledAtY[2] <= y;
		bool active3 = (mode == 0 || mode == 2) && _enabledAtY[3] <= y;

		if ( active0 ) RenderBgLine( 0, y );
		if ( active1 ) RenderBgLine( 1, y );
		if ( active2 )
		{
			if ( mode == 0 ) RenderBgLine( 2, y );            // tiled text
			else if ( mode <= 2 ) RenderAffineBgLine( 2, y ); // mode 1-2 affine (BG2X/Y reference + PA/PC per pixel)
			else RenderBitmapLine( mode, y );                 // mode 3-5 bitmap (BG2 only)
		}
		if ( active3 )
		{
			if ( mode == 0 ) RenderBgLine( 3, y );            // tiled text
			else RenderAffineBgLine( 3, y );                  // mode 2 affine
		}

		// OBJ sprites (gba_obj.shader). DISPCNT bit 12 enables the sprite layer. Rebuild the decoded sprite list from
		// live OAM when it changed since the last scanline, then render this line's sprites (with the cycle-budget
		// dropout) into the OBJ layer buffer. When OBJ is disabled RenderObjLine just clears the buffer so nothing
		// composites into the frame.
		bool objEnable = (DispCnt & 0x1000) != 0;
		if ( objEnable && _oamDirty )
			BuildSpriteList();
		RenderObjLine( y, objEnable );

		// Window mask (gba_window.shader): per-pixel 6 bits — 0-3 BG0-3, 4 OBJ, 5 colour-effect — deciding which
		// layers and blending are allowed at each x. Computed after OBJ so obj-window sprites (_objWindow) count.
		ComputeWindowMask( y );

		// --- Compositor + blend (gba_finalize.shader) ---
		int bldCnt = BldCnt;
		int bldAlpha = BldAlpha;
		int effect = (bldCnt >> 6) & 3;             // 0 none, 1 alpha, 2 brighten, 3 darken
		int eva = bldAlpha & 0x1F; if ( eva > 16 ) eva = 16;
		int evb = (bldAlpha >> 8) & 0x1F; if ( evb > 16 ) evb = 16;
		int evy = BldY & 0x1F; if ( evy > 16 ) evy = 16;

		int backdropYf = ((bldCnt >> 5) & 1) | (((bldCnt >> 13) & 1) << 1) | (effect << 2);
		int backdropRaw = Palette555( 0 );

		// Per-BG blend flags: bit0 = 1st-target (BLDCNT bit b), bit1 = 2nd-target (bit 8+b), bits2-3 = effect.
		int bg0Yf = ((bldCnt >> 0) & 1) | (((bldCnt >> 8) & 1) << 1) | (effect << 2);
		int bg1Yf = ((bldCnt >> 1) & 1) | (((bldCnt >> 9) & 1) << 1) | (effect << 2);
		int bg2Yf = ((bldCnt >> 2) & 1) | (((bldCnt >> 10) & 1) << 1) | (effect << 2);
		int bg3Yf = ((bldCnt >> 3) & 1) | (((bldCnt >> 11) & 1) << 1) | (effect << 2);

		int prio0 = BgCnt[0] & 3;
		int prio1 = BgCnt[1] & 3;
		int prio2 = BgCnt[2] & 3;
		int prio3 = BgCnt[3] & 3;

		// A BG contributes only if it was rendered above and its DISPCNT enable bit is set (bit 8+b) — 1:1 with the
		// finalize shader's per-layer dispCnt gate. Combined with active* to avoid reading a stale layer buffer.
		bool draw0 = active0 && (DispCnt & 0x0100) != 0;
		bool draw1 = active1 && (DispCnt & 0x0200) != 0;
		bool draw2 = active2 && (DispCnt & 0x0400) != 0;
		bool draw3 = active3 && (DispCnt & 0x0800) != 0;

		int w = GbaConstants.ScreenWidth;

		// OBJ 1st/2nd-target bits from BLDCNT (obj = bit 4 / bit 12); the 1st-target bit is OR'd with each sprite's
		// own semi-transparent flag at composite time (gba_finalize's objT1 = ((bldCnt>>4)&1) | semiTrans).
		int objT2 = (bldCnt >> 12) & 1;

		for ( int x = 0; x < w; x++ )
		{
			int topPrio = 32, topYf = backdropYf, topRaw = backdropRaw, topSemi = 0;
			int botPrio = 32, botYf = backdropYf, botRaw = backdropRaw, botSemi = 0;

			// OBJ is composited FIRST (gba_finalize), so at equal priority it stays in front of the BGs (Composite uses
			// >=, i.e. a later equal-priority layer never displaces the current top). Windows are forced to mask 0x3F
			// this stage, so the OBJ window-enable bit (mask bit 4) is always considered set.
			if ( _objOpaque[x] && (_winMask[x] & 0x10) != 0 )
			{
				int objSemi = _objSemi[x] ? 1 : 0;
				int objT1 = ((bldCnt >> 4) & 1) | objSemi;
				int objYf = objT1 | (objT2 << 1) | (effect << 2);
				Composite( _objPriority[x], objYf, _objColor[x], objSemi,
					ref topPrio, ref topYf, ref topRaw, ref topSemi, ref botPrio, ref botYf, ref botRaw, ref botSemi );
			}

			// Then BG0..BG3, so equal-priority ties keep the lower BG number in front (BGs never carry semi = 0).
			if ( draw0 && _bgOpaque[0 * w + x] && (_winMask[x] & 0x01) != 0 )
				Composite( prio0, bg0Yf, _bgColor[0 * w + x], 0, ref topPrio, ref topYf, ref topRaw, ref topSemi, ref botPrio, ref botYf, ref botRaw, ref botSemi );
			if ( draw1 && _bgOpaque[1 * w + x] && (_winMask[x] & 0x02) != 0 )
				Composite( prio1, bg1Yf, _bgColor[1 * w + x], 0, ref topPrio, ref topYf, ref topRaw, ref topSemi, ref botPrio, ref botYf, ref botRaw, ref botSemi );
			if ( draw2 && _bgOpaque[2 * w + x] && (_winMask[x] & 0x04) != 0 )
				Composite( prio2, bg2Yf, _bgColor[2 * w + x], 0, ref topPrio, ref topYf, ref topRaw, ref topSemi, ref botPrio, ref botYf, ref botRaw, ref botSemi );
			if ( draw3 && _bgOpaque[3 * w + x] && (_winMask[x] & 0x08) != 0 )
				Composite( prio3, bg3Yf, _bgColor[3 * w + x], 0, ref topPrio, ref topYf, ref topRaw, ref topSemi, ref botPrio, ref botYf, ref botRaw, ref botSemi );

			// Window colour-effect gate (mask bit 5): where clear, drop the top layer's 1st-target bit so no
				// alpha/brighten/darken applies (gba_finalize). A semi-transparent OBJ still alpha-blends via topSemi.
				if ( (_winMask[x] & 0x20) == 0 ) topYf &= ~1;

				// Blend in the GBA's native 5-bit-per-channel domain (matches hardware's integer >>4), then expand once.
				int tr = topRaw & 0x1F;
			int tg = (topRaw >> 5) & 0x1F;
			int tb = (topRaw >> 10) & 0x1F;

			// Window colour-effect bit is treated as always set this stage, so the top 1st-target bit is never cleared.
			// A semi-transparent OBJ on top (topSemi>0) forces an alpha-blend with a 2nd-target layer below regardless of
			// the BLDCNT effect, and suppresses brighten/darken (gba_finalize's topFlags.w path).
			int yf13 = topYf & 13;
			if ( (yf13 == 5 || topSemi > 0) && (botYf & 2) == 2 )
			{
				// Alpha: top is a 1st-target with alpha effect (or a semi-transparent OBJ) and bottom is a 2nd-target.
				int br = botRaw & 0x1F;
				int bg = (botRaw >> 5) & 0x1F;
				int bb = (botRaw >> 10) & 0x1F;
				tr = (tr * eva + br * evb) >> 4; if ( tr > 31 ) tr = 31;
				tg = (tg * eva + bg * evb) >> 4; if ( tg > 31 ) tg = 31;
				tb = (tb * eva + bb * evb) >> 4; if ( tb > 31 ) tb = 31;
			}
			else if ( topSemi == 0 )
			{
				if ( yf13 == 9 )
				{
					// Brighten toward white.
					tr += ((31 - tr) * evy) >> 4;
					tg += ((31 - tg) * evy) >> 4;
					tb += ((31 - tb) * evy) >> 4;
				}
				else if ( yf13 == 13 )
				{
					// Darken toward black.
					tr -= (tr * evy) >> 4;
					tg -= (tg * evy) >> 4;
					tb -= (tb * evy) >> 4;
				}
			}

			int p = rowBase + x * 4;
			fb[p] = (byte)((tr << 3) | (tr >> 2));
			fb[p + 1] = (byte)((tg << 3) | (tg >> 2));
			fb[p + 2] = (byte)((tb << 3) | (tb >> 2));
			fb[p + 3] = 255;
		}
	}

	// Keep the two frontmost layers by priority (gba_finalize.shader Composite). Lower priority number = more front;
	// on a tie the incoming layer never displaces the current top (>=), so composite order decides ties. Colour is
	// carried as raw BGR555; semi is the semi-transparent-OBJ flag (gba_finalize's flags.w), carried so it swaps
	// with its layer and reaches the blend stage as topSemi.
	private static void Composite(
		int newPrio, int newYf, int newRaw, int newSemi,
		ref int topPrio, ref int topYf, ref int topRaw, ref int topSemi,
		ref int botPrio, ref int botYf, ref int botRaw, ref int botSemi )
	{
		if ( newPrio >= topPrio )
		{
			if ( newPrio >= botPrio )
				return;
			botPrio = newPrio; botYf = newYf; botRaw = newRaw; botSemi = newSemi;
		}
		else
		{
			botPrio = topPrio; botYf = topYf; botRaw = topRaw; botSemi = topSemi;
			topPrio = newPrio; topYf = newYf; topRaw = newRaw; topSemi = newSemi;
		}
	}

	// Render one BG Mode 0 text scanline into _bgColor[b]/_bgOpaque[b] (ports gba_bg_mode0.shader's per-pixel logic).
	private void RenderBgLine( int b, int y )
	{
		int w = GbaConstants.ScreenWidth;
		int baseIdx = b * w;
		int cnt = BgCnt[b];

		uint charBase = (uint)((cnt >> 2) & 3) * 0x4000u;
		uint screenBase = (uint)((cnt >> 8) & 0x1F) * 0x800u;
		bool is8bpp = (cnt & 0x80) != 0;
		int size = (cnt >> 14) & 3;

		bool mosaic = (cnt & 0x40) != 0;
		int mosH = 1, mosV = 1;
		if ( mosaic )
		{
			mosH = (Mosaic & 0xF) + 1;
			mosV = ((Mosaic >> 4) & 0xF) + 1;
		}

		int offX = BgHOfs[b] & 0x1FF;
		int offY = BgVOfs[b] & 0x1FF;

		int wrapMaskX = 255, wrapMaskY = 255, doty = 0;
		if ( (size & 1) == 1 ) { wrapMaskX = 511; doty++; }
		if ( (size & 2) == 2 ) { wrapMaskY = 511; doty++; }

		byte[] vram = Gba.Memory.Vram;

		// Vertical mosaic + scroll are constant across the line.
		int cy = y;
		if ( mosaic && mosV > 1 ) cy = MosaicFloor( cy, mosV );

		for ( int x = 0; x < w; x++ )
		{
			int cx = x;
			if ( mosaic && mosH > 1 ) cx = MosaicFloor( cx, mosH );

			int coordX = cx + offX;
			int coordY = cy + offY;

			// Wrap to the map size and fold the 512-wide/tall screen-block selection into coordY
			// (gba_bg_mode0.shader lines 77-84).
			coordX &= wrapMaskX;
			coordY &= wrapMaskY;
			int wrapBitsX = coordX & 256;
			int wrapBitsY = coordY & 256;
			coordX &= 255;
			coordY &= 255;
			coordY += wrapBitsX + wrapBitsY * doty;

			uint mapAddr = screenBase + (uint)( ( coordX >> 3 ) + ( coordY >> 3 ) * 32 ) * 2u;
			int mapEntry = vram[mapAddr] | ( vram[mapAddr + 1] << 8 );

			int localX = coordX & 7;
			int localY = coordY & 7;
			if ( (mapEntry & 0x400) != 0 ) localX ^= 7;   // horizontal flip
			if ( (mapEntry & 0x800) != 0 ) localY ^= 7;   // vertical flip

			int tile = mapEntry & 0x3FF;
			int paletteId = (mapEntry >> 12) & 0xF;

			int palIdx;
			if ( is8bpp )
			{
				uint addr = charBase + (uint)tile * 64u + (uint)localY * 8u + (uint)localX;
				if ( addr >= 0x10000u ) { _bgOpaque[baseIdx + x] = false; continue; }
				palIdx = vram[addr];
				if ( palIdx == 0 ) { _bgOpaque[baseIdx + x] = false; continue; }
			}
			else
			{
				uint addr = charBase + (uint)tile * 32u + (uint)localY * 4u + ((uint)localX >> 1);
				if ( addr >= 0x10000u ) { _bgOpaque[baseIdx + x] = false; continue; }
				int data = vram[addr];
				int entry = (data >> (4 * (localX & 1))) & 0xF;
				if ( entry == 0 ) { _bgOpaque[baseIdx + x] = false; continue; }
				palIdx = paletteId * 16 + entry;
			}

			_bgColor[baseIdx + x] = Palette555( palIdx );
			_bgOpaque[baseIdx + x] = true;
		}
	}

	private static int MosaicFloor( int val, int grid )
	{
		return ( ( val * MosaicTable[grid] ) >> 12 ) * grid;
	}

	// Decode live OAM into _sprites (ports GbaVideo.Rendering.cs CleanOam). Walks all 128 OAM slots; skips prohibited
	// (obj-mode 3), disabled (non-affine + rot/scale bit) and bitmap-mode-reserved sprites; and computes each sprite's
	// size, tile addressing (char base, 1D/2D column + stride), affine matrix (PA-PD) or regular flip matrix, packed
	// flags and cycle cost. Called when _oamDirty and OBJ is enabled; clears _oamDirty so it runs once per OAM change.
	private void BuildSpriteList()
	{
		byte[] oam = Gba.Memory.Oam;
		bool mapping1D = (DispCnt & 0x40) != 0;
		int bgMode = DispCnt & 7;
		int count = 0;

		for ( int i = 0; i < 128; i++ )
		{
			int off = i * 8;
			int attr0 = oam[off] | (oam[off + 1] << 8);
			int attr1 = oam[off + 2] | (oam[off + 3] << 8);
			int attr2 = oam[off + 4] | (oam[off + 5] << 8);

			int objMode = (attr0 >> 10) & 3;
			if ( objMode == 3 ) continue;                                  // prohibited

			bool isAffine = (attr0 & 0x100) != 0;
			bool doubleSize = isAffine && (attr0 & 0x200) != 0;
			if ( !isAffine && (attr0 & 0x200) != 0 ) continue;             // regular + disable bit -> hidden

			int shape = (attr0 >> 14) & 3;
			int sizeParam = (attr1 >> 14) & 3;
			GetSpriteSize( shape, sizeParam, out int sw, out int sh );

			int sprY = attr0 & 0xFF;
			if ( sprY >= 160 ) sprY -= 256;
			int sprX = attr1 & 0x1FF;
			if ( sprX >= 240 ) sprX -= 512;

			int tileNum = attr2 & 0x3FF;
			bool is8bpp = (attr0 & 0x2000) != 0;

			if ( bgMode >= 3 && tileNum < 512 ) continue;                  // bitmap modes reserve OBJ tiles 0-511

			// Tile 0x10000-based char address, plus the 2D-mapping column split (charBase holds the row start, tile the
			// column offset that wraps within a row) or 1D stride. spr.CharBase*2 == the OBJ VRAM byte base at fetch.
			int align = is8bpp && !mapping1D ? 1 : 0;
			uint charBase = (uint)((0x10000 >> 1) + ((tileNum & ~align) * 0x10));
			uint tile = 0;
			if ( !mapping1D )
			{
				if ( is8bpp )
					tile = (charBase >> 5) & 0xFu;
				else
					tile = (charBase >> 4) & 0x1Fu;
				charBase &= ~0x1FFu;
			}
			uint stride = mapping1D ? (uint)(sw >> 3) : (uint)(0x20 >> (is8bpp ? 1 : 0));

			// Render cycle cost (drives the per-scanline dropout). Regular sprites clipped fully off the left edge are
			// dropped from the list entirely.
			int cycles;
			if ( isAffine )
			{
				int renderWCyc = doubleSize ? sw * 2 : sw;
				cycles = 8 + renderWCyc * 2;
				if ( sprX < 0 )
					cycles += sprX;
			}
			else
			{
				cycles = sw - 2;
				if ( sprX < 0 )
				{
					if ( sprX + sw < 0 ) continue;
					cycles += sprX >> 1;
				}
			}

			bool flipH = !isAffine && (attr1 & 0x1000) != 0;
			bool flipV = !isAffine && (attr1 & 0x2000) != 0;
			bool isMosaic = (attr0 & 0x1000) != 0;
			int priority = (attr2 >> 10) & 3;

			uint flags = 0;
			if ( is8bpp ) flags |= 1u;
			if ( flipH ) flags |= 2u;
			if ( flipV ) flags |= 4u;
			if ( isAffine ) flags |= 8u;
			if ( doubleSize ) flags |= 16u;
			if ( isMosaic ) flags |= 64u;
			if ( objMode == 1 ) flags |= 128u;                             // semi-transparent
			if ( objMode == 2 ) flags |= 256u;                             // obj-window
			flags |= (uint)(priority << 10);

			ObjSprite spr;
			spr.X = sprX;
			spr.Y = sprY;
			spr.Width = sw;
			spr.Height = sh;
			spr.RenderWidth = doubleSize ? sw * 2 : sw;
			spr.RenderHeight = doubleSize ? sh * 2 : sh;
			spr.CharBase = charBase;
			spr.Tile = tile;
			spr.Stride = stride;
			spr.Palette = (uint)((attr2 >> 12) & 0xF);
			spr.Flags = flags;
			spr.Cycles = cycles;

			if ( isAffine )
			{
				// The 2x2 affine matrix lives interleaved in OAM: group affIdx, params at +6/+14/+22/+30 (PA/PB/PC/PD).
				int affIdx = (attr1 >> 9) & 0x1F;
				int paOff = affIdx * 32 + 6;
				int pbOff = affIdx * 32 + 14;
				int pcOff = affIdx * 32 + 22;
				int pdOff = affIdx * 32 + 30;
				spr.PA = (short)(oam[paOff] | (oam[paOff + 1] << 8));
				spr.PB = (short)(oam[pbOff] | (oam[pbOff + 1] << 8));
				spr.PC = (short)(oam[pcOff] | (oam[pcOff + 1] << 8));
				spr.PD = (short)(oam[pdOff] | (oam[pdOff + 1] << 8));
			}
			else
			{
				spr.PA = flipH ? -256 : 256;
				spr.PB = 0;
				spr.PC = 0;
				spr.PD = flipV ? -256 : 256;
			}

			_sprites[count] = spr;
			count++;
		}

		_spriteCount = count;
		_oamDirty = false;
	}

	// Render scanline y's sprites into the OBJ layer buffer (ports gba_obj.shader). Iterates the decoded sprite list in
	// OAM order; charges each covering sprite's cycle cost and stops once the budget is spent (sprite dropout); applies
	// the affine (PA-PD) / regular flip transform, mosaic, edge wrap, 4/8bpp tile fetch and index-0 transparency; keeps
	// the lowest-priority-number opaque pixel (first sprite in OAM order wins ties). FLAG_OBJWINDOW sprites set the
	// pixel's obj-window flag instead of drawing a colour.
	private void RenderObjLine( int y, bool objEnable )
	{
		int w = GbaConstants.ScreenWidth;

		// Clear the OBJ layer for this scanline. Priority starts at 5 = gba_obj.shader's bestPriority sentinel.
		for ( int x = 0; x < w; x++ )
		{
			_objOpaque[x] = false;
			_objWindow[x] = false;
			_objColor[x] = 0;
			_objPriority[x] = 5;
			_objSemi[x] = false;
		}

		if ( !objEnable )
			return;

		byte[] vram = Gba.Memory.Vram;
		int cycleBudget = (DispCnt & 0x20) != 0 ? ObjHblankFreeLength : ObjLength;
		int cyclesUsed = 0;
		bool objWindowEnable = (DispCnt & 0x8000) != 0;
		int objMosH = ((Mosaic >> 8) & 0xF) + 1;
		int objMosV = ((Mosaic >> 12) & 0xF) + 1;

		for ( int i = 0; i < _spriteCount; i++ )
		{
			ObjSprite spr = _sprites[i];
			uint flags = spr.Flags;
			int priority = (int)((flags >> 10) & 3);
			int rw = spr.RenderWidth;
			int rh = spr.RenderHeight;

			// Vertical coverage (with the +256 edge wrap), constant across the line, computed once per sprite.
			int relY = y - spr.Y;
			float relYf = (y + 0.5f) - spr.Y;
			if ( relY < 0 && spr.Y > 160 - rh ) { relY += 256; relYf += 256.0f; }
			if ( relY < 0 || relY >= rh )
				continue;

			// Per-scanline cycle budget: covering sprites charge their cost in OAM order; once spent, later sprites drop.
			if ( cyclesUsed >= cycleBudget )
				break;
			cyclesUsed += spr.Cycles;

			bool is8bpp = (flags & 1u) != 0;
			bool isHFlip = (flags & 2u) != 0 && (flags & 8u) == 0;
			bool isMosaic = (flags & 64u) != 0;
			bool isSemi = (flags & 128u) != 0;
			bool isObjWindow = (flags & 256u) != 0;

			for ( int x = 0; x < w; x++ )
			{
				int relX = x - spr.X;
				float relXf = (x + 0.5f) - spr.X;
				if ( relX < 0 && spr.X > 240 - rw ) { relX += 512; relXf += 512.0f; }
				if ( relX < 0 || relX >= rw )
					continue;

				float inX = relXf;
				float inY = relYf;

				if ( isMosaic )
				{
					if ( objMosH > 1 )
					{
						if ( !isHFlip )
						{
							int mx = MosaicFloor( spr.X + relX, objMosH ) - spr.X;
							inX = GbaCompat.Clamp( mx, 0, rw - 1 );
						}
						else
						{
							int mx = rw - relX - 1;
							mx = rw - ( MosaicFloor( spr.X + mx, objMosH ) - spr.X ) - 1;
							inX = GbaCompat.Clamp( mx, 0, rw - 1 );
						}
					}
					if ( objMosV > 1 )
					{
						int my = MosaicFloor( spr.Y + relY, objMosV ) - spr.Y;
						inY = GbaCompat.Clamp( my, 0, rh - 1 );
					}
				}

				// Affine transform about the sprite centre (PA-PD are 8.8 fixed): map the render box into the WxH tile
				// grid. Regular sprites use PA/PD = +/-256 (H/V flip), PB = PC = 0.
				float fdx = inX - rw * 0.5f;
				float fdy = inY - rh * 0.5f;
				int localX = (int)( spr.PA / 256.0f * fdx + spr.PB / 256.0f * fdy + spr.Width * 0.5f );
				int localY = (int)( spr.PC / 256.0f * fdx + spr.PD / 256.0f * fdy + spr.Height * 0.5f );

				if ( (localX & ~(spr.Width - 1)) != 0 || (localY & ~(spr.Height - 1)) != 0 )
					continue;

				uint palIdx;
				if ( is8bpp )
				{
					uint tileIdx = (uint)((((localX >> 3) + (int)spr.Tile) & 15)) + (uint)(localY >> 3) * spr.Stride;
					uint addr = spr.CharBase * 2u + tileIdx * 64u + (uint)(localY & 7) * 8u + (uint)(localX & 7);
					if ( addr >= 0x18000u ) continue;
					palIdx = vram[addr];
					if ( palIdx == 0u ) continue;
				}
				else
				{
					uint tileIdx = (uint)((((localX >> 3) + (int)spr.Tile) & 31)) + (uint)(localY >> 3) * spr.Stride;
					uint addr = spr.CharBase * 2u + tileIdx * 32u + (uint)(localY & 7) * 4u + (uint)(localX & 7) / 2u;
					if ( addr >= 0x18000u ) continue;
					int data = vram[addr];
					uint entry = (uint)((data >> (4 * ((localX & 7) & 1))) & 0xF);
					if ( entry == 0u ) continue;
					palIdx = spr.Palette * 16u + entry;
				}

				if ( isObjWindow )
				{
					// FLAG_OBJWINDOW: mark the pixel's obj-window flag (windows sub-stage will consume it) instead of
					// drawing. Still charged cycles above, so obj-window sprites participate in the dropout budget.
					if ( objWindowEnable )
						_objWindow[x] = true;
					continue;
				}

				// Keep the lowest-priority-number opaque pixel; ties keep the first (lowest OAM index) sprite.
				if ( priority < _objPriority[x] || ( priority == _objPriority[x] && !_objOpaque[x] ) )
				{
					_objPriority[x] = priority;
					_objColor[x] = Palette555( 256 + (int)palIdx );
					_objOpaque[x] = true;
					_objSemi[x] = isSemi;
				}
			}
		}
	}

	// OBJ size table (attr0 shape x attr1 size). Ports GbaVideo.Rendering.cs GetSpriteSize.
	private static void GetSpriteSize( int shape, int size, out int w, out int h )
	{
		switch ( shape )
		{
			case 0:
				w = h = 8 << size;
				break;
			case 1:
				switch ( size )
				{
					case 0: w = 16; h = 8; break;
					case 1: w = 32; h = 8; break;
					case 2: w = 32; h = 16; break;
					default: w = 64; h = 32; break;
				}
				break;
			case 2:
				switch ( size )
				{
					case 0: w = 8; h = 16; break;
					case 1: w = 8; h = 32; break;
					case 2: w = 16; h = 32; break;
					default: w = 32; h = 64; break;
				}
				break;
			default:
				w = h = 8;
				break;
		}
	}

	// BG palette entry (0..255) -> raw 15-bit BGR555. Reads live palette RAM (2 bytes/entry, little-endian). Bit 15 is
	// masked off so the value is a clean 5-5-5 the compositor can blend and then expand via (v<<3)|(v>>2).
	private int Palette555( int entry )
	{
		byte[] pal = Gba.Memory.PaletteRam;
		int o = entry * 2;
		return ( pal[o] | ( pal[o + 1] << 8 ) ) & 0x7FFF;
	}

	// Build this scanline's window mask (gba_window.shader). When no window is enabled every pixel is 0x3F (all layers
	// + colour-effect allowed). Otherwise each pixel takes its 6-bit enable set from win0 (top priority), then win1,
	// then the obj-window, then the outside region — win0 > win1 > objwin > out, exactly like the shader.
	private void ComputeWindowMask( int y )
	{
		int w = GbaConstants.ScreenWidth;
		bool win0En = (DispCnt & 0x2000) != 0;
		bool win1En = (DispCnt & 0x4000) != 0;
		bool objWinEn = (DispCnt & 0x8000) != 0;

		if ( !win0En && !win1En && !objWinEn )
		{
			for ( int x = 0; x < w; x++ ) _winMask[x] = 0x3F;
			return;
		}

		int in0 = WinIn & 0x3F;
		int in1 = (WinIn >> 8) & 0x3F;
		int outMask = WinOut & 0x3F;
		int objMask = (WinOut >> 8) & 0x3F;

		// Window ranges: high byte = start (x1/y1), low byte = end (x2/y2) — a [start,end) span that wraps when
		// start > end (CleanWindowRange has already clamped/normalised these).
		int w0x1 = _win0H >> 8, w0x2 = _win0H & 0xFF, w0y1 = _win0V >> 8, w0y2 = _win0V & 0xFF;
		int w1x1 = _win1H >> 8, w1x2 = _win1H & 0xFF, w1y1 = _win1V >> 8, w1y2 = _win1V & 0xFF;

		bool row0 = win0En && RangeContains( y, w0y1, w0y2 );
		bool row1 = win1En && RangeContains( y, w1y1, w1y2 );

		for ( int x = 0; x < w; x++ )
		{
			if ( row0 && RangeContains( x, w0x1, w0x2 ) ) _winMask[x] = (byte)in0;
			else if ( row1 && RangeContains( x, w1x1, w1x2 ) ) _winMask[x] = (byte)in1;
			else if ( objWinEn && _objWindow[x] ) _winMask[x] = (byte)objMask;
			else _winMask[x] = (byte)outMask;
		}
	}

	// A [lo,hi) span with wraparound when lo > hi (the window wraps past the screen edge) — WindowCrop's per-axis test.
	private static bool RangeContains( int v, int lo, int hi )
	{
		return lo <= hi ? (v >= lo && v < hi) : (v >= lo || v < hi);
	}

	// Render one affine BG scanline (mode 1 BG2, mode 2 BG2/BG3) into _bgColor[b]/_bgOpaque[b] (ports gba_bg_mode2).
	// BgX/BgY (8 fractional bits) is the per-scanline reference point — already stepped by PB/PD each line in
	// StartHBlank — and PA/PC step it per pixel. 8bpp only: one byte per map tile, one byte per texel. Out-of-bounds is
	// transparent unless the BGCNT overflow bit (bit 13) wraps the coordinates.
	private void RenderAffineBgLine( int b, int y )
	{
		int w = GbaConstants.ScreenWidth;
		int baseIdx = b * w;
		int i = b - 2;                 // BG2 -> 0, BG3 -> 1 in BgX/BgY/PA..PD
		int cnt = BgCnt[b];

		uint charBase = (uint)((cnt >> 2) & 3) * 0x4000u;
		uint screenBase = (uint)((cnt >> 8) & 0x1F) * 0x800u;
		int sizeBits = (cnt >> 14) & 3;
		int size = 128 << sizeBits;    // 128 / 256 / 512 / 1024 px square
		int mapSize = size >> 3;       // tiles per row
		int wrapMask = size - 1;
		bool wrap = (cnt & 0x2000) != 0;

		int refX = BgX[i], refY = BgY[i];
		int pa = BgPA[i], pc = BgPC[i];
		byte[] vram = Gba.Memory.Vram;

		for ( int x = 0; x < w; x++ )
		{
			int px = (refX + x * pa) >> 8;
			int py = (refY + x * pc) >> 8;

			if ( wrap ) { px &= wrapMask; py &= wrapMask; }
			else if ( (uint)px >= (uint)size || (uint)py >= (uint)size ) { _bgOpaque[baseIdx + x] = false; continue; }

			uint mapAddr = screenBase + (uint)((py >> 3) * mapSize + (px >> 3));
			if ( mapAddr >= 0x10000u ) { _bgOpaque[baseIdx + x] = false; continue; }
			int tile = vram[mapAddr];

			uint addr = charBase + (uint)tile * 64u + (uint)(py & 7) * 8u + (uint)(px & 7);
			if ( addr >= 0x10000u ) { _bgOpaque[baseIdx + x] = false; continue; }
			int palIdx = vram[addr];
			if ( palIdx == 0 ) { _bgOpaque[baseIdx + x] = false; continue; }

			_bgColor[baseIdx + x] = Palette555( palIdx );
			_bgOpaque[baseIdx + x] = true;
		}
	}

	// Render the BG2 bitmap scanline for modes 3/4/5 into _bgColor[2]/_bgOpaque[2] (ports gba_bg_mode3/4/5). BG2's
	// affine transform (BgX/BgY reference + PA/PC per pixel) samples the frame buffer: mode 3 = 240x160 direct BGR555,
	// mode 4 = 240x160 8bpp paletted with a 0xA000 page flip (DISPCNT bit 4), mode 5 = 160x128 direct BGR555, flipped.
	private void RenderBitmapLine( int mode, int y )
	{
		int w = GbaConstants.ScreenWidth;
		int baseIdx = 2 * w;
		int refX = BgX[0], refY = BgY[0];
		int pa = BgPA[0], pc = BgPC[0];
		byte[] vram = Gba.Memory.Vram;
		int page = (mode != 3 && (DispCnt & 0x10) != 0) ? 0xA000 : 0;
		int bw = mode == 5 ? 160 : 240;
		int bh = mode == 5 ? 128 : 160;

		for ( int x = 0; x < w; x++ )
		{
			int px = (refX + x * pa) >> 8;
			int py = (refY + x * pc) >> 8;

			if ( (uint)px >= (uint)bw || (uint)py >= (uint)bh ) { _bgOpaque[baseIdx + x] = false; continue; }

			if ( mode == 4 )
			{
				int palIdx = vram[page + py * bw + px];
				if ( palIdx == 0 ) { _bgOpaque[baseIdx + x] = false; continue; }
				_bgColor[baseIdx + x] = Palette555( palIdx );
			}
			else
			{
				int o = page + (py * bw + px) * 2;
				_bgColor[baseIdx + x] = (vram[o] | (vram[o + 1] << 8)) & 0x7FFF;
			}
			_bgOpaque[baseIdx + x] = true;
		}
	}

	// Real PPU captured this scanline's register/palette/OAM snapshot here; we render live in CaptureScanline instead.
	private void SnapshotVram() { }

	// Called at VBlank (GbaVideo.StartHDraw). All 160 scanlines have already been rendered into _framebuffer by
	// CaptureScanline, so this just advances the committed-frame counter.
	private void CommitFrame()
	{
		_frameCount++;
	}
}
