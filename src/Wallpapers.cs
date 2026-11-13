using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace MacDock
{
    // Fondos de pantalla en pixel art, dibujados por codigo (originales, sin imagenes de internet):
    //   "universo": estrellas, nebulosa, planeta con anillos y luna.
    //   "neon":     ciudad cyberpunk con ventanas encendidas, letreros de neon, sol retro y lluvia.
    // Cada tema tiene 4 versiones segun la hora: amanecer, dia, atardecer y noche (para el fondo dinamico).
    static class Wallpapers
    {
        public static readonly string[] Themes = { "aurora", "universo", "neon" };
        public static readonly string[] Periods = { "amanecer", "dia", "atardecer", "noche" };

        public static string NameOf(string theme)
        {
            switch (theme)
            {
                case "universo": return "Universo pixel";
                case "neon": return "Ciudad neón";
                default: return "Aurora (el de MacDock)";
            }
        }

        static string PicDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MacDock"); } }
        static string PrevFile { get { return Path.Combine(Program.DataDir, "fondo-anterior.txt"); } }
        public static string PeriodFile(string period) { return Path.Combine(PicDir, period + ".png"); }

        public static string PeriodNow()
        {
            var now = DateTime.Now.TimeOfDay;   // mismas horas que el fondo dinamico del dock
            return now < new TimeSpan(6, 0, 0) ? "noche" : now < new TimeSpan(9, 0, 0) ? "amanecer" :
                   now < new TimeSpan(17, 0, 0) ? "dia" : now < new TimeSpan(19, 30, 0) ? "atardecer" : "noche";
        }

        // Genera las 4 versiones del tema (en segundo plano) y pone la de la hora actual.
        // La primera vez guarda tu fondo anterior para poder volver a el (y el desinstalador lo regresa).
        public static void Apply(string theme, Action<bool> done)
        {
            var th = new Thread(() =>
            {
                bool ok = false;
                try
                {
                    RememberPrevious();
                    Directory.CreateDirectory(PicDir);
                    var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                    foreach (var p in Periods)
                        using (var bmp = Render(theme, p, b.Width, b.Height)) bmp.Save(PeriodFile(p), ImageFormat.Png);
                    SetWallpaper(PeriodFile(PeriodNow()));
                    ok = true;
                }
                catch (Exception ex) { Program.Log(ex); }
                if (done != null) done(ok);
            });
            th.IsBackground = true; th.Start();
        }

        public static bool HasPrevious { get { return File.Exists(PrevFile); } }

        // Regresa el fondo que tenias antes de elegir uno de MacDock.
        public static bool RestorePrevious()
        {
            try
            {
                if (!File.Exists(PrevFile)) return false;
                var lines = File.ReadAllLines(PrevFile);
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                    if (k != null && lines.Length >= 3) { k.SetValue("WallpaperStyle", lines[1]); k.SetValue("TileWallpaper", lines[2]); }
                N.SpiStr(0x14, 0, lines.Length > 0 ? lines[0] : "", 3);
                File.Delete(PrevFile);
                return true;
            }
            catch (Exception ex) { Program.Log(ex); return false; }
        }

        static void RememberPrevious()
        {
            if (File.Exists(PrevFile)) return;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
            {
                if (k == null) return;
                string cur = Convert.ToString(k.GetValue("WallPaper", ""));
                // si el fondo actual ya es de MacDock (aurora de los Ajustes), no es "tu" fondo: de ese se encarga el menu de Ajustes
                if (cur != "" && cur.StartsWith(PicDir, StringComparison.OrdinalIgnoreCase)) return;
                Directory.CreateDirectory(Program.DataDir);
                File.WriteAllLines(PrevFile, new[] { cur, Convert.ToString(k.GetValue("WallpaperStyle", "10")), Convert.ToString(k.GetValue("TileWallpaper", "0")) });
            }
        }

        static void SetWallpaper(string file)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                if (k != null) { k.SetValue("WallpaperStyle", "10"); k.SetValue("TileWallpaper", "0"); }   // rellenar la pantalla
            N.SpiStr(0x14, 0, file, 3);
        }

        // Dibuja el fondo al tamano de la pantalla. El pixel art se hace en pequeno y se agranda sin suavizar.
        public static Bitmap Render(string theme, string period, int W, int H)
        {
            if (theme == "aurora") return Aurora(period, W, H);
            int px = Math.Max(3, (int)Math.Round(H / 216.0));
            int w = (W + px - 1) / px, h = (H + px - 1) / px;
            var art = new Canvas(w, h);
            var glow = new Canvas(w, h);
            var rnd = new Random(theme == "neon" ? 2077 : 4242);   // siempre el mismo dibujo para el mismo tema
            if (theme == "neon") City(art, glow, period, rnd); else Universe(art, glow, period, rnd);

            var outBmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var lowArt = art.ToBitmap())
            using (var lowGlow = glow.ToBitmap())
            using (var g = Graphics.FromImage(outBmp))
            {
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(lowArt, new Rectangle(0, 0, w * px, h * px));
                // resplandor: la capa de luces se reduce y se agranda suave, encima del pixel art
                int gw = Math.Max(2, w / 3), gh = Math.Max(2, h / 3);
                using (var soft = new Bitmap(gw, gh, PixelFormat.Format32bppArgb))
                {
                    using (var gs = Graphics.FromImage(soft)) { gs.InterpolationMode = InterpolationMode.HighQualityBilinear; gs.DrawImage(lowGlow, 0, 0, gw, gh); }
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        var cm = new ColorMatrix { Matrix33 = 0.85f };
                        ia.SetColorMatrix(cm);
                        g.DrawImage(soft, new Rectangle(0, 0, W, H), 0, 0, gw, gh, GraphicsUnit.Pixel, ia);
                    }
                }
            }
            return outBmp;
        }

        // =====================  aurora (el fondo original de MacDock, pasado de los Ajustes a C#)  =====================
        static Bitmap Aurora(string period, int W, int H)
        {
            int[] c1, c2; int[][] blobs;
            switch (period)
            {
                case "amanecer": c1 = new[] { 44, 46, 104 }; c2 = new[] { 246, 160, 132 };
                    blobs = new[] { new[] { 20, 85, 80, 60, 255, 170, 120, 200 }, new[] { 60, 95, 85, 50, 255, 120, 140, 170 }, new[] { 85, 55, 60, 55, 255, 196, 150, 160 },
                                    new[] { 30, 25, 70, 50, 120, 130, 230, 150 }, new[] { 80, 15, 55, 45, 190, 140, 240, 130 }, new[] { 5, 35, 45, 40, 255, 150, 190, 110 } }; break;
                case "dia": c1 = new[] { 118, 170, 255 }; c2 = new[] { 196, 168, 255 };
                    blobs = new[] { new[] { 20, 80, 80, 60, 255, 255, 255, 150 }, new[] { 60, 95, 85, 50, 255, 196, 230, 170 }, new[] { 85, 60, 60, 55, 160, 230, 255, 170 },
                                    new[] { 30, 25, 70, 50, 90, 150, 255, 140 }, new[] { 80, 15, 55, 45, 210, 180, 255, 150 }, new[] { 5, 35, 45, 40, 140, 220, 255, 120 } }; break;
                case "atardecer": c1 = new[] { 56, 20, 74 }; c2 = new[] { 236, 108, 64 };
                    blobs = new[] { new[] { 20, 82, 80, 60, 255, 140, 50, 200 }, new[] { 60, 95, 85, 50, 255, 90, 90, 180 }, new[] { 85, 60, 60, 55, 240, 70, 150, 170 },
                                    new[] { 30, 25, 70, 50, 120, 60, 200, 150 }, new[] { 80, 15, 55, 45, 255, 120, 80, 130 }, new[] { 5, 35, 45, 40, 255, 190, 90, 110 } }; break;
                default: c1 = new[] { 12, 18, 52 }; c2 = new[] { 58, 24, 92 };
                    blobs = new[] { new[] { 18, 78, 75, 55, 255, 94, 58, 180 }, new[] { 55, 95, 85, 50, 255, 140, 60, 170 }, new[] { 85, 65, 65, 60, 236, 64, 160, 170 },
                                    new[] { 35, 30, 70, 50, 72, 110, 255, 150 }, new[] { 80, 20, 55, 45, 150, 80, 255, 140 }, new[] { 5, 25, 45, 40, 40, 160, 255, 110 } }; break;
            }
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.CompositingQuality = CompositingQuality.HighQuality;
                var rect = new Rectangle(0, 0, W, H);
                using (var bg = new LinearGradientBrush(rect, Color.FromArgb(c1[0], c1[1], c1[2]), Color.FromArgb(c2[0], c2[1], c2[2]), 70f)) g.FillRectangle(bg, rect);
                foreach (var b in blobs)
                {
                    float cx = b[0] / 100f * W, cy = b[1] / 100f * H, rw = b[2] / 100f * W, rh = b[3] / 100f * H;
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(cx - rw / 2, cy - rh / 2, rw, rh);
                        using (var pgb = new PathGradientBrush(path))
                        {
                            pgb.CenterColor = Color.FromArgb(b[7], b[4], b[5], b[6]);
                            pgb.SurroundColors = new[] { Color.FromArgb(0, b[4], b[5], b[6]) };
                            g.FillPath(pgb, path);
                        }
                    }
                }
            }
            return bmp;
        }

        // =====================  universo pixel  =====================
        static void Universe(Canvas c, Canvas glow, string period, Random rnd)
        {
            int w = c.W, h = c.H;
            Color top, bottom; Color[] neb; Color planetA, planetB, ring;
            switch (period)
            {
                case "amanecer": top = Rgb(20, 14, 52); bottom = Rgb(92, 40, 110); neb = new[] { Rgb(120, 60, 160), Rgb(220, 110, 170), Rgb(255, 180, 170) }; planetA = Rgb(240, 150, 110); planetB = Rgb(150, 70, 120); ring = Rgb(255, 210, 190); break;
                case "dia": top = Rgb(10, 30, 70); bottom = Rgb(20, 90, 130); neb = new[] { Rgb(30, 130, 170), Rgb(80, 200, 210), Rgb(190, 250, 240) }; planetA = Rgb(110, 190, 230); planetB = Rgb(40, 90, 160); ring = Rgb(210, 240, 255); break;
                case "atardecer": top = Rgb(30, 10, 36); bottom = Rgb(110, 30, 40); neb = new[] { Rgb(160, 40, 70), Rgb(240, 100, 60), Rgb(255, 190, 110) }; planetA = Rgb(250, 170, 80); planetB = Rgb(170, 60, 50); ring = Rgb(255, 220, 160); break;
                default: top = Rgb(6, 6, 20); bottom = Rgb(24, 14, 56); neb = new[] { Rgb(60, 30, 120), Rgb(150, 60, 190), Rgb(90, 170, 255) }; planetA = Rgb(200, 130, 230); planetB = Rgb(70, 40, 140); ring = Rgb(220, 200, 255); break;
            }
            // cielo en degradado con tramado (bandas pixeladas)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    c.Set(x, y, Dither(top, bottom, (float)y / h, x, y, 12));

            // nebulosa: ruido suave en una franja diagonal, en 3 tonos tramados
            float ox = (float)rnd.NextDouble() * 100, oy = (float)rnd.NextDouble() * 100;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float band = 1f - Math.Abs(((float)y / h) - (0.75f - 0.55f * x / w)) * 2.6f;
                    if (band <= 0) continue;
                    float n = Fbm(x * 0.022f + ox, y * 0.034f + oy) * band;
                    float lv = (n - 0.32f) * 4.2f + Bayer(x, y) * 0.9f - 0.45f;
                    if (lv < 0) continue;
                    int k = Math.Min(neb.Length - 1, (int)lv);
                    c.Blend(x, y, neb[k], 0.35f + 0.18f * k);
                    if (k == neb.Length - 1) glow.Blend(x, y, neb[k], 0.35f);
                }

            // estrellas
            int stars = w * h / 70;
            for (int i = 0; i < stars; i++)
            {
                int x = rnd.Next(w), y = rnd.Next(h);
                int b = 120 + rnd.Next(135);
                var col = rnd.Next(8) == 0 ? Rgb(b, b * 4 / 5, 255) : rnd.Next(10) == 0 ? Rgb(255, b, b * 3 / 4) : Rgb(b, b, b);
                c.Set(x, y, col);
                if (rnd.Next(28) == 0)   // estrellas grandes en cruz con brillo
                {
                    var hi = Rgb(255, 255, 255);
                    c.Set(x, y, hi); c.Blend(x - 1, y, col, 0.8f); c.Blend(x + 1, y, col, 0.8f); c.Blend(x, y - 1, col, 0.8f); c.Blend(x, y + 1, col, 0.8f);
                    c.Blend(x - 2, y, col, 0.35f); c.Blend(x + 2, y, col, 0.35f); c.Blend(x, y - 2, col, 0.35f); c.Blend(x, y + 2, col, 0.35f);
                    glow.Set(x, y, hi); glow.Blend(x - 1, y, col, 0.8f); glow.Blend(x + 1, y, col, 0.8f);
                }
            }

            // estrella fugaz
            {
                int sx = (int)(w * 0.55), sy = (int)(h * 0.12), len = w / 9;
                for (int i = 0; i < len; i++) { float a = 1f - (float)i / len; c.Blend(sx - i, sy + i / 3, Rgb(255, 255, 255), a); glow.Blend(sx - i, sy + i / 3, Rgb(255, 255, 255), a * 0.7f); }
            }

            // planeta lejano y luna
            Planet(c, glow, w * 0.64f, h * 0.38f, h * 0.035f, Mix(planetA, Rgb(255, 255, 255), 0.3f), planetB, false, ring, rnd);
            Moon(c, glow, w * 0.82f, h * 0.2f, h * 0.075f, rnd);

            // planeta grande con anillos, abajo a la izquierda
            Planet(c, glow, w * 0.22f, h * 0.8f, h * 0.32f, planetA, planetB, true, ring, rnd);
        }

        static void Planet(Canvas c, Canvas glow, float cx, float cy, float r, Color a, Color b, bool rings, Color ring, Random rnd)
        {
            float rx = r * 2.05f, ry = r * 0.42f, rIn = r * 1.25f;
            Action<bool> drawRing = front =>
            {
                if (!rings) return;
                for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++)
                    for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
                    {
                        float dx = (x - cx) / rx, dy = (y - cy) / ry, d = dx * dx + dy * dy;
                        float dxi = (x - cx) / rIn, dyi = (y - cy) / (ry * rIn / rx), di = dxi * dxi + dyi * dyi;
                        if (d > 1 || di < 1) continue;
                        if ((y >= cy) != front) continue;                               // mitad de atras primero, la de adelante despues
                        float stripe = (float)Math.Sin(Math.Sqrt(d) * 40);
                        var col = stripe > 0.6f ? Mix(ring, b, 0.45f) : ring;
                        c.Blend(x, y, col, stripe < -0.7f ? 0.35f : 0.85f);
                    }
            };
            drawRing(false);
            for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
                for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                {
                    float dx = (x - cx) / r, dy = (y - cy) / r, d = dx * dx + dy * dy;
                    if (d > 1) continue;
                    // franjas del gas + luz que viene de arriba a la derecha, con tramado en la sombra
                    float bands = (float)Math.Sin(dy * 9 + Fbm(x * 0.08f, y * 0.2f) * 3);
                    var baseCol = Mix(a, b, 0.5f + 0.5f * bands * 0.6f);
                    float dz = (float)Math.Sqrt(1 - d);
                    float light = Math.Max(0, (dx * 0.55f - dy * 0.6f + dz * 0.6f));
                    float lv = light * 4 + Bayer(x, y) - 0.5f;
                    float shade = lv < 1 ? 0.25f : lv < 2 ? 0.5f : lv < 3 ? 0.78f : 1f;
                    var col = Mix(Rgb(8, 6, 20), baseCol, shade);
                    if (d > 0.9f && dx - dy > 0.2f) col = Mix(col, Rgb(255, 255, 255), 0.35f);   // borde iluminado
                    c.Set(x, y, col);
                }
            drawRing(true);
            for (int i = 0; i < 40; i++)   // halo suave
            {
                double ang = -Math.PI / 4 + (rnd.NextDouble() - 0.5) * 1.6;
                glow.Blend((int)(cx + Math.Cos(ang) * r), (int)(cy - Math.Sin(ang) * r), a, 0.5f);
            }
        }

        static void Moon(Canvas c, Canvas glow, float cx, float cy, float r, Random rnd)
        {
            var craters = new List<float[]>();
            for (int i = 0; i < 7; i++) craters.Add(new[] { cx + (float)(rnd.NextDouble() - 0.5) * r * 1.3f, cy + (float)(rnd.NextDouble() - 0.5) * r * 1.3f, r * (0.12f + (float)rnd.NextDouble() * 0.2f) });
            for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
                for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                {
                    float dx = (x - cx) / r, dy = (y - cy) / r, d = dx * dx + dy * dy;
                    if (d > 1) continue;
                    var col = Rgb(214, 210, 226);
                    foreach (var cr in craters) { float ddx = x - cr[0], ddy = y - cr[1]; if (ddx * ddx + ddy * ddy < cr[2] * cr[2]) col = Rgb(168, 164, 186); }
                    float light = Math.Max(0, -dx * 0.7f - dy * 0.3f + (float)Math.Sqrt(1 - d) * 0.5f);
                    float lv = light * 3 + Bayer(x, y) - 0.5f;
                    col = Mix(Rgb(20, 16, 40), col, lv < 0.8f ? 0.3f : lv < 1.8f ? 0.65f : 1f);
                    c.Set(x, y, col);
                    glow.Blend(x, y, Rgb(200, 200, 255), 0.25f);
                }
        }

        // =====================  ciudad neon  =====================
        static void City(Canvas c, Canvas glow, string period, Random rnd)
        {
            int w = c.W, h = c.H;
            Color top, mid, horizon, sunA, sunB, far, near; float lights; bool rain = false, stars = false;
            switch (period)
            {
                case "amanecer": top = Rgb(24, 40, 90); mid = Rgb(120, 90, 160); horizon = Rgb(255, 170, 150); sunA = Rgb(255, 230, 160); sunB = Rgb(255, 120, 150); far = Rgb(90, 70, 130); near = Rgb(24, 18, 44); lights = 0.35f; break;
                case "dia": top = Rgb(60, 140, 210); mid = Rgb(150, 190, 230); horizon = Rgb(255, 200, 220); sunA = Rgb(255, 250, 220); sunB = Rgb(255, 190, 200); far = Rgb(130, 140, 190); near = Rgb(40, 44, 80); lights = 0.12f; break;
                case "atardecer": top = Rgb(40, 16, 70); mid = Rgb(200, 60, 120); horizon = Rgb(255, 150, 60); sunA = Rgb(255, 220, 90); sunB = Rgb(255, 60, 140); far = Rgb(110, 40, 110); near = Rgb(26, 10, 40); lights = 0.5f; break;
                default: top = Rgb(6, 4, 24); mid = Rgb(40, 14, 70); horizon = Rgb(150, 30, 120); sunA = Rgb(255, 120, 200); sunB = Rgb(120, 40, 200); far = Rgb(44, 22, 80); near = Rgb(10, 6, 24); lights = 0.75f; rain = true; stars = true; break;
            }
            int horizonY = (int)(h * 0.66f);
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / horizonY;
                for (int x = 0; x < w; x++)
                    c.Set(x, y, t < 0.6f ? Dither(top, mid, t / 0.6f, x, y, 10) : Dither(mid, horizon, Math.Min(1f, (t - 0.6f) / 0.4f), x, y, 10));
            }
            if (stars) for (int i = 0; i < w * h / 160; i++) { int x = rnd.Next(w), y = rnd.Next(horizonY / 2 + 1); int b = 120 + rnd.Next(120); c.Set(x, y, Rgb(b, b, b + Math.Min(15, 255 - b))); }

            // sol (o luna) retro con franjas, detras de la ciudad
            float scx = w * 0.62f, scy = horizonY - h * 0.2f, sr = h * 0.21f;
            for (int y = (int)(scy - sr); y <= scy + sr; y++)
            {
                float k = (y - (scy - sr)) / (sr * 2);
                bool gap = k > 0.5f && ((int)((k - 0.5f) * 46) % 3 == 0);   // franjas horizontales en la mitad de abajo
                if (gap) continue;
                for (int x = (int)(scx - sr); x <= scx + sr; x++)
                {
                    float dx = x - scx, dy = y - scy;
                    if (dx * dx + dy * dy > sr * sr) continue;
                    var col = Dither(sunA, sunB, k, x, y, 8);
                    c.Set(x, y, col); glow.Blend(x, y, col, 0.55f);
                }
            }

            // tres capas de edificios: lejos (claros), medio y cerca (oscuros y con mas luces)
            // debajo de cada capa se rellena con su color, para que no se vea el cielo entre edificio y edificio
            int farBase = horizonY - (int)(h * 0.02f), midBase = horizonY + (int)(h * 0.12f);
            Color farCol = Mix(far, horizon, 0.25f), midCol = Mix(far, near, 0.45f);
            Skyline(c, glow, rnd, farBase, 0.08f, 0.22f, 6, 14, farCol, lights * 0.3f, false);
            c.FillRect(0, farBase, w, h - farBase, Mix(farCol, Rgb(0, 0, 0), 0.15f)); glow.Clear(0, farBase, w, h - farBase);
            Skyline(c, glow, rnd, midBase, 0.14f, 0.32f, 8, 20, midCol, lights * 0.6f, true);
            c.FillRect(0, midBase, w, h - midBase, Mix(midCol, Rgb(0, 0, 0), 0.2f)); glow.Clear(0, midBase, w, h - midBase);
            Skyline(c, glow, rnd, h + 2, 0.18f, 0.40f, 12, 30, near, lights, true);

            // autos voladores: rayitas de luz
            for (int i = 0; i < 9; i++)
            {
                int y = (int)(h * (0.25f + 0.35f * (float)rnd.NextDouble())), x = rnd.Next(w), len = 3 + rnd.Next(5);
                var col = rnd.Next(2) == 0 ? Rgb(255, 80, 80) : Rgb(255, 240, 200);
                for (int k = 0; k < len; k++) { c.Blend(x + k, y, col, 1f - (float)k / len); glow.Blend(x + k, y, col, 0.6f); }
            }

            // lluvia (solo de noche)
            if (rain)
                for (int i = 0; i < w * h / 150; i++)
                {
                    int x = rnd.Next(w), y = rnd.Next(h), len = 3 + rnd.Next(4);
                    for (int k = 0; k < len; k++) c.Blend(x - k / 2, y + k, Rgb(170, 190, 255), 0.15f);
                }
        }

        static readonly Color[] Neon = { Rgb(0, 240, 255), Rgb(255, 40, 180), Rgb(255, 220, 40), Rgb(120, 255, 90), Rgb(170, 90, 255), Rgb(255, 90, 60) };

        static void Skyline(Canvas c, Canvas glow, Random rnd, int baseY, float minH, float maxH, int minW, int maxW, Color body, float lights, bool signs)
        {
            int w = c.W, h = c.H;
            int x = -rnd.Next(maxW);
            while (x < w)
            {
                int bw = minW + rnd.Next(maxW - minW + 1);
                int bh = (int)(h * (minH + (maxH - minH) * (float)rnd.NextDouble()));
                int top = baseY - bh;
                var col = Mix(body, Rgb(0, 0, 0), (float)rnd.NextDouble() * 0.25f);
                c.FillRect(x, top, bw, baseY - top, col);
                glow.Clear(x, top, bw, baseY - top);   // el edificio tapa el resplandor del sol que esta detras
                // remates: escalon o antena con luz roja
                int roof = rnd.Next(3);
                if (roof == 0) c.FillRect(x + bw / 4, top - 3, bw / 2, 3, col);
                else if (roof == 1)
                {
                    int ax = x + bw / 2, ah = 4 + rnd.Next(8);
                    c.FillRect(ax, top - ah, 1, ah, col);
                    c.Set(ax, top - ah - 1, Rgb(255, 50, 50)); glow.Set(ax, top - ah - 1, Rgb(255, 50, 50));
                }
                // borde iluminado de un lado
                c.FillRect(x + bw - 1, top, 1, baseY - top, Mix(col, Rgb(255, 255, 255), 0.08f));
                // ventanas
                var winA = rnd.Next(3) == 0 ? Rgb(120, 230, 255) : Rgb(255, 210, 120);
                for (int wy = top + 2; wy < baseY - 1; wy += 3)
                    for (int wx = x + 2; wx < x + bw - 2; wx += 2 + (bw > 16 ? 1 : 0))
                        if (rnd.NextDouble() < lights * 0.55)
                        {
                            var wc = Mix(winA, col, (float)rnd.NextDouble() * 0.5f);
                            c.Set(wx, wy, wc);
                            if (rnd.Next(4) == 0) glow.Blend(wx, wy, wc, 0.4f);
                        }
                // letreros de neon
                if (signs && rnd.NextDouble() < 0.55)
                {
                    var nc = Neon[rnd.Next(Neon.Length)];
                    if (rnd.Next(2) == 0)
                    {   // vertical, colgado del costado
                        int sx = rnd.Next(2) == 0 ? x : x + bw - 2, sy = top + 3 + rnd.Next(Math.Max(1, (baseY - top) / 3)), sl = 8 + rnd.Next(12);
                        c.FillRect(sx, sy, 2, sl, nc); glow.FillRect(sx - 1, sy - 1, 4, sl + 2, nc);
                        for (int k = sy + 2; k < sy + sl - 1; k += 3) c.Set(sx, k, Mix(nc, Rgb(255, 255, 255), 0.6f));
                    }
                    else if (bw >= 12)
                    {   // anuncio rectangular
                        int bw2 = Math.Min(bw - 4, 10 + rnd.Next(10)), bh2 = 5 + rnd.Next(4);
                        int bx = x + (bw - bw2) / 2, by = top + 3 + rnd.Next(Math.Max(1, (baseY - top) / 4));
                        c.FillRect(bx, by, bw2, bh2, Mix(nc, Rgb(0, 0, 0), 0.55f));
                        c.Rect(bx, by, bw2, bh2, nc);
                        for (int k = 0; k < bw2 - 4; k += 2) c.FillRect(bx + 2 + k, by + 2, 1, bh2 - 4, Mix(nc, Rgb(255, 255, 255), 0.4f));
                        glow.FillRect(bx - 1, by - 1, bw2 + 2, bh2 + 2, nc);
                    }
                }
                x += bw + (rnd.Next(4) == 0 ? rnd.Next(3) : 0);
            }
        }

        // =====================  utilidades  =====================
        static Color Rgb(int r, int g, int b) { return Color.FromArgb(255, Clamp(r), Clamp(g), Clamp(b)); }
        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }
        static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static readonly float[] BayerM = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        static float Bayer(int x, int y) { return (BayerM[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f; }

        // degradado en "escalones" con tramado de pixeles entre escalon y escalon
        static Color Dither(Color a, Color b, float t, int x, int y, int steps)
        {
            float v = Math.Max(0, Math.Min(1, t)) * steps;
            int lo = (int)v; float frac = v - lo;
            if (frac > Bayer(x, y)) lo++;
            return Mix(a, b, (float)lo / steps);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263;
                n = (n ^ (n >> 13)) * 1274126177;
                return ((n ^ (n >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        static float Noise(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        static float Fbm(float x, float y)
        {
            float sum = 0, amp = 0.5f;
            for (int i = 0; i < 4; i++) { sum += Noise(x, y) * amp; x *= 2.03f; y *= 2.03f; amp *= 0.5f; }
            return sum / 0.9375f;
        }

        // lienzo pequeno de pixeles (rapido: un arreglo de enteros)
        class Canvas
        {
            public readonly int W, H; readonly int[] px;
            public Canvas(int w, int h) { W = w; H = h; px = new int[w * h]; }

            public void Set(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = c.ToArgb(); }

            public void Blend(int x, int y, Color c, float a)
            {
                if (x < 0 || y < 0 || x >= W || y >= H || a <= 0) return;
                a = Math.Min(1, a);
                int i = y * W + x; var d = Color.FromArgb(px[i]);
                int na = Math.Min(255, d.A + (int)(a * 255 * (1 - d.A / 255f)));
                if (d.A == 0) { px[i] = Color.FromArgb((int)(a * 255), c.R, c.G, c.B).ToArgb(); return; }   // capa vacia (resplandor): toma el color tal cual
                px[i] = Color.FromArgb(Math.Max(na, (int)(a * 255)), (int)(d.R + (c.R - d.R) * a), (int)(d.G + (c.G - d.G) * a), (int)(d.B + (c.B - d.B) * a)).ToArgb();
            }

            public void FillRect(int x, int y, int w, int h, Color c) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Set(xx, yy, c); }
            public void Clear(int x, int y, int w, int h) { for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++) for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++) px[yy * W + xx] = 0; }
            public void Rect(int x, int y, int w, int h, Color c) { FillRect(x, y, w, 1, c); FillRect(x, y + h - 1, w, 1, c); FillRect(x, y, 1, h, c); FillRect(x + w - 1, y, 1, h, c); }

            public Bitmap ToBitmap()
            {
                var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                var bd = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < H; y++) Marshal.Copy(px, y * W, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), W);
                b.UnlockBits(bd);
                return b;
            }
        }
    }
}
