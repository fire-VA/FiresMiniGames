using UnityEngine;

namespace FiresMiniGames.Modules.AirHockey
{
    /// <summary>
    /// Drives the table's two 7-segment digit quads from the score in the table's ZDO, so every nearby player
    /// (and late joiners) see the live score. Digits are material-property-block UV shifts into the ah_digits
    /// atlas — no per-instance materials.
    /// </summary>
    public sealed class AirHockeyScoreboard : MonoBehaviour
    {
        public const string KeyScoreA = "ah_score_a";
        public const string KeyScoreB = "ah_score_b";

        private ZNetView _nview;
        private Renderer _digitA, _digitB;
        private int _shownA = -1, _shownB = -1;
        private float _poll;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _digitA = transform.Find("rig/Digits/DigitA")?.GetComponent<Renderer>();
            _digitB = transform.Find("rig/Digits/DigitB")?.GetComponent<Renderer>();
            Show(0, 0);
        }

        private void Update()
        {
            _poll += Time.deltaTime;
            if (_poll < 0.25f) return;
            _poll = 0f;
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            Show(zdo.GetInt(KeyScoreA), zdo.GetInt(KeyScoreB));
        }

        private void Show(int a, int b)
        {
            if (a == _shownA && b == _shownB) return;
            _shownA = a; _shownB = b;
            SetDigit(_digitA, a);
            SetDigit(_digitB, b);
        }

        private static void SetDigit(Renderer r, int value)
        {
            if (r == null) return;
            int d = Mathf.Clamp(value, 0, 9);
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetVector("_MainTex_ST", new Vector4(0.1f, 1f, d * 0.1f, 0f));
            r.SetPropertyBlock(mpb);
        }
    }
}
