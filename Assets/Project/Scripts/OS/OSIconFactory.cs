using System.Collections.Generic;
using UnityEngine;

namespace Project.LinuxOS
{
    /// <summary>
    /// Генератор аккуратных, чётких иконок рабочего стола, проводника и системного трея (без внешних ассетов).
    /// Кэширует спрайты один раз на сессию.
    /// </summary>
    public static class OSIconFactory
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Cache.Clear();
        }

        public static Sprite GetDesktopMyComputer() => GetOrCreate("desk_pc", 64, DrawMyComputer);
        public static Sprite GetDesktopTrash(bool hasItems) => GetOrCreate(hasItems ? "desk_trash_full" : "desk_trash_empty", 64, tex => DrawTrash(tex, hasItems));
        public static Sprite GetDesktopNotes() => GetOrCreate("desk_notes", 64, DrawNotes);
        public static Sprite GetDesktopBrowser() => GetOrCreate("desk_browser", 64, DrawBrowser);
        public static Sprite GetDesktopMail() => GetOrCreate("desk_mail", 64, DrawMail);
        public static Sprite GetDesktopDecoder() => GetOrCreate("desk_decoder", 64, DrawDecoder);
        public static Sprite GetDesktopTerminal() => GetOrCreate("desk_term", 64, DrawTerminal);
        public static Sprite GetDesktopDataCenter() => GetOrCreate("desk_dc", 64, DrawDataCenter);
        public static Sprite GetDesktopMarket() => GetOrCreate("desk_market", 64, DrawMarketCrate);
        public static Sprite GetDesktopProxy() => GetOrCreate("desk_proxy", 64, DrawProxyShield);

        public static Sprite GetFolderIcon() => GetOrCreate("fs_folder", 48, DrawFolder);
        public static Sprite GetFileIcon(bool isLog) => GetOrCreate(isLog ? "fs_log" : "fs_txt", 48, tex => DrawFileDoc(tex, isLog));

        public static Sprite GetWifiBars(int bars, Color tint) => GetOrCreate($"wifi_{bars}_{ColorUtility.ToHtmlStringRGBA(tint)}", 32, tex => DrawWifi(tex, bars, tint));
        public static Sprite GetLockIcon(Color tint) => GetOrCreate($"lock_{ColorUtility.ToHtmlStringRGBA(tint)}", 24, tex => DrawLock(tex, tint));
        public static Sprite GetPowerIcon(Color tint) => GetOrCreate($"power_{ColorUtility.ToHtmlStringRGBA(tint)}", 28, tex => DrawPower(tex, tint));
        public static Sprite GetSpeakerIcon(bool muted, Color tint) => GetOrCreate($"spk_{muted}_{ColorUtility.ToHtmlStringRGBA(tint)}", 28, tex => DrawSpeaker(tex, muted, tint));
        public static Sprite GetAppsMenuIcon(Color tint) => GetOrCreate($"apps_{ColorUtility.ToHtmlStringRGBA(tint)}", 24, tex => DrawAppsGrid(tex, tint));
        public static Sprite GetResizeGripIcon(Color tint) => GetOrCreate($"grip_{ColorUtility.ToHtmlStringRGBA(tint)}", 16, tex => DrawResizeGrip(tex, tint));
        public static Sprite GetCheckmarkIcon(Color tint) => GetOrCreate($"chk_{ColorUtility.ToHtmlStringRGBA(tint)}", 20, tex => DrawCheckmark(tex, tint));
        public static Sprite GetEyeIcon(Color tint) => GetOrCreate($"eye_{ColorUtility.ToHtmlStringRGBA(tint)}", 28, tex => DrawEye(tex, tint));

        private static Sprite GetOrCreate(string key, int size, System.Action<Texture2D> draw)
        {
            if (Cache.TryGetValue(key, out var existing) && existing != null)
                return existing;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Clear(tex, new Color(0, 0, 0, 0));
            draw(tex);
            tex.Apply();

            var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sp;
            return sp;
        }

        private static void Clear(Texture2D t, Color c)
        {
            var pixels = new Color[t.width * t.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
            t.SetPixels(pixels);
        }

        private static void FillRect(Texture2D t, int x, int y, int w, int h, Color c)
        {
            int x0 = Mathf.Clamp(x, 0, t.width);
            int y0 = Mathf.Clamp(y, 0, t.height);
            int x1 = Mathf.Clamp(x + w, 0, t.width);
            int y1 = Mathf.Clamp(y + h, 0, t.height);
            for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                    BlendPixel(t, px, py, c);
        }

        private static void StrokeRect(Texture2D t, int x, int y, int w, int h, int thick, Color c)
        {
            FillRect(t, x, y, w, thick, c);
            FillRect(t, x, y + h - thick, w, thick, c);
            FillRect(t, x, y, thick, h, c);
            FillRect(t, x + w - thick, y, thick, h, c);
        }

        private static void DrawCircle(Texture2D t, float cx, float cy, float r, Color c, bool filled = true, float thick = 2f)
        {
            for (int py = 0; py < t.height; py++)
            {
                for (int px = 0; px < t.width; px++)
                {
                    float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    if (filled)
                    {
                        if (d <= r) BlendPixel(t, px, py, c);
                        else if (d < r + 1f) BlendPixel(t, px, py, new Color(c.r, c.g, c.b, c.a * (1f - (d - r))));
                    }
                    else
                    {
                        float diff = Mathf.Abs(d - r);
                        if (diff <= thick * 0.5f) BlendPixel(t, px, py, c);
                    }
                }
            }
        }

        private static void DrawLine(Texture2D t, float x0, float y0, float x1, float y1, float thick, Color c)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)) * 2f);
            if (steps < 1) steps = 1;
            for (int i = 0; i <= steps; i++)
            {
                float f = (float)i / steps;
                float cx = Mathf.Lerp(x0, x1, f);
                float cy = Mathf.Lerp(y0, y1, f);
                DrawCircle(t, cx, cy, thick * 0.5f, c, true);
            }
        }

        private static void BlendPixel(Texture2D t, int x, int y, Color src)
        {
            if (x < 0 || x >= t.width || y < 0 || y >= t.height) return;
            Color dst = t.GetPixel(x, y);
            float outA = src.a + dst.a * (1f - src.a);
            if (outA <= 0.001f) return;
            Color res = (src * src.a + dst * dst.a * (1f - src.a)) / outA;
            res.a = outA;
            t.SetPixel(x, y, res);
        }

        // --- Иконки рабочего стола ---

        private static void DrawMyComputer(Texture2D t)
        {
            // Монитор + подставка
            FillRect(t, 6, 18, 52, 38, new Color(0.14f, 0.16f, 0.19f, 1f));
            StrokeRect(t, 6, 18, 52, 38, 2, new Color(0.68f, 0.72f, 0.78f, 1f));
            FillRect(t, 10, 22, 44, 30, new Color(0.18f, 0.30f, 0.42f, 1f));
            // Диагональный блик на экране
            FillRect(t, 12, 44, 24, 5, new Color(0.32f, 0.52f, 0.68f, 0.6f));
            FillRect(t, 12, 37, 14, 4, new Color(0.85f, 0.41f, 0.16f, 0.9f));
            // Стойка и основание
            FillRect(t, 27, 10, 10, 8, new Color(0.55f, 0.58f, 0.64f, 1f));
            FillRect(t, 18, 6, 28, 5, new Color(0.68f, 0.72f, 0.78f, 1f));
        }

        private static void DrawTrash(Texture2D t, bool hasItems)
        {
            Color metal = new Color(0.72f, 0.75f, 0.80f, 1f);
            Color dark = new Color(0.34f, 0.38f, 0.43f, 1f);

            if (hasItems)
            {
                // Смятая бумага торчит сверху
                DrawCircle(t, 26, 48, 7, new Color(0.92f, 0.92f, 0.88f, 1f));
                DrawCircle(t, 38, 47, 6, new Color(0.85f, 0.85f, 0.80f, 1f));
            }

            // Корпус корзины
            FillRect(t, 15, 8, 34, 38, dark);
            StrokeRect(t, 15, 8, 34, 38, 2, metal);
            // Вертикальные рёбра
            FillRect(t, 22, 13, 3, 28, metal);
            FillRect(t, 30, 13, 4, 28, metal);
            FillRect(t, 39, 13, 3, 28, metal);
            // Крышка
            FillRect(t, 11, 46, 42, 5, metal);
            FillRect(t, 25, 51, 14, 4, metal);
        }

        private static void DrawNotes(Texture2D t)
        {
            // Белый блокнот с разлиновкой и карандашом (как на референсе!)
            FillRect(t, 12, 8, 36, 46, new Color(0.95f, 0.96f, 0.97f, 1f));
            StrokeRect(t, 12, 8, 36, 46, 2, new Color(0.25f, 0.28f, 0.32f, 1f));
            // Спираль сверху
            for (int x = 16; x <= 42; x += 6)
                FillRect(t, x, 51, 3, 6, new Color(0.35f, 0.38f, 0.42f, 1f));
            // Строки
            for (int y = 16; y <= 44; y += 7)
                FillRect(t, 17, y, 24, 2, new Color(0.68f, 0.74f, 0.80f, 1f));
            // Карандаш справа под углом
            DrawLine(t, 36, 16, 54, 44, 5f, new Color(0.88f, 0.62f, 0.18f, 1f));
            DrawLine(t, 34, 13, 37, 17, 3f, new Color(0.18f, 0.18f, 0.20f, 1f));
        }

        private static void DrawBrowser(Texture2D t)
        {
            Color rim = new Color(0.82f, 0.86f, 0.90f, 1f);
            DrawCircle(t, 32, 32, 23, new Color(0.16f, 0.24f, 0.34f, 1f), true);
            DrawCircle(t, 32, 32, 23, rim, false, 2.5f);
            DrawCircle(t, 32, 32, 12, new Color(0.85f, 0.41f, 0.16f, 1f), false, 2f);
            DrawLine(t, 10, 32, 54, 32, 2f, rim);
            DrawLine(t, 32, 10, 32, 54, 2f, rim);
        }

        private static void DrawMail(Texture2D t)
        {
            FillRect(t, 8, 14, 48, 36, new Color(0.92f, 0.93f, 0.95f, 1f));
            StrokeRect(t, 8, 14, 48, 36, 2, new Color(0.22f, 0.25f, 0.29f, 1f));
            DrawLine(t, 10, 48, 32, 30, 2.5f, new Color(0.85f, 0.41f, 0.16f, 1f));
            DrawLine(t, 54, 48, 32, 30, 2.5f, new Color(0.85f, 0.41f, 0.16f, 1f));
        }

        private static void DrawDecoder(Texture2D t)
        {
            FillRect(t, 10, 10, 44, 44, new Color(0.16f, 0.18f, 0.21f, 1f));
            StrokeRect(t, 10, 10, 44, 44, 2, new Color(0.85f, 0.41f, 0.16f, 1f));
            DrawCircle(t, 24, 34, 8, new Color(0.92f, 0.72f, 0.26f, 1f), false, 3f);
            DrawLine(t, 31, 34, 48, 34, 3f, new Color(0.92f, 0.72f, 0.26f, 1f));
            FillRect(t, 40, 26, 3, 8, new Color(0.92f, 0.72f, 0.26f, 1f));
            FillRect(t, 45, 28, 3, 6, new Color(0.92f, 0.72f, 0.26f, 1f));
        }

        private static void DrawTerminal(Texture2D t)
        {
            FillRect(t, 6, 10, 52, 44, new Color(0.09f, 0.10f, 0.11f, 1f));
            FillRect(t, 6, 46, 52, 8, new Color(0.22f, 0.24f, 0.27f, 1f));
            StrokeRect(t, 6, 10, 52, 44, 2, new Color(0.55f, 0.58f, 0.62f, 1f));
            // >_
            Color green = new Color(0.24f, 0.80f, 0.36f, 1f);
            DrawLine(t, 14, 36, 24, 28, 3f, green);
            DrawLine(t, 14, 20, 24, 28, 3f, green);
            FillRect(t, 28, 20, 14, 4, green);
        }

        private static void DrawDataCenter(Texture2D t)
        {
            FillRect(t, 12, 8, 40, 48, new Color(0.18f, 0.20f, 0.23f, 1f));
            StrokeRect(t, 12, 8, 40, 48, 2, new Color(0.62f, 0.66f, 0.72f, 1f));
            for (int y = 14; y <= 44; y += 14)
            {
                FillRect(t, 16, y, 32, 9, new Color(0.10f, 0.11f, 0.13f, 1f));
                FillRect(t, 19, y + 3, 4, 3, new Color(0.24f, 0.82f, 0.38f, 1f));
                FillRect(t, 26, y + 3, 4, 3, new Color(0.85f, 0.41f, 0.16f, 1f));
                FillRect(t, 34, y + 3, 11, 3, new Color(0.45f, 0.48f, 0.54f, 1f));
            }
        }

        private static void DrawMarketCrate(Texture2D t)
        {
            // Картонная коробка как у Shadow Market на референсе
            FillRect(t, 10, 10, 44, 36, new Color(0.68f, 0.52f, 0.36f, 1f));
            FillRect(t, 10, 38, 44, 10, new Color(0.78f, 0.62f, 0.44f, 1f));
            StrokeRect(t, 10, 10, 44, 38, 2, new Color(0.32f, 0.24f, 0.16f, 1f));
            FillRect(t, 28, 12, 8, 34, new Color(0.86f, 0.74f, 0.56f, 0.9f));
        }

        // --- Иконки проводника ---

        private static void DrawFolder(Texture2D t)
        {
            Color body = new Color(0.86f, 0.60f, 0.24f, 1f);
            Color tab = new Color(0.74f, 0.48f, 0.18f, 1f);
            FillRect(t, 6, 32, 18, 8, tab);
            FillRect(t, 6, 8, 36, 26, body);
            StrokeRect(t, 6, 8, 36, 26, 2, new Color(0.45f, 0.30f, 0.12f, 1f));
        }

        private static void DrawFileDoc(Texture2D t, bool isLog)
        {
            FillRect(t, 10, 6, 28, 36, new Color(0.95f, 0.96f, 0.97f, 1f));
            StrokeRect(t, 10, 6, 28, 36, 2, isLog ? new Color(0.85f, 0.41f, 0.16f, 1f) : new Color(0.35f, 0.39f, 0.45f, 1f));
            for (int y = 13; y <= 33; y += 6)
                FillRect(t, 15, y, 18, 2, new Color(0.48f, 0.52f, 0.58f, 1f));
        }

        // --- Иконки трея и Wi-Fi ---

        private static void DrawWifi(Texture2D t, int bars, Color tint)
        {
            Color dim = new Color(tint.r, tint.g, tint.b, 0.22f);
            float cx = t.width * 0.5f;
            float cy = 6f;

            // Точка внизу
            DrawCircle(t, cx, cy + 2f, 2.2f, bars > 0 ? tint : dim, true);

            // 3 дуги уровня сигнала
            float[] radii = { 7f, 12f, 17f };
            for (int i = 0; i < radii.Length; i++)
            {
                Color c = bars >= (i + 2) ? tint : dim;
                float r = radii[i];
                for (float deg = 42f; deg <= 138f; deg += 2.5f)
                {
                    float rad = deg * Mathf.Deg2Rad;
                    float x = cx + Mathf.Cos(rad) * r;
                    float y = cy + Mathf.Sin(rad) * r;
                    DrawCircle(t, x, y, 1.3f, c, true);
                }
            }

            // Если нет сети (bars == 0), рисуем аккуратную косую черту
            if (bars == 0)
            {
                DrawLine(t, 6, 6, t.width - 6, t.height - 6, 2.2f, new Color(0.85f, 0.32f, 0.28f, 1f));
            }
        }

        private static void DrawLock(Texture2D t, Color tint)
        {
            // Замочек для запароленной Wi-Fi сети
            FillRect(t, 5, 3, 14, 10, tint);
            DrawCircle(t, 12, 14, 4.5f, tint, false, 2.2f);
            FillRect(t, 7, 10, 10, 4, tint);
        }

        private static void DrawPower(Texture2D t, Color tint)
        {
            float cx = t.width * 0.5f;
            float cy = t.height * 0.5f - 1f;
            for (float deg = -55f; deg <= 235f; deg += 3f)
            {
                float rad = deg * Mathf.Deg2Rad;
                DrawCircle(t, cx + Mathf.Cos(rad) * 8.5f, cy + Mathf.Sin(rad) * 8.5f, 1.4f, tint, true);
            }
            DrawLine(t, cx, cy + 1f, cx, cy + 11f, 2.6f, tint);
        }

        private static void DrawSpeaker(Texture2D t, bool muted, Color tint)
        {
            FillRect(t, 5, 10, 5, 8, tint);
            DrawLine(t, 10, 10, 15, 6, 2.5f, tint);
            DrawLine(t, 10, 18, 15, 22, 2.5f, tint);
            FillRect(t, 14, 6, 2, 16, tint);

            if (muted)
            {
                Color red = new Color(0.85f, 0.32f, 0.28f, 1f);
                DrawLine(t, 18, 9, 25, 19, 2f, red);
                DrawLine(t, 25, 9, 18, 19, 2f, red);
            }
            else
            {
                DrawLine(t, 19, 11, 21, 14, 1.8f, tint);
                DrawLine(t, 21, 14, 19, 17, 1.8f, tint);
                DrawLine(t, 22, 8, 25, 14, 1.8f, tint);
                DrawLine(t, 25, 14, 22, 20, 1.8f, tint);
            }
        }

        private static void DrawAppsGrid(Texture2D t, Color tint)
        {
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    FillRect(t, 4 + c * 6, 4 + r * 6, 4, 4, tint);
        }

        private static void DrawProxyShield(Texture2D t)
        {
            // Щит прокси / VPN
            Color body = new Color(0.16f, 0.22f, 0.28f, 1f);
            Color rim = new Color(0.85f, 0.41f, 0.16f, 1f);
            FillRect(t, 14, 22, 36, 32, body);
            StrokeRect(t, 14, 22, 36, 32, 3, rim);
            DrawLine(t, 14, 22, 32, 8, 3f, rim);
            DrawLine(t, 50, 22, 32, 8, 3f, rim);
            DrawCircle(t, 32, 36, 7, new Color(0.24f, 0.78f, 0.36f, 1f), false, 2.5f);
            DrawLine(t, 22, 36, 42, 36, 2f, new Color(0.24f, 0.78f, 0.36f, 1f));
        }

        private static void DrawResizeGrip(Texture2D t, Color tint)
        {
            DrawLine(t, 4, 2, 14, 12, 1.5f, tint);
            DrawLine(t, 8, 2, 14, 8, 1.5f, tint);
            DrawLine(t, 12, 2, 14, 4, 1.5f, tint);
        }

        private static void DrawCheckmark(Texture2D t, Color tint)
        {
            DrawLine(t, 3, 10, 8, 5, 2.2f, tint);
            DrawLine(t, 8, 5, 16, 15, 2.2f, tint);
        }

        private static void DrawEye(Texture2D t, Color tint)
        {
            DrawCircle(t, 14, 14, 9f, tint, false, 2f);
            DrawCircle(t, 14, 14, 3.5f, tint, true);
        }
    }
}
