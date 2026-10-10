using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;

namespace FiresMiniGames.Modules.Common
{
    /// <summary>
    /// Shared themed mode-select modal for every machine (air hockey ends, slot wagers, arcade coin-in).
    /// Standalone ScreenSpaceOverlay canvas in the LockpickPanel style — InputBlock while open so nothing leaks
    /// to the player. Optional footer line for a live purse readout.
    /// </summary>
    public static class MiniGamePopup
    {
        private static GameObject _canvasGo;
        private static bool _blocked;

        public static bool IsOpen => _canvasGo != null;

        public static void Show(string title, string subtitle, params (string label, string desc, Action action)[] options)
            => Show(title, subtitle, null, options);

        public static void Show(string title, string subtitle, string footer, params (string label, string desc, Action action)[] options)
        {
            Close();
            _canvasGo = new GameObject("FMG_MiniGamePopup");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6500;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            var dim = NewImage(_canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.rectTransform);

            bool hasFooter = !string.IsNullOrEmpty(footer);
            const float rowH = 74f;
            const float maxH = 620f;                                   // cap: a longer list scrolls instead of running off-screen
            float baseH = 150f + (hasFooter ? 26f : 0f);
            float fullH = baseH + options.Length * rowH;
            bool scroll = fullH > maxH;
            float h = scroll ? maxH : fullH;
            var panel = NewImage(_canvasGo.transform, "Panel", new Color(0.09f, 0.07f, 0.05f, 0.96f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(460f, h);

            var titleT = NewText(panel.transform, "Title", title, 28f, new Color(0.95f, 0.86f, 0.6f));
            Place(titleT.rectTransform, new Vector2(420f, 40f), new Vector2(0f, h * 0.5f - 34f));
            var subT = NewText(panel.transform, "Sub", subtitle, 15f, new Color(0.8f, 0.78f, 0.72f));
            Place(subT.rectTransform, new Vector2(420f, 26f), new Vector2(0f, h * 0.5f - 68f));

            // One option button; the label + desc are centred inside it either way, so the direct and scrolled paths
            // share this. top=true anchors it to the top of the scroll content; top=false is the classic centred place.
            void AddOption((string label, string desc, Action action) opt, Transform parent, bool top, Vector2 pos)
            {
                var captured = opt;
                var btn = NewImage(parent, "Btn", new Color(0.22f, 0.16f, 0.10f, 1f));
                if (top)
                {
                    var rt = btn.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                    rt.sizeDelta = new Vector2(400f, 62f); rt.anchoredPosition = pos;
                }
                else Place(btn.rectTransform, new Vector2(400f, 62f), pos);
                var b = btn.gameObject.AddComponent<Button>();
                var colors = b.colors;
                colors.highlightedColor = new Color(1.25f, 1.2f, 1.05f, 1f);
                colors.pressedColor = new Color(0.8f, 0.75f, 0.65f, 1f);
                b.colors = colors;
                b.onClick.AddListener(() => { Close(); captured.action?.Invoke(); });
                var lab = NewText(btn.transform, "Label", captured.label, 20f, new Color(0.96f, 0.92f, 0.82f));
                Place(lab.rectTransform, new Vector2(390f, 28f), new Vector2(0f, 12f));
                var d = NewText(btn.transform, "Desc", captured.desc, 13f, new Color(0.75f, 0.72f, 0.65f));
                Place(d.rectTransform, new Vector2(390f, 22f), new Vector2(0f, -14f));
            }

            if (!scroll)
            {
                float y = h * 0.5f - 110f;
                foreach (var opt in options) { AddOption(opt, panel.transform, false, new Vector2(0f, y)); y -= rowH; }
            }
            else
            {
                // A masked, mouse-wheel-scrollable list filling the band between the subtitle and the footer/hint.
                float bandTop = h * 0.5f - 96f;
                float bandBottom = -h * 0.5f + (hasFooter ? 60f : 36f);
                float bandH = bandTop - bandBottom;
                var viewport = NewImage(panel.transform, "Viewport", new Color(0f, 0f, 0f, 0f));
                Place(viewport.rectTransform, new Vector2(440f, bandH), new Vector2(0f, (bandTop + bandBottom) * 0.5f));
                viewport.gameObject.AddComponent<RectMask2D>();
                var sr = viewport.gameObject.AddComponent<ScrollRect>();
                sr.horizontal = false; sr.vertical = true;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.scrollSensitivity = 34f;
                sr.viewport = viewport.rectTransform;

                var contentGo = new GameObject("Content", typeof(RectTransform));
                contentGo.transform.SetParent(viewport.transform, false);
                var crt = contentGo.GetComponent<RectTransform>();
                crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 1f); crt.pivot = new Vector2(0.5f, 1f);
                crt.sizeDelta = new Vector2(440f, options.Length * rowH + 6f);
                crt.anchoredPosition = Vector2.zero;
                sr.content = crt;

                float ty = -6f;
                foreach (var opt in options) { AddOption(opt, contentGo.transform, true, new Vector2(0f, ty)); ty -= rowH; }
                sr.verticalNormalizedPosition = 1f;   // start at the top of the list
            }

            if (hasFooter)
            {
                var foot = NewText(panel.transform, "Footer", footer, 15f, new Color(0.92f, 0.8f, 0.45f));
                Place(foot.rectTransform, new Vector2(420f, 24f), new Vector2(0f, -h * 0.5f + 44f));
            }

            var hint = NewText(panel.transform, "Hint", "[Esc] close", 13f, new Color(0.6f, 0.6f, 0.6f));
            Place(hint.rectTransform, new Vector2(420f, 22f), new Vector2(0f, -h * 0.5f + 18f));

            ApplyFont(_canvasGo);

            if (!InputBlock.IsBlocked) { InputBlock.Block(true); _blocked = true; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _canvasGo.AddComponent<PopupEscape>();
        }

        public static void Close()
        {
            if (_blocked) { InputBlock.Block(false); _blocked = false; }
            if (_canvasGo != null) { UnityEngine.Object.Destroy(_canvasGo); _canvasGo = null; }
        }

        internal static void ApplyFont(GameObject root)
        {
            var font = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
            if (font != null) foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) t.font = font;
        }

        private sealed class PopupEscape : MonoBehaviour
        {
            private void Update()
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (Input.GetKeyDown(KeyCode.Escape)) Close();
            }
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        internal static void Place(RectTransform rt, Vector2 size, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
        }

        internal static Image NewImage(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = c; return img;
        }

        internal static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false; t.enableWordWrapping = false;
            return t;
        }
    }
}
