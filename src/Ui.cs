using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Menus estilo Mac: fondo oscuro (o claro), esquinas redondeadas, letra Inter y la opcion elegida en azul.
    static class MenuStyle
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        public static readonly Font MenuFont = new Font(UiFont.Reg, 9.75f);
        public static readonly Font MenuFontBold = new Font(UiFont.Reg, 9.75f, FontStyle.Bold);

        public static ContextMenuStrip New(bool light)
        {
            var m = new ContextMenuStrip();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
            m.Renderer = new MacRenderer(light);
            m.Font = MenuFont;
            m.Padding = new Padding(5, 5, 5, 5);
            m.DropShadowEnabled = true;
            Hook(m, light);
            return m;
        }

        // aplica el estilo a un menu (y a sus submenus cuando se abren)
        public static void Hook(ToolStripDropDown d, bool light)
        {
            d.Opening += (o, e) =>
            {
                foreach (ToolStripItem it in d.Items)
                {
                    it.Padding = new Padding(6, 4, 14, 4);
                    var mi = it as ToolStripMenuItem;
                    if (mi != null && mi.HasDropDownItems)
                    {
                        var dd = mi.DropDown as ToolStripDropDownMenu;
                        if (dd != null)
                        {
                            dd.Renderer = d.Renderer; dd.Font = d.Font; dd.Padding = d.Padding;
                            dd.ShowImageMargin = false; dd.ShowCheckMargin = true;
                            Hook(dd, light);
                        }
                    }
                }
            };
            d.Opened += (o, e) => Round(d.Handle, light);
        }

        public static void Round(IntPtr h, bool light)
        {
            try
            {
                int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4);
                int dark = light ? 0 : 1; DwmSetWindowAttribute(h, 20, ref dark, 4);
                int border = light ? 0x00D0D0D0 : 0x00404046; DwmSetWindowAttribute(h, 34, ref border, 4);
            }
            catch { }
        }
    }

    // Botones de ventana estilo Mac: cerrar (rojo), minimizar (amarillo), maximizar (verde).
    // Al pasar el mouse por el grupo muestran sus simbolos, como en macOS.
    static class TrafficLights
    {
        public static float D(float s) { return 12.5f * s; }        // diametro
        public static float Step(float s) { return 20f * s; }       // distancia entre centros
        public static float Width(float s) { return Step(s) * 2 + D(s); }

        // index: 0 cerrar, 1 minimizar, 2 maximizar; -1 nada
        public static int Hit(PointF p, float x, float y, float s)
        {
            for (int i = 0; i < 3; i++)
            {
                float cx = x + D(s) / 2 + i * Step(s), cy = y + D(s) / 2;
                float dx = p.X - cx, dy = p.Y - cy;
                if (dx * dx + dy * dy <= (D(s) / 2 + 3 * s) * (D(s) / 2 + 3 * s)) return i;
            }
            return -1;
        }

        public static void Draw(Graphics g, float x, float y, float s, bool active, bool glyphs, bool light)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color[] fill = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
            Color[] edge = { Color.FromArgb(224, 68, 62), Color.FromArgb(222, 161, 35), Color.FromArgb(26, 171, 41) };
            Color[] sym = { Color.FromArgb(120, 10, 6), Color.FromArgb(150, 87, 0), Color.FromArgb(0, 100, 0) };
            Color off = light ? Color.FromArgb(206, 206, 210) : Color.FromArgb(78, 78, 84);
            float d = D(s);
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(x + i * Step(s), y, d, d);
                bool on = active || glyphs;
                using (var b = new SolidBrush(on ? fill[i] : off)) g.FillEllipse(b, r);
                using (var p = new Pen(on ? edge[i] : Color.FromArgb(40, 0, 0, 0), Math.Max(0.8f, 0.6f * s))) g.DrawEllipse(p, r);
                if (!glyphs) continue;
                float cx = r.X + d / 2, cy = r.Y + d / 2, k = d * 0.24f;
                using (var p = new Pen(sym[i], Math.Max(1.1f, 1.25f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var b = new SolidBrush(sym[i]))
                {
                    if (i == 0) { g.DrawLine(p, cx - k, cy - k, cx + k, cy + k); g.DrawLine(p, cx - k, cy + k, cx + k, cy - k); }
                    else if (i == 1) g.DrawLine(p, cx - k * 1.25f, cy, cx + k * 1.25f, cy);
                    else
                    {   // dos triangulos en diagonal (pantalla completa de Mac)
                        float q = k * 1.15f;
                        g.FillPolygon(b, new[] { new PointF(cx - q, cy - q), new PointF(cx + q * 0.35f, cy - q), new PointF(cx - q, cy + q * 0.35f) });
                        g.FillPolygon(b, new[] { new PointF(cx + q, cy + q), new PointF(cx - q * 0.35f, cy + q), new PointF(cx + q, cy - q * 0.35f) });
                    }
                }
            }
            g.SmoothingMode = old;
        }
    }

    // Ventana sin borde de Windows que se mueve, redimensiona y maximiza como una ventana normal (para poner los botones de Mac)
    static class Borderless
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public static void BeginDrag(Form f)
        {
            ReleaseCapture();
            SendMessage(f.Handle, 0xA1, new IntPtr(2), IntPtr.Zero);   // WM_NCLBUTTONDOWN, HTCAPTION: Windows mueve la ventana (y la acomoda con Snap)
        }

        static readonly System.Collections.Generic.Dictionary<IntPtr, Rectangle> restore = new System.Collections.Generic.Dictionary<IntPtr, Rectangle>();
        public static bool IsMax(Form f) { return f.Bounds == Screen.FromHandle(f.Handle).WorkingArea; }
        public static void ToggleMax(Form f)
        {
            var wa = Screen.FromHandle(f.Handle).WorkingArea;
            if (IsMax(f))
            {
                Rectangle r;
                f.Bounds = restore.TryGetValue(f.Handle, out r) ? r : new Rectangle(wa.Left + wa.Width / 8, wa.Top + wa.Height / 8, wa.Width * 3 / 4, wa.Height * 3 / 4);
            }
            else { restore[f.Handle] = f.Bounds; f.Bounds = wa; }
        }

        // bordes para redimensionar (se llama desde WndProc con WM_NCHITTEST)
        public static bool HitTest(Form f, ref Message m, int grip)
        {
            if (m.Msg != 0x84 || IsMax(f)) return false;
            long lp = m.LParam.ToInt64();
            var p = f.PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            bool l = p.X < grip, r = p.X >= f.ClientSize.Width - grip, t = p.Y < grip, b = p.Y >= f.ClientSize.Height - grip;
            int ht = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
            if (ht == 0) return false;
            m.Result = new IntPtr(ht);
            return true;
        }
    }

    class MacRenderer : ToolStripProfessionalRenderer
    {
        readonly bool light;
        readonly Color bg, fg, dim, sel, sep;

        public MacRenderer(bool lightTheme) : base(new ProfessionalColorTable())
        {
            light = lightTheme;
            bg = light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(40, 40, 44);
            fg = light ? Color.FromArgb(28, 28, 30) : Color.FromArgb(236, 236, 240);
            dim = light ? Color.FromArgb(150, 150, 156) : Color.FromArgb(120, 120, 128);
            sel = Color.FromArgb(10, 122, 255);
            sep = light ? Color.FromArgb(222, 222, 226) : Color.FromArgb(62, 62, 68);
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using (var p = Round(r, 5)) using (var b = new SolidBrush(sel)) g.FillPath(b, p);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? dim : (e.Item.Selected ? Color.White : fg);
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(sep)) e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && e.Item.Selected ? Color.White : dim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            using (var p = new Pen(e.Item.Selected ? Color.White : fg, 1.8f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLines(p, new[] {
                    new PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.76f),
                    new PointF(r.Left + r.Width * 0.84f, r.Top + r.Height * 0.26f) });
            }
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    // Icono de bateria estilo Mac. Con el cargador conectado muestra un rayo (se usa en la barra superior, el dock y el widget).
    static class BatteryGlyph
    {
        public struct State { public float Pct; public bool Plugged, Charging, HasBattery; }

        public static State Read()
        {
            var ps = SystemInformation.PowerStatus;
            var st = new State();
            st.HasBattery = ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryLifePercent >= 0 && ps.BatteryLifePercent <= 1;
            st.Pct = st.HasBattery ? ps.BatteryLifePercent : 0;
            st.Plugged = ps.PowerLineStatus == PowerLineStatus.Online;
            st.Charging = (ps.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
            return st;
        }

        // body: el cuerpo de la bateria (sin el polo). fg: color del relleno; frame: contorno; cut: color para recortar el rayo (el del fondo).
        public static void Draw(Graphics g, RectangleF body, State st, Color fg, Color frame, Color cut, float s)
        {
            float rad = body.Height * 0.28f;
            using (var pen = new Pen(frame, Math.Max(1f, body.Height * 0.1f)))
            using (var path = Round(body, rad)) g.DrawPath(pen, path);
            using (var b = new SolidBrush(frame))
                g.FillRectangle(b, body.Right + body.Height * 0.08f, body.Y + body.Height * 0.3f, body.Height * 0.15f, body.Height * 0.4f);

            Color fill = st.Pct <= 0.2f && !st.Plugged ? Color.FromArgb(255, 69, 58) : (st.Plugged ? Color.FromArgb(52, 199, 89) : fg);
            float inset = body.Height * 0.18f;
            var inner = new RectangleF(body.X + inset, body.Y + inset, Math.Max(inset, (body.Width - inset * 2) * st.Pct), body.Height - inset * 2);
            using (var b = new SolidBrush(fill)) using (var path = Round(inner, rad * 0.5f)) g.FillPath(b, path);

            if (st.Plugged)
            {
                // rayo centrado, un poco mas alto que la bateria, con borde del color del fondo para que se lea encima del relleno
                float h = body.Height * 1.25f, w = h * 0.62f;
                float cx = body.X + body.Width / 2f, top = body.Y + (body.Height - h) / 2f;
                var pts = new[] {
                    new PointF(cx + w * 0.12f, top), new PointF(cx - w * 0.5f, top + h * 0.56f), new PointF(cx - w * 0.02f, top + h * 0.56f),
                    new PointF(cx - w * 0.14f, top + h), new PointF(cx + w * 0.5f, top + h * 0.42f), new PointF(cx + w * 0.02f, top + h * 0.42f) };
                using (var path = new GraphicsPath()) { path.AddPolygon(pts);
                    using (var pen = new Pen(cut, Math.Max(1.2f, body.Height * 0.2f)) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                    using (var b = new SolidBrush(fg)) g.FillPath(b, path); }
            }
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Max(0.1f, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }
}
