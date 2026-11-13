using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacDock
{
    // Centro de Notificaciones estilo macOS Big Sur: clic en la fecha y hora de la barra superior y entra un panel desde la derecha
    // con el dia de hoy y los widgets (calendario, reloj, bateria, sistema y notas).
    // Windows no deja que otros programas lean tus notificaciones, asi que hay un boton para abrir las de Windows (Win + N)
    // y un interruptor de "No molestar".
    class NotificationCenter : Form
    {
        static NotificationCenter current; static DateTime closedAt = DateTime.MinValue;

        public static void Toggle(float s, bool light, Rectangle work)
        {
            if (current != null && !current.IsDisposed) { current.SlideOut(); return; }
            if ((DateTime.Now - closedAt).TotalMilliseconds < 250) return;
            current = new NotificationCenter(s, light, work);
            current.FormClosed += (o, e) => { closedAt = DateTime.Now; current = null; GC.Collect(); };
            current.Show(); current.Activate(); N.SetForegroundWindow(current.Handle);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        readonly float s; readonly bool light;
        readonly List<KeyValuePair<DesktopWidget, RectangleF>> widgets = new List<KeyValuePair<DesktopWidget, RectangleF>>();
        readonly Timer tick = new Timer { Interval = 1000 }, anim = new Timer { Interval = 12 };
        RectangleF rNotif, rNotifBtn, rDnd, rNotifCfg;
        RectangleF rMusic, rPrev, rPlay, rNext;   // tarjeta de "que esta sonando"
        NowPlayingInfo music;
        bool dnd, closing; float slide;   // 0 = fuera de la pantalla, 1 = en su lugar
        int restX, hidX;

        NotificationCenter(float scale, bool lightTheme, Rectangle workArea)
        {
            s = scale; light = lightTheme;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
            BackColor = light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(30, 30, 36);

            float pad = 14 * s, gap = DesktopWidget.GapPx * s, small = DesktopWidget.SmallPx * s, wide = small * 2 + gap;
            int w = (int)(wide + pad * 2), margin = (int)(10 * s);
            float y = pad + 40 * s;
            rNotif = new RectangleF(pad, y, wide, 92 * s);
            rNotifBtn = new RectangleF(pad + 14 * s, rNotif.Bottom - 40 * s, 170 * s, 28 * s);
            rDnd = new RectangleF(rNotif.Right - 14 * s - 130 * s, rNotif.Bottom - 40 * s, 130 * s, 28 * s);
            rNotifCfg = new RectangleF(rNotif.Right - 14 * s - 84 * s, rNotif.Y + 9 * s, 84 * s, 22 * s);
            y = rNotif.Bottom + gap;
            rMusic = new RectangleF(pad, y, wide, 84 * s);
            float bsz = 32 * s, by = rMusic.Y + (rMusic.Height - bsz) / 2;
            rNext = new RectangleF(rMusic.Right - 12 * s - bsz, by, bsz, bsz);
            rPlay = new RectangleF(rNext.X - 6 * s - bsz, by, bsz, bsz);
            rPrev = new RectangleF(rPlay.X - 6 * s - bsz, by, bsz, bsz);
            y = rMusic.Bottom + gap;
            music = NowPlaying.Last;

            // los widgets, en el orden de Mac: calendario arriba, luego en pares
            bool laptop = !DeviceProfile.IsDesktop && BatteryGlyph.Read().HasBattery;
            var layout = new List<string[]> { new[] { "calendar" }, new[] { "focus", "clock" }, laptop ? new[] { "battery", "system" } : new[] { "system", "notes" }, laptop ? new[] { "notes" } : new string[0] };
            foreach (var row in layout)
            {
                if (y + small > workArea.Height - pad) break;   // pantallas bajitas: los que quepan
                float x = pad;
                foreach (var kind in row)
                {
                    var wd = DesktopWidget.Create(kind, s, light);
                    if (wd == null) continue;
                    var cs = wd.CardSize;
                    widgets.Add(new KeyValuePair<DesktopWidget, RectangleF>(wd, new RectangleF(x, y, cs.Width, cs.Height)));
                    wd.UpdateData();
                    x += cs.Width + gap;
                }
                y += small + gap;
            }
            int h = (int)Math.Min(workArea.Height - margin * 2, y - gap + pad);
            restX = workArea.Right - w - margin; hidX = workArea.Right + 4;
            Bounds = new Rectangle(hidX, workArea.Top + margin, w, h);
            dnd = ReadDnd();
            tick.Tick += (o, e) => RefreshMusic();
            tick.Tick += (o, e) => { bool changed = false; foreach (var kv in widgets) { var k = kv.Key.Tag as string; var nk = kv.Key.UpdateData(); if (nk != k) { kv.Key.Tag = nk; changed = true; } } if (changed) Invalidate(); };
            anim.Tick += (o, e) => Step();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            int border = light ? 0x00CFCFD6 : 0x00484852; DwmSetWindowAttribute(Handle, 34, ref border, 4);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); anim.Start(); tick.Start(); RefreshMusic(); }

        void RefreshMusic()
        {
            NowPlaying.Refresh(info =>
            {
                try { BeginInvoke(new Action(() => { if (IsDisposed) return; bool changed = music == null || music.Key != info.Key; music = info; if (changed) Invalidate(); })); } catch { }
            });
        }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); SlideOut(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) SlideOut(); }

        // entra deslizandose desde la orilla derecha, como en Mac
        void Step()
        {
            float target = closing ? 0f : 1f;
            slide += (target - slide) * 0.32f;
            if (Math.Abs(target - slide) < 0.01f) slide = target;
            float e = 1f - (1f - slide) * (1f - slide);
            Left = (int)(hidX + (restX - hidX) * e);
            if (slide == target) { anim.Stop(); if (closing) Close(); }
        }

        public void SlideOut() { if (closing || IsDisposed) return; closing = true; anim.Start(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop(); tick.Dispose(); anim.Stop(); anim.Dispose();
            foreach (var kv in widgets) try { kv.Key.Dispose(); } catch { }
            base.OnFormClosed(e);
        }

        Color CardBg { get { return light ? Color.FromArgb(250, 250, 253) : Color.FromArgb(44, 44, 52); } }
        Color CardBorder { get { return light ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(32, 255, 255, 255); } }
        Color Fg { get { return light ? Color.FromArgb(24, 24, 28) : Color.FromArgb(242, 242, 246); } }
        Color Sub { get { return light ? Color.FromArgb(118, 118, 128) : Color.FromArgb(156, 156, 168); } }

        static readonly string[] Days = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };
        static readonly string[] Months = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float pad = 14 * s;
            var now = DateTime.Now;
            using (var fBig = new Font(UiFont.Semi, 17f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fTitle = new Font(UiFont.Semi, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fSub = new Font(UiFont.Reg, 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fgB = new SolidBrush(Fg)) using (var subB = new SolidBrush(Sub))
            {
                // encabezado: "viernes, 13 de noviembre"
                string today = char.ToUpperInvariant(Days[(int)now.DayOfWeek][0]) + Days[(int)now.DayOfWeek].Substring(1) + ", " + now.Day + " de " + Months[now.Month - 1];
                g.DrawString(today, fBig, fgB, pad + 2 * s, pad + 4 * s);

                // tarjeta de notificaciones
                Card(g, rNotif, 18 * s);
                g.DrawString("Notificaciones", fTitle, fgB, rNotif.X + 14 * s, rNotif.Y + 12 * s);
                using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                using (var ab = new SolidBrush(Color.FromArgb(10, 132, 255))) g.DrawString("Configurar", fSub, ab, rNotifCfg, sf);   // como Ajustes > Notificaciones de Mac
                g.DrawString(dnd ? "No molestar está activado: tus apps no harán sonido." : "Tus avisos de apps están en el centro de Windows.", fSub, subB,
                             new RectangleF(rNotif.X + 14 * s, rNotif.Y + 31 * s, rNotif.Width - 28 * s, 18 * s), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                Pill(g, rNotifBtn, "Ver notificaciones", false, fSub);
                Pill(g, rDnd, dnd ? "No molestar  ●" : "No molestar", dnd, fSub);

                DrawMusic(g, fTitle, fSub);
                foreach (var kv in widgets) kv.Key.PaintInto(g, kv.Value, CardBg, CardBorder);
            }
        }

        // Tarjeta de musica: portada (o icono de la app), cancion, artista · app y controles
        void DrawMusic(Graphics g, Font fTitle, Font fSub)
        {
            Card(g, rMusic, 18 * s);
            bool has = music != null && music.HasSession && music.Title != "";
            var art = new RectangleF(rMusic.X + 12 * s, rMusic.Y + 12 * s, rMusic.Height - 24 * s, rMusic.Height - 24 * s);
            using (var p = Round(art, 10 * s))
            {
                if (has && music.Art != null)
                {
                    var st = g.Save(); g.SetClip(p); g.DrawImage(music.Art, art); g.Restore(st);
                }
                else
                {
                    using (var b = new SolidBrush(light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(62, 62, 72))) g.FillPath(b, p);
                    if (has && music.AppIcon != null) g.DrawImage(music.AppIcon, RectangleF.Inflate(art, -art.Width * 0.24f, -art.Height * 0.24f));
                    else using (var f = new Font("Segoe MDL2 Assets", 20f * s, FontStyle.Regular, GraphicsUnit.Pixel)) using (var b = new SolidBrush(Sub))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString("\uE8D6", f, b, art, sf);   // nota musical
                }
            }
            float tx = art.Right + 12 * s, tw = rPrev.X - 8 * s - tx;
            var sfT = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var fgB = new SolidBrush(Fg)) using (var subB = new SolidBrush(Sub))
            {
                if (has)
                {
                    g.DrawString(music.Title, fTitle, fgB, new RectangleF(tx, rMusic.Y + 20 * s, tw, 20 * s), sfT);
                    string sub = music.Artist != "" && music.App != "" ? music.Artist + " · " + music.App : (music.Artist != "" ? music.Artist : music.App);
                    g.DrawString(sub, fSub, subB, new RectangleF(tx, rMusic.Y + 42 * s, tw, 18 * s), sfT);
                }
                else
                {
                    g.DrawString("Nada sonando", fTitle, fgB, new RectangleF(tx, rMusic.Y + 20 * s, tw, 20 * s), sfT);
                    g.DrawString("Spotify, YouTube Music, el navegador...", fSub, subB, new RectangleF(tx, rMusic.Y + 42 * s, tw, 18 * s), sfT);
                }
            }
            using (var f = new Font("Segoe MDL2 Assets", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                Color bc = light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(62, 62, 72);
                foreach (var pr in new[] { new KeyValuePair<RectangleF, string>(rPrev, "\uE892"), new KeyValuePair<RectangleF, string>(rPlay, has && music.Playing ? "\uE769" : "\uE768"), new KeyValuePair<RectangleF, string>(rNext, "\uE893") })
                {
                    using (var b = new SolidBrush(bc)) g.FillEllipse(b, pr.Key);
                    using (var b = new SolidBrush(has ? Fg : Sub)) g.DrawString(pr.Value, f, b, pr.Key, sf);
                }
            }
        }

        void Pill(Graphics g, RectangleF r, string t, bool on, Font f)
        {
            Color bg = on ? Color.FromArgb(94, 92, 230) : (light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(62, 62, 72));
            using (var p = Round(r, r.Height / 2)) using (var b = new SolidBrush(bg)) g.FillPath(b, p);
            using (var b = new SolidBrush(on ? Color.White : Fg)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(t, f, b, r, sf);
        }

        void Card(Graphics g, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) { using (var b = new SolidBrush(CardBg)) g.FillPath(b, p); using (var pen = new Pen(CardBorder)) g.DrawPath(pen, p); }
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Max(0.1f, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hand = rNotifBtn.Contains(e.Location) || rNotifCfg.Contains(e.Location) || rDnd.Contains(e.Location) || rPrev.Contains(e.Location) || rPlay.Contains(e.Location) || rNext.Contains(e.Location) || widgets.Any(kv => kv.Value.Contains(e.Location) && (kv.Key is SystemWidget || kv.Key is NotesWidget || kv.Key is CalendarWidget));
            Cursor = hand ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            foreach (var kv in widgets)
                if (kv.Value.Contains(e.Location)) { kv.Key.Wheel(new PointF(e.X - kv.Value.X, e.Y - kv.Value.Y), e.Delta); Invalidate(); return; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            try
            {
                if (rNotifBtn.Contains(e.Location)) { SlideOut(); N.PressKeys(0x5B, 0x4E); return; }   // Win + N
                if (rNotifCfg.Contains(e.Location)) { SlideOut(); System.Diagnostics.Process.Start("ms-settings:notifications"); return; }
                Action again = () => RefreshMusic();
                if (rPlay.Contains(e.Location)) { if (music != null) { music.Playing = !music.Playing; Invalidate(); } NowPlaying.PlayPause(again); return; }
                if (rPrev.Contains(e.Location)) { NowPlaying.Prev(again); return; }
                if (rNext.Contains(e.Location)) { NowPlaying.Next(again); return; }
                if (rDnd.Contains(e.Location))
                {
                    dnd = !dnd;
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                        if (k != null) k.SetValue("NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", dnd ? 0 : 1, RegistryValueKind.DWord);
                    Invalidate(); return;
                }
                foreach (var kv in widgets)
                    if (kv.Value.Contains(e.Location))
                    {
                        var p = PointToScreen(Point.Round(kv.Value.Location));
                        kv.Key.Pos = new Point(p.X - (int)(kv.Value.Width + 330 * s), p.Y);   // la ventanita de Notas sale a la izquierda del panel
                        if (kv.Key is SystemWidget || kv.Key is NotesWidget) SlideOut();
                        kv.Key.ClickAt(new PointF(e.X - kv.Value.X, e.Y - kv.Value.Y));
                        Invalidate();   // el calendario pudo cambiar de mes
                        return;
                    }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        static bool ReadDnd()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                    return k != null && Convert.ToInt32(k.GetValue("NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 1)) == 0;
            }
            catch { return false; }
        }
    }
}
