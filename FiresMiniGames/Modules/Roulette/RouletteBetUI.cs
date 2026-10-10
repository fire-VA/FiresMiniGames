using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.Roulette
{
    /// <summary>
    /// The betting board: the full European layout (zero, 12x3 number grid, dozens, the even-money row) plus a
    /// chip-size stepper. Left-click stacks the current chip on a spot, right-click clears the spot. Spin takes
    /// the whole stake from the purse up front and hands the board to RouletteGame.
    /// </summary>
    public sealed class RouletteBetUI : MonoBehaviour
    {
        private static readonly Color RedCell = new Color(0.55f, 0.10f, 0.09f);
        private static readonly Color BlackCell = new Color(0.10f, 0.10f, 0.12f);
        private static readonly Color GreenCell = new Color(0.07f, 0.36f, 0.16f);
        private static readonly Color OutsideCell = new Color(0.16f, 0.12f, 0.08f);

        private static GameObject _canvasGo;

        private RouletteGame _table;
        private readonly Dictionary<string, int> _stakes = new Dictionary<string, int>();
        private readonly Dictionary<string, TextMeshProUGUI> _badges = new Dictionary<string, TextMeshProUGUI>();
        private int[] _chips;
        private int _chipIdx;
        private TextMeshProUGUI _chipLabel, _purseLabel, _stakedLabel;
        private bool _blocked;

        public static void Show(RouletteGame table)
        {
            Close();
            _canvasGo = new GameObject("FMG_RouletteBoard");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6500;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _canvasGo.AddComponent<RouletteBetUI>().Build(table);
        }

        public static void Close()
        {
            if (_canvasGo != null)
            {
                var ui = _canvasGo.GetComponent<RouletteBetUI>();
                if (ui != null && ui._blocked) { InputBlock.Block(false); ui._blocked = false; }
                UnityEngine.Object.Destroy(_canvasGo);
                _canvasGo = null;
            }
        }

        private void Build(RouletteGame table)
        {
            _table = table;
            _chips = MiniGamesConfig.ParseSizes(MiniGamesConfig.RouletteChips != null ? MiniGamesConfig.RouletteChips.Value : null, new[] { 1, 5, 10, 25, 100 });
            _chipIdx = Mathf.Min(1, _chips.Length - 1);

            var dim = MiniGamePopup.NewImage(transform, "Dim", new Color(0f, 0f, 0f, 0.6f));
            MiniGamePopup.Stretch(dim.rectTransform);

            var panel = MiniGamePopup.NewImage(transform, "Panel", new Color(0.09f, 0.07f, 0.05f, 0.97f));
            MiniGamePopup.Place(panel.rectTransform, new Vector2(1010f, 640f), Vector2.zero);

            var title = MiniGamePopup.NewText(panel.transform, "Title", "Roulette", 30f, new Color(0.95f, 0.86f, 0.6f));
            MiniGamePopup.Place(title.rectTransform, new Vector2(400f, 40f), new Vector2(0f, 286f));
            var sub = MiniGamePopup.NewText(panel.transform, "Sub", "Left-click stakes a chip — right-click clears the spot.", 14f, new Color(0.75f, 0.72f, 0.65f));
            MiniGamePopup.Place(sub.rectTransform, new Vector2(700f, 22f), new Vector2(0f, 254f));

            // board: zero + 12x3 grid; cell 56x70, spacing 58/74
            float gx0 = -352f, gy0 = 148f;
            Cell(panel.transform, "s0", "0", GreenCell, new Vector2(52f, 3f * 74f - 4f), new Vector2(gx0 - 58f, gy0 - 74f));
            for (int c = 0; c < 12; c++)
                for (int r = 0; r < 3; r++)
                {
                    int n = c * 3 + (3 - r);
                    Cell(panel.transform, "s" + n, n.ToString(), RouletteRed(n) ? RedCell : BlackCell,
                        new Vector2(56f, 70f), new Vector2(gx0 + c * 58f, gy0 - r * 74f));
                }

            string[] dzKeys = { "dz1", "dz2", "dz3" };
            string[] dzLabels = { "1st 12", "2nd 12", "3rd 12" };
            for (int i = 0; i < 3; i++)
                Cell(panel.transform, dzKeys[i], dzLabels[i], OutsideCell, new Vector2(228f, 44f), new Vector2(gx0 + 58f + i * 232f - 58f, -102f));

            (string key, string label)[] outs = { ("low", "1 - 18"), ("even", "EVEN"), ("red", "RED"), ("black", "BLACK"), ("odd", "ODD"), ("high", "19 - 36") };
            for (int i = 0; i < outs.Length; i++)
            {
                var color = outs[i].key == "red" ? RedCell : outs[i].key == "black" ? BlackCell : OutsideCell;
                Cell(panel.transform, outs[i].key, outs[i].label, color, new Vector2(112f, 44f), new Vector2(gx0 - 58f + 56f + i * 116f, -152f));
            }

            // right rail: chip stepper, purse, stake, actions
            float rx = 400f;
            var chipMinus = ActionButton(panel.transform, "-", new Vector2(44f, 44f), new Vector2(rx - 64f, 170f), StepChipDown);
            _chipLabel = MiniGamePopup.NewText(panel.transform, "Chip", "", 24f, new Color(0.95f, 0.86f, 0.6f));
            MiniGamePopup.Place(_chipLabel.rectTransform, new Vector2(80f, 40f), new Vector2(rx, 170f));
            ActionButton(panel.transform, "+", new Vector2(44f, 44f), new Vector2(rx + 64f, 170f), StepChipUp);
            var chipCap = MiniGamePopup.NewText(panel.transform, "ChipCap", "chip", 13f, new Color(0.7f, 0.67f, 0.6f));
            MiniGamePopup.Place(chipCap.rectTransform, new Vector2(120f, 20f), new Vector2(rx, 140f));

            _purseLabel = MiniGamePopup.NewText(panel.transform, "Purse", "", 16f, new Color(0.92f, 0.8f, 0.45f));
            MiniGamePopup.Place(_purseLabel.rectTransform, new Vector2(220f, 24f), new Vector2(rx, 92f));
            _stakedLabel = MiniGamePopup.NewText(panel.transform, "Staked", "", 16f, new Color(0.85f, 0.82f, 0.75f));
            MiniGamePopup.Place(_stakedLabel.rectTransform, new Vector2(220f, 24f), new Vector2(rx, 64f));

            ActionButton(panel.transform, "Clear", new Vector2(180f, 46f), new Vector2(rx, 6f), ClearAll);
            var spin = ActionButton(panel.transform, "SPIN", new Vector2(180f, 62f), new Vector2(rx, -66f), Spin);
            spin.GetComponent<Image>().color = new Color(0.45f, 0.32f, 0.12f);

            var hint = MiniGamePopup.NewText(panel.transform, "Hint", "Straight pays 36x - red/black/odd/even 2x - dozens 3x\n[Esc] close", 13f, new Color(0.6f, 0.6f, 0.6f));
            MiniGamePopup.Place(hint.rectTransform, new Vector2(700f, 40f), new Vector2(0f, -288f));

            MiniGamePopup.ApplyFont(gameObject);
            if (!InputBlock.IsBlocked) { InputBlock.Block(true); _blocked = true; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }

        internal static bool RouletteRed(int n)
        {
            switch (n)
            {
                case 1: case 3: case 5: case 7: case 9: case 12: case 14: case 16: case 18:
                case 19: case 21: case 23: case 25: case 27: case 30: case 32: case 34: case 36:
                    return true;
                default: return false;
            }
        }

        private void Cell(Transform parent, string key, string label, Color color, Vector2 size, Vector2 pos)
        {
            var img = MiniGamePopup.NewImage(parent, "Cell_" + key, color);
            MiniGamePopup.Place(img.rectTransform, size, pos);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.2f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.7f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(() => AddStake(key));
            var catcher = img.gameObject.AddComponent<RightClick>();
            catcher.Action = () => RemoveStake(key);

            var text = MiniGamePopup.NewText(img.transform, "Label", label, size.y > 50f ? 19f : 15f, new Color(0.95f, 0.93f, 0.88f));
            MiniGamePopup.Place(text.rectTransform, new Vector2(size.x, 24f), new Vector2(0f, size.y > 50f ? 8f : 0f));

            var badge = MiniGamePopup.NewText(img.transform, "Badge", "", 13f, new Color(1f, 0.85f, 0.3f));
            badge.fontStyle = FontStyles.Bold;
            MiniGamePopup.Place(badge.rectTransform, new Vector2(size.x, 18f), new Vector2(0f, -size.y * 0.5f + 12f));
            _badges[key] = badge;
        }

        private GameObject ActionButton(Transform parent, string label, Vector2 size, Vector2 pos, Action action)
        {
            var img = MiniGamePopup.NewImage(parent, "Btn_" + label, new Color(0.22f, 0.16f, 0.10f));
            MiniGamePopup.Place(img.rectTransform, size, pos);
            var btn = img.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => action());
            var text = MiniGamePopup.NewText(img.transform, "Label", label, 19f, new Color(0.96f, 0.92f, 0.82f));
            MiniGamePopup.Place(text.rectTransform, new Vector2(size.x, 26f), Vector2.zero);
            return img.gameObject;
        }

        private sealed class RightClick : MonoBehaviour, IPointerClickHandler
        {
            public Action Action;
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Right) Action?.Invoke();
            }
        }

        private int Total() => _stakes.Values.Sum();

        private void AddStake(string key)
        {
            int chip = _chips[_chipIdx];
            int max = MiniGamesConfig.RouletteMaxBet != null ? MiniGamesConfig.RouletteMaxBet.Value : 500;
            var player = Player.m_localPlayer;
            if (Total() + chip > max)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Table limit is {max}.");
                return;
            }
            if (player != null && Total() + chip > CoinWallet.Count(player))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough coins.");
                return;
            }
            _stakes.TryGetValue(key, out int cur);
            _stakes[key] = cur + chip;
            Refresh();
        }

        private void RemoveStake(string key)
        {
            if (_stakes.Remove(key)) Refresh();
        }

        private void ClearAll()
        {
            _stakes.Clear();
            Refresh();
        }

        private void StepChipUp() { _chipIdx = Mathf.Min(_chipIdx + 1, _chips.Length - 1); Refresh(); }
        private void StepChipDown() { _chipIdx = Mathf.Max(_chipIdx - 1, 0); Refresh(); }

        private void Refresh()
        {
            _chipLabel.text = _chips[_chipIdx].ToString();
            var player = Player.m_localPlayer;
            _purseLabel.text = $"Purse: {(player != null ? CoinWallet.Count(player) : 0)}";
            _stakedLabel.text = $"Staked: {Total()}";
            foreach (var kv in _badges)
                kv.Value.text = _stakes.TryGetValue(kv.Key, out int amt) ? amt.ToString() : "";
        }

        private void Spin()
        {
            var player = Player.m_localPlayer;
            int total = Total();
            if (total <= 0)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Place a bet first.");
                return;
            }
            if (player == null || !CoinWallet.TrySpend(player, total))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough coins.");
                return;
            }
            var bets = new Dictionary<string, int>(_stakes);
            var table = _table;
            Close();
            table.StartSpin(bets);
        }

        private void Update()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void OnDestroy()
        {
            if (_blocked) { InputBlock.Block(false); _blocked = false; }
        }
    }
}
