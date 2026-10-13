using System.IO;
using Game.RPG.Cursors;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 软件光标的 6 张图标**程序生成器**（需求第 15 条：资源全部占位，不引入外部素材）。
    /// <para>
    /// 做法：在 4 倍超采样缓冲里用硬边图元（圆/环/矩形/三角形/粗线）作画，再下采样到 32×32
    /// —— 这样有天然的边缘抗锯齿，且形状全部由代码参数化，改颜色/粗细不用重做美术资源。
    /// </para>
    /// <para>
    /// 菜单：<c>Tools/我的工具/RPG/生成软件光标图标</c>（幂等，可重复执行；已存在的设置资产只做补齐）。
    /// 与 <c>RpgSceneWirer</c> 同样做成菜单的理由：资源生成必须碰编辑器，而 AI 侧的 execute_code 未必总是可用。
    /// </para>
    /// </summary>
    public static class RpgCursorIconGenerator
    {
        private const string MenuRoot = "Tools/我的工具/RPG/";
        private const string SpriteFolder = "Assets/Textures/RPG/Cursors";

        /// <summary>软件光标设置资产路径（<c>RpgSceneWirer</c> 装配时也用它，避免两处硬编码漂移）。</summary>
        public const string SettingsPath = "Assets/Scripts/Game/RPG/Data/RpgCursorSettings.asset";

        /// <summary>图标边长（像素）。</summary>
        private const int IconSize = 32;

        /// <summary>超采样倍数（内部作图分辨率 = IconSize * SuperSample）。</summary>
        private const int SuperSample = 4;

        // ---------- 调色（占位色，改这里即可整批换色） ----------
        private static readonly Color White = new Color32(245, 245, 250, 255);
        private static readonly Color Ink = new Color32(28, 30, 42, 255);
        private static readonly Color Green = new Color32(96, 220, 124, 255);
        private static readonly Color Amber = new Color32(255, 190, 72, 255);
        private static readonly Color Cyan = new Color32(96, 202, 240, 255);
        private static readonly Color Red = new Color32(236, 74, 74, 255);
        private static readonly Color Gray = new Color32(160, 164, 176, 255);

        [MenuItem(MenuRoot + "生成软件光标图标")]
        private static void GenerateAll()
        {
            Directory.CreateDirectory(SpriteFolder);

            var report = new System.Collections.Generic.List<string>();

            var def = Build("Cursor_Default", "Default（悬停 UI / 无命中）", DrawDefault);
            var grd = Build("Cursor_Ground", "Ground（点地移动）", DrawGround);
            var pik = Build("Cursor_Pickup", "Pickup（拾取）", DrawPickup);
            var npc = Build("Cursor_Npc", "Npc（对话）", DrawNpc);
            var enm = Build("Cursor_Enemy", "Enemy（攻击）", DrawEnemy);
            var unx = Build("Cursor_Unreachable", "Unreachable（不可达）", DrawUnreachable);

            AssetDatabase.Refresh();
            report.Add("已生成 6 张图标 → " + SpriteFolder);

            var settings = EnsureSettings();
            Assign(settings, def, grd, pik, npc, enm, unx);

            Debug.Log($"[RpgCursorIconGenerator] 软件光标资源就绪：{string.Join("；", report)}" +
                      $"；设置资产 = {SettingsPath}");
        }

        [MenuItem(MenuRoot + "检查软件光标资源是否齐全")]
        private static void CheckResources()
        {
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 6; i++)
            {
                var name = IconNames[i];
                var path = $"{SpriteFolder}/{name}.png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                lines.Add($"{name}={(sprite != null ? "OK" : "缺失")}");
            }

            var settings = AssetDatabase.LoadAssetAtPath<RpgCursorSettingsSO>(SettingsPath);
            lines.Add($"设置资产={(settings != null ? "OK" : "缺失")}");

            if (settings != null)
            {
                lines.Add($"槽位 Default={(settings.defaultIcon != null)}/Ground={(settings.groundIcon != null)}" +
                          $"/Pickup={(settings.pickupIcon != null)}/Npc={(settings.npcIcon != null)}" +
                          $"/Enemy={(settings.enemyIcon != null)}/Unreachable={(settings.unreachableIcon != null)}");
            }

            Debug.Log("[RpgCursorIconGenerator] 检查结果：" + string.Join("；", lines));
        }

        private static readonly string[] IconNames =
        {
            "Cursor_Default", "Cursor_Ground", "Cursor_Pickup", "Cursor_Npc", "Cursor_Enemy", "Cursor_Unreachable",
        };

        // ---------- 生成 / 导入 ----------

        private static Sprite Build(string fileName, string label, System.Action<Color32[], int> draw)
        {
            int size = IconSize * SuperSample;
            var buffer = new Color32[size * size];   // 默认全透明

            draw(buffer, size);

            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(Downsample(buffer, size, IconSize));
            tex.Apply();

            var path = $"{SpriteFolder}/{fileName}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                // 首次写入的文件还没被 AssetDatabase 认识 → 先导入一次再取 importer
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                // ★ 必须可读：传统方案（Cursor.SetCursor）对不可读贴图会静默失效
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static RpgCursorSettingsSO EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RpgCursorSettingsSO>(SettingsPath);
            if (settings != null) return settings;

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            settings = ScriptableObject.CreateInstance<RpgCursorSettingsSO>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static void Assign(RpgCursorSettingsSO settings,
            Sprite def, Sprite ground, Sprite pickup, Sprite npc, Sprite enemy, Sprite unreachable)
        {
            settings.defaultIcon = def;
            settings.groundIcon = ground;
            settings.pickupIcon = pickup;
            settings.npcIcon = npc;
            settings.enemyIcon = enemy;
            settings.unreachableIcon = unreachable;

            // 反射式赋值不会标脏（本项目踩过）→ 必须显式 SetDirty 才落盘
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        /// <summary>超采样 → 目标尺寸：按 alpha 加权平均，避免半透明边缘出现黑边。</summary>
        private static Color32[] Downsample(Color32[] src, int srcSize, int dstSize)
        {
            var dst = new Color32[dstSize * dstSize];
            int block = srcSize / dstSize;
            float total = block * block;

            for (int y = 0; y < dstSize; y++)
            {
                for (int x = 0; x < dstSize; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;

                    for (int sy = 0; sy < block; sy++)
                    {
                        for (int sx = 0; sx < block; sx++)
                        {
                            var p = src[(y * block + sy) * srcSize + (x * block + sx)];
                            float pa = p.a / 255f;
                            r += p.r * pa;
                            g += p.g * pa;
                            b += p.b * pa;
                            a += pa;
                        }
                    }

                    if (a <= 0f)
                    {
                        dst[y * dstSize + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    dst[y * dstSize + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(r / a), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(g / a), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(b / a), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(a / total * 255f), 0, 255));
                }
            }

            return dst;
        }

        // ---------- 六个图标（坐标 y 向上，原点左下） ----------

        private static void DrawDefault(Color32[] b, int n)
        {
            // 经典箭头：三角形箭头 + 尾巴粗线；先画深色描边再覆白色，保证在任何背景上都看得清
            var s = n / 128f;
            Arrow(b, n, s, Ink);
            Arrow(b, n, s * 0.82f, White);
        }

        private static void Arrow(Color32[] b, int n, float s, Color c)
        {
            float o = (1f - s) * 0.5f * n;   // 缩放时保持居中
            System.Func<float, float, Vector2> p = (x, y) => new Vector2(o + x * s, o + y * s);

            Triangle(b, n, p(30, 122), p(30, 30), p(96, 76), c);
            ThickLine(b, n, p(62, 70), p(88, 20), 16f * s, c);
        }

        private static void DrawGround(Color32[] b, int n)
        {
            float k = n / 128f;
            float cx = n * 0.5f, cy = n * 0.5f;
            Ring(b, n, cx, cy, 42f * k, 11f * k, Ink);
            Ring(b, n, cx, cy, 42f * k, 7f * k, Green);
            Disc(b, n, cx, cy, 12f * k, Green);
        }

        private static void DrawPickup(Color32[] b, int n)
        {
            float k = n / 128f;
            float cx = n * 0.5f, cy = n * 0.5f;
            Diamond(b, n, cx, cy, 50f * k, Ink);
            Diamond(b, n, cx, cy, 40f * k, Amber);
        }

        private static void DrawNpc(Color32[] b, int n)
        {
            float k = n / 128f;

            // 气泡本体（圆角矩形）+ 左下角小尾巴
            RoundedBox(b, n, 16f * k, 34f * k, 112f * k, 102f * k, 14f * k, Ink);
            Triangle(b, n, new Vector2(44f * k, 40f * k), new Vector2(78f * k, 40f * k), new Vector2(46f * k, 10f * k), Ink);

            RoundedBox(b, n, 22f * k, 40f * k, 106f * k, 96f * k, 10f * k, Cyan);
            Triangle(b, n, new Vector2(50f * k, 44f * k), new Vector2(74f * k, 44f * k), new Vector2(52f * k, 20f * k), Cyan);
        }

        private static void DrawEnemy(Color32[] b, int n)
        {
            float k = n / 128f;
            float cx = n * 0.5f, cy = n * 0.5f;

            Ring(b, n, cx, cy, 42f * k, 11f * k, Ink);
            Ring(b, n, cx, cy, 42f * k, 7f * k, Red);
            ThickLine(b, n, new Vector2(cx - 54f * k, cy), new Vector2(cx + 54f * k, cy), 7f * k, Red);
            ThickLine(b, n, new Vector2(cx, cy - 54f * k), new Vector2(cx, cy + 54f * k), 7f * k, Red);
        }

        private static void DrawUnreachable(Color32[] b, int n)
        {
            float k = n / 128f;
            float cx = n * 0.5f, cy = n * 0.5f;

            Ring(b, n, cx, cy, 42f * k, 12f * k, Ink);
            Ring(b, n, cx, cy, 42f * k, 8f * k, Red);
            // 禁止符的斜杠：左下 → 右上
            ThickLine(b, n, new Vector2(cx - 26f * k, cy - 26f * k), new Vector2(cx + 26f * k, cy + 26f * k), 10f * k, Red);
        }

        // ---------- 图元（硬边，坐标 y 向上） ----------

        private static void Disc(Color32[] b, int n, float cx, float cy, float r, Color c)
        {
            float r2 = r * r;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(cy + r));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r2) Set(b, n, x, y, c);
                }
            }
        }

        private static void Ring(Color32[] b, int n, float cx, float cy, float r, float thickness, Color c)
        {
            float ro = r + thickness * 0.5f, ri = Mathf.Max(0f, r - thickness * 0.5f);
            float ro2 = ro * ro, ri2 = ri * ri;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - ro)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(cx + ro));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ro)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(cy + ro));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= ro2 && d2 >= ri2) Set(b, n, x, y, c);
                }
            }
        }

        private static void Diamond(Color32[] b, int n, float cx, float cy, float r, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(cy + r));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - cx), dy = Mathf.Abs(y + 0.5f - cy);
                    if (dx + dy <= r) Set(b, n, x, y, c);
                }
            }
        }

        private static void RoundedBox(Color32[] b, int n, float x0, float y0, float x1, float y1, float radius, Color c)
        {
            int ix0 = Mathf.Max(0, Mathf.FloorToInt(x0)), ix1 = Mathf.Min(n - 1, Mathf.CeilToInt(x1));
            int iy0 = Mathf.Max(0, Mathf.FloorToInt(y0)), iy1 = Mathf.Min(n - 1, Mathf.CeilToInt(y1));

            for (int y = iy0; y <= iy1; y++)
            {
                for (int x = ix0; x <= ix1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    if (px < x0 || px > x1 || py < y0 || py > y1) continue;

                    // 四角做圆角：把点收缩到「内矩形」后再算到角心距离
                    float qx = Mathf.Clamp(px, x0 + radius, x1 - radius);
                    float qy = Mathf.Clamp(py, y0 + radius, y1 - radius);
                    float dx = px - qx, dy = py - qy;
                    if (dx * dx + dy * dy <= radius * radius) Set(b, n, x, y, c);
                }
            }
        }

        private static void Triangle(Color32[] b, int n, Vector2 a, Vector2 c1, Vector2 c2, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(c1.x, c2.x))));
            int x1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(c1.x, c2.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(c1.y, c2.y))));
            int y1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(c1.y, c2.y))));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d1 = Cross(a, c1, p), d2 = Cross(c1, c2, p), d3 = Cross(c2, a, p);
                    bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
                    bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
                    if (!(neg && pos)) Set(b, n, x, y, c);
                }
            }
        }

        /// <summary>二维叉积（用于点在三角形内判定）。</summary>
        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        private static void ThickLine(Color32[] b, int n, Vector2 a, Vector2 c, float thickness, Color col)
        {
            float half = thickness * 0.5f;
            float minX = Mathf.Min(a.x, c.x) - half, maxX = Mathf.Max(a.x, c.x) + half;
            float minY = Mathf.Min(a.y, c.y) - half, maxY = Mathf.Max(a.y, c.y) + half;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(maxX));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(maxY));

            var ab = c - a;
            float len2 = ab.sqrMagnitude;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = len2 <= 0f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    var closest = a + ab * t;
                    if ((p - closest).sqrMagnitude <= half * half) Set(b, n, x, y, col);
                }
            }
        }

        private static void Set(Color32[] b, int n, int x, int y, Color c)
        {
            b[y * n + x] = c;
        }
    }
}
