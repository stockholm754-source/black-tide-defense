using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // 精灵数字：一张图集里九套「0123456789+-!×」（Resources/UI/dmg_font + dmg_font_map），按字符拼出一行居中的字。
    // 顶点色会乘到贴图上：普通那套是白字，可以染成任意颜色；其余几套自带颜色，给白色就行
    public class SpriteNumber : MaskableGraphic
    {
        const string Chars = "0123456789+-!×";
        const float Advance = 0.84f;   // 相邻字符的格子略微重叠（字是斜的，格子里还留了描边的余量）
        static Texture2D atlas; static Rect[,] uv; static float[,] aspect; static bool loaded;

        string text = ""; int set; float height = 40f;
        public float Width { get; private set; }
        public override Texture mainTexture => atlas ? (Texture)atlas : s_WhiteTexture;

        protected override void Awake() { base.Awake(); Load(); }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            atlas = Resources.Load<Texture2D>("UI/dmg_font");
            var map = Resources.Load<TextAsset>("UI/dmg_font_map");
            if (!atlas || !map) { atlas = null; return; }
            var lines = map.text.Split('\n');
            var size = lines[0].Trim().Split(' ');
            float w = float.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture), h = float.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture);
            uv = new Rect[9, Chars.Length]; aspect = new float[9, Chars.Length];
            for (int i = 1; i < lines.Length; i++)
            {
                var p = lines[i].Trim().Split(' ');
                if (p.Length < 6) continue;
                int s = int.Parse(p[0]), c = int.Parse(p[1]);
                float x0 = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), x1 = float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture);
                float y0 = float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture), y1 = float.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture);
                uv[s, c] = Rect.MinMaxRect(x0 / w, 1f - y1 / h, x1 / w, 1f - y0 / h);   // 表里的 y 从图片顶部往下数
                aspect[s, c] = (x1 - x0) / (y1 - y0);
            }
        }

        public void Set(string s, int fontSet, float h)
        {
            Load();
            if (s == text && set == fontSet && Mathf.Approximately(h, height)) return;
            text = s; set = Mathf.Clamp(fontSet, 0, 8); height = h;
            float w = 0f;
            if (atlas) foreach (char ch in text) { int i = Chars.IndexOf(ch); w += i < 0 ? height * 0.3f : height * aspect[set, i] * Advance; }
            Width = w;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!atlas) return;
            float x = -Width * 0.5f, y0 = -height * 0.5f, y1 = height * 0.5f;
            Color32 c = color;
            foreach (char ch in text)
            {
                int i = Chars.IndexOf(ch);
                if (i < 0) { x += height * 0.3f; continue; }
                float cw = height * aspect[set, i];
                float xo = x - cw * (1f - Advance) * 0.5f;
                Rect r = uv[set, i];
                int k = vh.currentVertCount;
                vh.AddVert(new Vector3(xo, y0), c, new Vector2(r.xMin, r.yMin));
                vh.AddVert(new Vector3(xo, y1), c, new Vector2(r.xMin, r.yMax));
                vh.AddVert(new Vector3(xo + cw, y1), c, new Vector2(r.xMax, r.yMax));
                vh.AddVert(new Vector3(xo + cw, y0), c, new Vector2(r.xMax, r.yMin));
                vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k, k + 2, k + 3);
                x += cw * Advance;
            }
        }
    }
}
