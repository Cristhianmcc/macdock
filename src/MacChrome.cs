using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Le pone a cualquier ventana la barra de Mac: quita la barra de Windows y dibuja los tres circulos
    // (cerrar, minimizar, agrandar). La ventana se sigue arrastrando desde arriba, cambiando de tamaño
    // desde las orillas y acomodando a los lados. La ventana solo llama a Draw(g) al final de su OnPaint.
    class MacChrome : NativeWindow
    {
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        readonly Form f; readonly bool light; readonly float s;
        readonly Func<float> barHeight;            // alto de la zona para arrastrar
        readonly Func<Point, bool> interactive;    // partes de esa zona que son botones (no arrastran)
        Point mouse = new Point(-1, -1);
        bool active = true, hover;
        RectangleF lights;

        public MacChrome(Form form, bool lightTheme, float scale, Func<float> barH, Func<Point, bool> isInteractive)
        {
            f = form; light = lightTheme; s = scale; barHeight = barH; interactive = isInteractive;
            f.HandleCreated += (o, e) => { AssignHandle(f.Handle); SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027); };
            f.HandleDestroyed += (o, e) => ReleaseHandle();
            f.Activated += (o, e) => { active = true; f.Invalidate(Rectangle.Ceiling(lights)); };
            f.Deactivate += (o, e) => { active = false; f.Invalidate(Rectangle.Ceiling(lights)); };
            f.MouseMove += (o, e) => Move(e.Location);
            f.MouseLeave += (o, e) => Move(new Point(-1, -1));
            f.MouseUp += (o, e) => { if (e.Button == MouseButtons.Left) Click(e.Location); };
        }

        // donde terminan los circulos (para acomodar el titulo de la ventana a su derecha)
        public float LightsRight { get { return 16 * s + 12 * s * 3 + 8 * s * 2; } }

        void Move(Point p)
        {
            mouse = p;
            bool h = lights.Contains(p);
            if (h != hover) { hover = h; f.Invalidate(Rectangle.Ceiling(lights)); }
            if (h) f.Cursor = Cursors.Default;
        }

        void Click(Point p)
        {
            if (!lights.Contains(p)) return;
            float d = 12 * s, gap = 8 * s, x0 = 16 * s;
            int i = (int)((p.X - x0 + gap / 2) / (d + gap));
            if (i <= 0) f.Close();
            else if (i == 1) f.WindowState = FormWindowState.Minimized;
            else f.WindowState = f.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        public void Draw(Graphics g)
        {
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = 12 * s, gap = 8 * s, x = 16 * s, y = Math.Min(barHeight(), 52 * s) / 2 - d / 2;
            lights = new RectangleF(x - 4 * s, y - 6 * s, d * 3 + gap * 2 + 8 * s, d + 12 * s);
            Color[] cols = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(x + i * (d + gap), y, d, d);
                Color c = active || hover ? cols[i] : (light ? Color.FromArgb(206, 206, 210) : Color.FromArgb(80, 80, 88));
                using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
                using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 0.8f * s)) g.DrawEllipse(pen, r);
                if (!hover) continue;
                float cx = r.X + d / 2, cy = r.Y + d / 2, h = d * 0.22f;
                using (var pen = new Pen(Color.FromArgb(150, 0, 0, 0), 1.3f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (i == 0) { g.DrawLine(pen, cx - h, cy - h, cx + h, cy + h); g.DrawLine(pen, cx + h, cy - h, cx - h, cy + h); }
                    else { g.DrawLine(pen, cx - h * 1.2f, cy, cx + h * 1.2f, cy); if (i == 2) g.DrawLine(pen, cx, cy - h * 1.2f, cx, cy + h * 1.2f); }
                }
            }
            g.SmoothingMode = sm;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x83 && m.WParam != IntPtr.Zero)   // WM_NCCALCSIZE: sin la barra de Windows
            {
                if (f.WindowState == FormWindowState.Maximized)
                {
                    var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    int fr = GetSystemMetrics(32) + GetSystemMetrics(92);
                    r.L += fr; r.T += fr; r.R -= fr; r.B -= fr;
                    Marshal.StructureToPtr(r, m.LParam, false);
                }
                m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == 0x84)   // WM_NCHITTEST
            {
                long lp = m.LParam.ToInt64();
                var p = f.PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                m.Result = (IntPtr)HitTest(p); return;
            }
            base.WndProc(ref m);
        }

        int HitTest(Point p)
        {
            if (f.WindowState != FormWindowState.Maximized)
            {
                int e = (int)(6 * s), W = f.ClientSize.Width, H = f.ClientSize.Height;
                bool l = p.X < e, r = p.X >= W - e, t = p.Y < e, b = p.Y >= H - e;
                if (t && l) return 13; if (t && r) return 14; if (b && l) return 16; if (b && r) return 17;
                if (l) return 10; if (r) return 11; if (t) return 12; if (b) return 15;
            }
            if (p.Y < barHeight() && !lights.Contains(p) && (interactive == null || !interactive(p))) return 2;   // arrastrar
            return 1;
        }
    }
}
