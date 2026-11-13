using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacDock
{
    // Base de las ventanas estilo Mac (Concentracion, Reloj): colores del tema, tarjetas, botones y zonas de clic.
    // Cada ventana dibuja todo en Draw() y registra sus botones con Hit(); un clic ejecuta la accion y se redibuja.
    abstract class MacWindow : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        protected readonly float s; protected readonly bool light;
        readonly List<KeyValuePair<RectangleF, Action>> hits = new List<KeyValuePair<RectangleF, Action>>();
        protected int scroll;

        protected MacWindow(string title, int w, int h)
        {
            s = DeviceDpi / 96f;
            light = SystemLight();
            Text = title; StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true; KeyPreview = true;
            ClientSize = new Size((int)(w * s), (int)(h * s)); MinimumSize = new Size((int)(w * 0.8f * s), (int)(h * 0.8f * s));
            BackColor = Bg;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        static bool SystemLight()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 1; }
            catch { return false; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
        }

        // ---------- colores ----------
        protected Color Bg { get { return light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(30, 30, 36); } }
        protected Color CardBg { get { return light ? Color.FromArgb(250, 250, 253) : Color.FromArgb(44, 44, 52); } }
        protected Color CardBorder { get { return light ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(32, 255, 255, 255); } }
        protected Color Fg { get { return light ? Color.FromArgb(24, 24, 28) : Color.FromArgb(242, 242, 246); } }
        protected Color Sub { get { return light ? Color.FromArgb(118, 118, 128) : Color.FromArgb(156, 156, 168); } }
        protected Color Soft { get { return light ? Color.FromArgb(222, 222, 230) : Color.FromArgb(62, 62, 72); } }
        protected static readonly Color Accent = Color.FromArgb(10, 132, 255), Red = Color.FromArgb(255, 69, 58), Green = Color.FromArgb(52, 199, 89), Tomato = Color.FromArgb(255, 99, 71);

        // ---------- dibujo ----------
        protected abstract void Draw(Graphics g);

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            hits.Clear();
            try { Draw(g); } catch (Exception ex) { Program.Log(ex); }
        }

        protected void Hit(RectangleF r, Action a) { hits.Add(new KeyValuePair<RectangleF, Action>(r, a)); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Key.Contains(e.Location)) { try { hits[i].Value(); } catch (Exception ex) { Program.Log(ex); } Invalidate(); return; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = false; foreach (var h in hits) if (h.Key.Contains(e.Location)) { over = true; break; }
            Cursor = over ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected Font F(float px, bool semi = false) { return new Font(semi ? UiFont.Semi : UiFont.Reg, px * s, FontStyle.Regular, GraphicsUnit.Pixel); }
        protected Font Icons(float px) { return new Font("Segoe MDL2 Assets", px * s, FontStyle.Regular, GraphicsUnit.Pixel); }

        protected void Card(Graphics g, RectangleF r, float rad = 14)
        {
            using (var p = Round(r, rad * s)) { using (var b = new SolidBrush(CardBg)) g.FillPath(b, p); using (var pen = new Pen(CardBorder)) g.DrawPath(pen, p); }
        }

        protected void Txt(Graphics g, string t, Font f, Color c, RectangleF r, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(t ?? "", f, b, r, sf);
        }

        // boton tipo pastilla: primario (azul o del color dado) o normal
        protected void Button(Graphics g, RectangleF r, string text, Action a, bool primary = false, Color? color = null, bool enabled = true)
        {
            Color bg = !enabled ? Soft : primary ? (color ?? Accent) : Soft;
            using (var p = Round(r, r.Height / 2)) using (var b = new SolidBrush(bg)) g.FillPath(b, p);
            using (var f = F(12.5f, primary))
                Txt(g, text, f, !enabled ? Sub : primary ? Color.White : Fg, r, StringAlignment.Center);
            if (enabled && a != null) Hit(r, a);
        }

        // boton redondo con un icono de Segoe MDL2
        protected void IconButton(Graphics g, RectangleF r, string glyph, Action a, Color? bg = null, Color? fg = null)
        {
            using (var b = new SolidBrush(bg ?? Soft)) g.FillEllipse(b, r);
            using (var f = Icons(r.Height * 0.38f / s)) Txt(g, glyph, f, fg ?? Fg, r, StringAlignment.Center);
            if (a != null) Hit(r, a);
        }

        // "−  25 min  +"
        protected void Stepper(Graphics g, RectangleF r, string label, string value, Action minus, Action plus)
        {
            using (var f = F(12.5f)) Txt(g, label, f, Fg, new RectangleF(r.X, r.Y, r.Width - 120 * s, r.Height));
            float bs = 26 * s, y = r.Y + (r.Height - bs) / 2;
            RectangleF rm = new RectangleF(r.Right - 120 * s, y, bs, bs), rp = new RectangleF(r.Right - bs, y, bs, bs);
            IconButton(g, rm, "\uE738", minus);
            IconButton(g, rp, "\uE710", plus);
            using (var f = F(12.5f, true)) Txt(g, value, f, Fg, new RectangleF(rm.Right, r.Y, rp.X - rm.Right, r.Height), StringAlignment.Center);
        }

        // casilla redonda estilo Mac
        protected void Check(Graphics g, RectangleF r, bool on, Action a, Color? color = null)
        {
            if (on) { using (var b = new SolidBrush(color ?? Accent)) g.FillEllipse(b, r); using (var f = Icons(r.Height * 0.5f / s)) Txt(g, "\uE73E", f, Color.White, r, StringAlignment.Center); }
            else using (var pen = new Pen(Sub, 1.4f * s)) g.DrawEllipse(pen, r);
            if (a != null) Hit(RectangleF.Inflate(r, 4 * s, 4 * s), a);
        }

        // control de pestañas (segmented control)
        protected void Segments(Graphics g, RectangleF r, string[] items, int sel, Action<int> pick)
        {
            using (var p = Round(r, 8 * s)) using (var b = new SolidBrush(Soft)) g.FillPath(b, p);
            float w = r.Width / items.Length;
            using (var f = F(12.5f)) using (var fb = F(12.5f, true))
                for (int i = 0; i < items.Length; i++)
                {
                    var cr = new RectangleF(r.X + i * w, r.Y, w, r.Height);
                    if (i == sel) using (var p = Round(RectangleF.Inflate(cr, -2 * s, -2 * s), 6 * s)) using (var b = new SolidBrush(light ? Color.White : Color.FromArgb(92, 92, 104))) g.FillPath(b, p);
                    Txt(g, items[i], i == sel ? fb : f, Fg, cr, StringAlignment.Center);
                    int idx = i; Hit(cr, () => pick(idx));
                }
        }

        protected void Ring(Graphics g, PointF c, float radius, float width, float value, Color color)
        {
            var rr = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var pen = new Pen(light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255), width)) g.DrawEllipse(pen, rr);
            if (value > 0.002f) using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, rr, -90, 360f * Math.Min(1f, value));
        }

        protected TextBox MakeBox(string placeholder)
        {
            var tb = new TextBox
            {
                BorderStyle = BorderStyle.None, Font = new Font(UiFont.Reg, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel),
                BackColor = Soft, ForeColor = Fg
            };
            // texto gris de ayuda cuando esta vacia (EM_SETCUEBANNER)
            tb.HandleCreated += (o, e) => { try { SendMessage(tb.Handle, 0x1501, (IntPtr)1, placeholder); } catch { } };
            Controls.Add(tb);
            return tb;
        }

        protected static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Max(0.1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }
}
