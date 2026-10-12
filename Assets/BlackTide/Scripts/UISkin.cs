using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // 原始 PNG 的 Sprite rect 去掉透明留白；缓存贴图与 Sprite，不改图片像素。
    public static class UISkin
    {
        struct Entry { public string file; public Rect rect; public Vector4 border; }
        static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        static readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();
        static bool loaded;

        public static Sprite Get(int id)
        {
            if (sprites.TryGetValue(id, out var found)) return found;
            if (!loaded)
            {
                loaded = true;
                var map = Resources.Load<TextAsset>("UI/Batch8/sprite_rects");
                if (map) foreach (var line in map.text.Split('\n'))
                {
                    var p = line.Trim().Split('|');
                    if (p.Length != 9) continue;
                    var n = new float[8];
                    for (int i = 0; i < n.Length; i++) n[i] = float.Parse(p[i + 1], CultureInfo.InvariantCulture);
                    entries[int.Parse(p[0].Substring(1, 2), CultureInfo.InvariantCulture)] = new Entry
                    {
                        file = p[0], rect = new Rect(n[0], n[1], n[2], n[3]),
                        border = new Vector4(n[4], n[5], n[6], n[7])
                    };
                }
            }
            if (!entries.TryGetValue(id, out var e)) { sprites[id] = null; return null; }
            var texture = Resources.Load<Texture2D>("UI/Batch8/" + e.file);
            if (!texture) { sprites[id] = null; return null; }
            var sprite = Sprite.Create(texture, e.rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, e.border);
            sprite.name = e.file;
            sprites[id] = sprite;
            return sprite;
        }

        public static Image Image(Transform parent, int id, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Art_U" + id.ToString("00"), typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.sprite = Get(id); image.preserveAspect = true; image.raycastTarget = false;
            image.enabled = image.sprite != null;
            return image;
        }

        public static Image Border(RectTransform parent, int id, Vector2 padding)
        {
            var image = Image(parent, id, Vector2.zero, Vector2.zero, Vector2.zero);
            var rt = image.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = -padding; rt.offsetMax = padding;
            image.preserveAspect = false; image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 4f;
            return image;
        }

        public static int Echo(string id)
        {
            for (int i = 0; i < Echoes.All.Length; i++) if (Echoes.All[i].id == id) return 70 + i;
            return -1;
        }

        public static int Item(string id)
        {
            switch (id)
            {
                case "heal": return 50;
                case "tonic": return 51;
                case "stab": return 52;
                case "bomb": return 53;
                case "purifier": return 54;
                case "shield": return 55;
                default: return -1;
            }
        }
    }
}
