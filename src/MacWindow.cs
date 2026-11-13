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
    // Base de las ventanas estilo Mac (Concentracion, Reloj): barra de titulo propia con los tres circulos
    // (cerrar, minimizar, agrandar), colores del tema, tarjetas, botones con iconos dibujados y zonas de clic.
    // Se sigue pudiendo arrastrar, cambiar de tamaño y acomodar a los lados como cualquier ventana de Windows.
    abstract class MacWindow : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        protected readonly float s; protected readonly bool light;
        readonly List<KeyValuePair<RectangleF, Action>> hits = new List<KeyValuePair<RectangleF, Action>>();
        protected int scroll;
        protected Point mouse = new Point(-1, -1);
        int hoverHit = -1, hoverLight = -1;
        bool active = true;
        RectangleF lights;

        protected float TitleH { get { return 44 * s; } }
        protected virtual bool ShowTitle { get { return true; } }   // false si la ventana pone sus pestañas en la barra

        protected MacWindow(string title, int w, int h)
        {
            s = DeviceDpi / 96f;
            light = SystemLight();
            Text = title; StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true; KeyPreview = true;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min((int)(w * s), wa.Width - 20), Math.Min((int)(h * s), wa.Height - 20));
            MinimumSize = new Size((int)(w * 0.75f * s), Math.Min((int)(h * 0.75f * s), wa.Height - 20));
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
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);   // aplica la ventana sin la barra de Windows
        }

        // ---------- ventana sin la barra de Windows (la dibujamos nosotros, como en Mac) ----------
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x83 && m.WParam != IntPtr.Zero)   // WM_NCCALCSIZE: todo es area de la app
            {
                if (WindowState == FormWindowState.Maximized)
                {
                    var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    int f = GetSystemMetrics(32) + GetSystemMetrics(92);
                    r.L += f; r.T += f; r.R -= f; r.B -= f;
                    Marshal.StructureToPtr(r, m.LParam, false);
                }
                m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == 0x84)   // WM_NCHITTEST: bordes para cambiar tamaño y barra para arrastrar
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                m.Result = (IntPtr)HitTestNC(p); return;
            }
            base.WndProc(ref m);
        }

        int HitTestNC(Point p)
        {
            if (WindowState != FormWindowState.Maximized)
            {
                int e = (int)(6 * s), W = ClientSize.Width, H = ClientSize.Height;
                bool l = p.X < e, r = p.X >= W - e, t = p.Y < e, b = p.Y >= H - e;
                if (t && l) return 13; if (t && r) return 14; if (b && l) return 16; if (b && r) return 17;
                if (l) return 10; if (r) return 11; if (t) return 12; if (b) return 15;
            }
            if (p.Y < TitleH)
            {
                if (lights.Contains(p)) return 1;
                foreach (var h in hits) if (h.Key.Contains(p)) return 1;
                return 2;   // arrastrar; doble clic agranda
            }
            return 1;
        }

        protected override void OnActivated(EventArgs e) { base.OnActivated(e); active = true; Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); active = false; Invalidate(); }

        // ---------- colores ----------
        protected Color Bg { get { return light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(30, 30, 36); } }
        protected Color CardBg { get { return light ? Color.FromArgb(250, 250, 253) : Color.FromArgb(44, 44, 52); } }
        protected Color CardBorder { get { return light ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(32, 255, 255, 255); } }
        protected Color Fg { get { return light ? Color.FromArgb(24, 24, 28) : Color.FromArgb(242, 242, 246); } }
        protected Color Sub { get { return light ? Color.FromArgb(118, 118, 128) : Color.FromArgb(156, 156, 168); } }
        protected Color Soft { get { return light ? Color.FromArgb(222, 222, 230) : Color.FromArgb(62, 62, 72); } }
        protected Color HoverBg { get { return light ? Color.FromArgb(14, 0, 0, 0) : Color.FromArgb(14, 255, 255, 255); } }
        protected static readonly Color Accent = Color.FromArgb(10, 132, 255), Red = Color.FromArgb(255, 69, 58), Green = Color.FromArgb(52, 199, 89),
            Tomato = Color.FromArgb(255, 99, 71), Orange = Color.FromArgb(255, 149, 0);

        // ---------- dibujo ----------
        protected abstract void Draw(Graphics g);
        protected virtual void DrawTitleBar(Graphics g, RectangleF bar) { }   // pestañas o botones en la barra

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            hits.Clear();
            try
            {
                g.Clear(Bg);
                Draw(g);
                var bar = new RectangleF(0, 0, ClientSize.Width, TitleH);
                using (var b = new SolidBrush(Bg)) g.FillRectangle(b, bar);
                using (var pen = new Pen(light ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(70, 0, 0, 0))) g.DrawLine(pen, 0, bar.Bottom - 0.5f, bar.Width, bar.Bottom - 0.5f);
                DrawLights(g);
                if (ShowTitle) using (var f = F(13f, true)) Txt(g, Text, f, active ? Fg : Sub, bar, StringAlignment.Center);
                DrawTitleBar(g, bar);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        // los tres circulos de Mac: rojo cerrar, amarillo minimizar, verde agrandar (iconos al pasar el mouse)
        void DrawLights(Graphics g)
        {
            float d = 12 * s, gap = 8 * s, x = 16 * s, y = (TitleH - d) / 2;
            lights = new RectangleF(x - 4 * s, y - 6 * s, d * 3 + gap * 2 + 8 * s, d + 12 * s);
            bool show = lights.Contains(mouse);
            Color[] cols = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
            string[] icons = { "x", "minus", "plus" };
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(x + i * (d + gap), y, d, d);
                Color c = active || show ? cols[i] : (light ? Color.FromArgb(206, 206, 210) : Color.FromArgb(80, 80, 88));
                using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
                using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 0.8f * s)) g.DrawEllipse(pen, r);
                if (show) Glyph(g, icons[i], RectangleF.Inflate(r, -1.5f * s, -1.5f * s), Color.FromArgb(150, 0, 0, 0), 1.3f);
                int idx = i;
                Hit(RectangleF.Inflate(r, 3 * s, 4 * s), () =>
                {
                    if (idx == 0) Close();
                    else if (idx == 1) WindowState = FormWindowState.Minimized;
                    else WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                });
            }
        }

        protected void Hit(RectangleF r, Action a) { hits.Add(new KeyValuePair<RectangleF, Action>(r, a)); }
        protected bool Hover(RectangleF r) { return r.Contains(mouse); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Key.Contains(e.Location))
                {
                    var a = hits[i].Value;
                    try { a(); } catch (Exception ex) { Program.Log(ex); }
                    if (!IsDisposed) Invalidate();
                    return;
                }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouse = e.Location;
            int over = -1; for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Key.Contains(e.Location)) { over = i; break; }
            int ol = lights.Contains(mouse) ? 1 : 0;
            Cursor = over >= 0 ? Cursors.Hand : Cursors.Default;
            if (over != hoverHit || ol != hoverLight) { hoverHit = over; hoverLight = ol; Invalidate(); }   // efecto al pasar el mouse
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); mouse = new Point(-1, -1); hoverHit = -1; hoverLight = 0; Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected Font F(float px, bool semi = false) { return new Font(semi ? UiFont.Semi : UiFont.Reg, px * s, FontStyle.Regular, GraphicsUnit.Pixel); }

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

        // ---------- iconos dibujados (estilo de los simbolos de Mac, se ven igual en cualquier Windows) ----------
        protected void Glyph(Graphics g, string kind, RectangleF r, Color c, float weight = 1.7f)
        {
            float z = Math.Min(r.Width, r.Height), cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, h = z * 0.28f;
            using (var pen = new Pen(c, weight * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var b = new SolidBrush(c))
                switch (kind)
                {
                    case "x": { float q = h * 0.8f; g.DrawLine(pen, cx - q, cy - q, cx + q, cy + q); g.DrawLine(pen, cx + q, cy - q, cx - q, cy + q); } break;
                    case "minus": g.DrawLine(pen, cx - h, cy, cx + h, cy); break;
                    case "plus": g.DrawLine(pen, cx - h, cy, cx + h, cy); g.DrawLine(pen, cx, cy - h, cx, cy + h); break;
                    case "up": g.DrawLines(pen, new[] { new PointF(cx - h, cy + h * 0.5f), new PointF(cx, cy - h * 0.5f), new PointF(cx + h, cy + h * 0.5f) }); break;
                    case "down": g.DrawLines(pen, new[] { new PointF(cx - h, cy - h * 0.5f), new PointF(cx, cy + h * 0.5f), new PointF(cx + h, cy - h * 0.5f) }); break;
                    case "updown":
                        float k = h * 0.6f;
                        g.DrawLines(pen, new[] { new PointF(cx - k, cy - k * 0.4f), new PointF(cx, cy - k * 1.4f), new PointF(cx + k, cy - k * 0.4f) });
                        g.DrawLines(pen, new[] { new PointF(cx - k, cy + k * 0.4f), new PointF(cx, cy + k * 1.4f), new PointF(cx + k, cy + k * 0.4f) });
                        break;
                    case "check": g.DrawLines(pen, new[] { new PointF(cx - h, cy), new PointF(cx - h * 0.25f, cy + h * 0.75f), new PointF(cx + h, cy - h * 0.7f) }); break;
                    case "play":
                        { float a = z * 0.2f; var pts = new[] { new PointF(cx - a * 0.75f, cy - a), new PointF(cx + a, cy), new PointF(cx - a * 0.75f, cy + a) };
                          using (var gp = new GraphicsPath()) { gp.AddPolygon(pts); g.FillPath(b, gp); using (var pr = new Pen(c, z * 0.08f) { LineJoin = LineJoin.Round }) g.DrawPath(pr, gp); } }
                        break;
                    case "pause":
                        { float bw = z * 0.09f, bh = z * 0.36f;
                          using (var p1 = Round(new RectangleF(cx - bw * 1.8f, cy - bh / 2, bw, bh), bw / 2)) g.FillPath(b, p1);
                          using (var p2 = Round(new RectangleF(cx + bw * 0.8f, cy - bh / 2, bw, bh), bw / 2)) g.FillPath(b, p2); }
                        break;
                    case "skip":
                        { float a = z * 0.18f; var pts = new[] { new PointF(cx - a * 1.1f, cy - a), new PointF(cx + a * 0.6f, cy), new PointF(cx - a * 1.1f, cy + a) };
                          using (var gp = new GraphicsPath()) { gp.AddPolygon(pts); g.FillPath(b, gp); }
                          g.DrawLine(pen, cx + a * 0.95f, cy - a, cx + a * 0.95f, cy + a); }
                        break;
                    case "reset":
                        { float rr = z * 0.2f; var arc = new RectangleF(cx - rr, cy - rr, rr * 2, rr * 2);
                          g.DrawArc(pen, arc, -80, 300);   // flecha circular (contra reloj) con la punta arriba
                          var tip = new PointF(cx + (float)Math.Cos(-80 * Math.PI / 180) * rr, cy + (float)Math.Sin(-80 * Math.PI / 180) * rr);
                          float ah = z * 0.1f;
                          g.FillPolygon(b, new[] { new PointF(tip.X - ah * 1.2f, tip.Y), new PointF(tip.X + ah * 0.4f, tip.Y - ah * 1.1f), new PointF(tip.X + ah * 0.4f, tip.Y + ah * 1.1f) }); }
                        break;
                    default:
                        using (var f = new Font("Segoe MDL2 Assets", z * 0.45f, FontStyle.Regular, GraphicsUnit.Pixel)) Txt(g, kind, f, c, r, StringAlignment.Center);
                        break;
                }
        }

        // boton tipo pastilla: primario (azul o del color dado) o normal; se aclara al pasar el mouse
        protected void Button(Graphics g, RectangleF r, string text, Action a, bool primary = false, Color? color = null, bool enabled = true)
        {
            Color bg = !enabled ? Soft : primary ? (color ?? Accent) : Soft;
            if (enabled && Hover(r)) bg = primary ? ControlPaint.Light(bg, 0.15f) : (light ? Color.FromArgb(210, 210, 220) : Color.FromArgb(76, 76, 88));
            using (var p = Round(r, r.Height / 2)) using (var b = new SolidBrush(bg)) g.FillPath(b, p);
            using (var f = F(12.5f, primary))
                Txt(g, text, f, !enabled ? Sub : primary ? Color.White : Fg, r, StringAlignment.Center);
            if (enabled && a != null) Hit(r, a);
        }

        // boton redondo con un icono
        protected void IconButton(Graphics g, RectangleF r, string glyph, Action a, Color? bg = null, Color? fg = null)
        {
            Color c = bg ?? Soft;
            if (a != null && Hover(r)) c = bg.HasValue ? ControlPaint.Light(c, 0.15f) : (light ? Color.FromArgb(206, 206, 216) : Color.FromArgb(80, 80, 92));
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            Glyph(g, glyph, r, fg ?? Fg);
            if (a != null) Hit(r, a);
        }

        // casilla redonda (como en Recordatorios)
        protected void Check(Graphics g, RectangleF r, bool on, Action a, Color? color = null)
        {
            Color c = color ?? Accent;
            if (on) { using (var b = new SolidBrush(c)) g.FillEllipse(b, r); Glyph(g, "check", RectangleF.Inflate(r, -2 * s, -2 * s), Color.White, 1.8f); }
            else using (var pen = new Pen(Hover(RectangleF.Inflate(r, 4 * s, 4 * s)) ? c : Sub, 1.4f * s)) g.DrawEllipse(pen, r);
            if (a != null) Hit(RectangleF.Inflate(r, 4 * s, 4 * s), a);
        }

        // interruptor verde de Mac
        protected void Switch(Graphics g, RectangleF r, bool on, Action a)
        {
            using (var p = Round(r, r.Height / 2)) using (var b = new SolidBrush(on ? Green : Soft)) g.FillPath(b, p);
            float k = r.Height - 4 * s;
            using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(b, (on ? r.Right - k - 2 * s : r.X + 2 * s), r.Y + 2.6f * s, k, k);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, on ? r.Right - k - 2 * s : r.X + 2 * s, r.Y + 2 * s, k, k);
            if (a != null) Hit(r, a);
        }

        // fila de ajustes con menu desplegable, como en Ajustes del Sistema: "Concentración      25 min ⌃⌄"
        protected void PopupRow(Graphics g, RectangleF r, string label, string value, string[] options, int selected, Action<int> pick)
        {
            using (var f = F(13f)) Txt(g, label, f, Fg, r);
            float w;
            using (var f = F(13f)) w = g.MeasureString(value, f).Width + 34 * s;
            var pr = new RectangleF(r.Right - w, r.Y + (r.Height - 26 * s) / 2, w, 26 * s);
            Color bg = Hover(pr) ? (light ? Color.FromArgb(214, 214, 224) : Color.FromArgb(78, 78, 90)) : (light ? Color.White : Color.FromArgb(64, 64, 74));
            using (var p = Round(pr, 6 * s)) { using (var b = new SolidBrush(bg)) g.FillPath(b, p); using (var pen = new Pen(CardBorder)) g.DrawPath(pen, p); }
            using (var f = F(13f)) Txt(g, value, f, Fg, new RectangleF(pr.X + 10 * s, pr.Y, pr.Width - 30 * s, pr.Height));
            var chip = new RectangleF(pr.Right - 22 * s, pr.Y + 4 * s, 18 * s, pr.Height - 8 * s);
            using (var p = Round(chip, 4 * s)) using (var b = new SolidBrush(Accent)) g.FillPath(b, p);
            Glyph(g, "updown", chip, Color.White, 1.4f);
            Hit(pr, () =>
            {
                var m = MenuStyle.New(light);
                for (int i = 0; i < options.Length; i++)
                {
                    int idx = i;
                    var mi = new ToolStripMenuItem(options[i]) { Checked = i == selected };
                    mi.Click += (o, e) => { try { pick(idx); } catch (Exception ex) { Program.Log(ex); } Invalidate(); };
                    m.Items.Add(mi);
                }
                m.Show(this, Point.Round(new PointF(pr.X, pr.Bottom + 2 * s)));
            });
        }

        // control de pestañas (segmented control)
        protected void Segments(Graphics g, RectangleF r, string[] items, int sel, Action<int> pick, Color? selColor = null)
        {
            using (var p = Round(r, 8 * s)) using (var b = new SolidBrush(Soft)) g.FillPath(b, p);
            float w = r.Width / items.Length;
            using (var f = F(12.5f)) using (var fb = F(12.5f, true))
                for (int i = 0; i < items.Length; i++)
                {
                    var cr = new RectangleF(r.X + i * w, r.Y, w, r.Height);
                    var inner = RectangleF.Inflate(cr, -2 * s, -2 * s);
                    if (i == sel) using (var p = Round(inner, 6 * s)) using (var b = new SolidBrush(selColor ?? (light ? Color.White : Color.FromArgb(92, 92, 104)))) g.FillPath(b, p);
                    else if (Hover(cr)) using (var p = Round(inner, 6 * s)) using (var b = new SolidBrush(HoverBg)) g.FillPath(b, p);
                    Txt(g, items[i], i == sel ? fb : f, i == sel && selColor.HasValue ? Color.White : Fg, cr, StringAlignment.Center);
                    int idx = i; Hit(cr, () => pick(idx));
                }
        }

        protected void Ring(Graphics g, PointF c, float radius, float width, float value, Color color)
        {
            var rr = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var pen = new Pen(light ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(36, 255, 255, 255), width)) g.DrawEllipse(pen, rr);
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
