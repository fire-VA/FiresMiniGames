using UnityEngine;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// Paints a cartridge's per-game sticker. A cartridge's identity is its origin box (customData <c>fmg_box</c>);
    /// the sticker art is that box's cover texture, so a Zelda cart shows Zelda art even when it's sitting in the
    /// wrong box. Works on a "sticker" child renderer (a quad the Unity builder bakes onto the cart shell over the
    /// blank label recess) — until that quad exists this is a harmless no-op, so the grey cart just shows its own
    /// baked label. Applied from the world ItemDrop's own customData (a dropped cart) and driven directly by the box
    /// when it displays a held cartridge.
    /// </summary>
    public sealed class CartridgeSkin : MonoBehaviour
    {
        private void Awake()
        {
            var drop = GetComponent<ItemDrop>();
            var data = drop != null ? drop.m_itemData : null;
            if (data != null) Apply(Cartridge.Get(data, Cartridge.KeyBox));
        }

        /// <summary>Texture this cartridge's sticker quad from the given box's cover art. Safe with a missing quad,
        /// a null/blank box, or a coverless box (each path just leaves the shell's own art showing).</summary>
        public void Apply(string boxPrefab)
        {
            var sticker = FindSticker();
            if (sticker == null) return;
            var cover = string.IsNullOrEmpty(boxPrefab) ? null : BoxCover(boxPrefab);
            if (cover == null) { sticker.enabled = false; return; }
            var mat = sticker.material;   // per-instance so carts don't share one texture
            mat.mainTexture = cover;
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", cover);
            sticker.enabled = true;
        }

        private Renderer FindSticker()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name.IndexOf("sticker", System.StringComparison.OrdinalIgnoreCase) >= 0) return r;
                var m = r.sharedMaterial;
                if (m != null && m.name.IndexOf("sticker", System.StringComparison.OrdinalIgnoreCase) >= 0) return r;
            }
            return null;
        }

        // The cover art of a box piece = the largest texture on its renderers (the cover face dwarfs the spine/sides).
        private static Texture BoxCover(string boxPrefab)
        {
            GameObject box = null;
            foreach (var p in FiresMiniGames.piecePrefabs)
                if (p != null && p.name == boxPrefab) { box = p; break; }
            if (box == null) return null;

            Texture best = null;
            long bestArea = 0;
            foreach (var r in box.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    var t = m != null ? m.mainTexture : null;
                    if (t == null) continue;
                    long area = (long)t.width * t.height;
                    if (area > bestArea) { bestArea = area; best = t; }
                }
            return best;
        }
    }
}
