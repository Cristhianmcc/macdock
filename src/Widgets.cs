using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Widgets del escritorio estilo macOS: reloj, calendario, bateria, sistema (CPU y RAM) y notas.
    // Viven "pegados" al escritorio: detras de tus ventanas y siguen visibles con Win + D.
    // Arrastralos para moverlos (se guarda donde los dejas). Clic derecho para quitar o agregar otros.
    // No usan internet. Cada uno se redibuja solo cuando cambia lo que muestra.
    static class Widgets
    {
        public static readonly string[] Kinds = { "clock", "calendar", "battery", "system", "notes" };
        static readonly string[] DefaultOn = { "clock", "battery", "calendar", "system" };

        public static string NameOf(string kind)
        {
            switch (kind)
            {
                case "clock": return "Reloj";
                case "calendar": return "Calendario";
                case "battery": return "Batería";
                case "system": return "Sistema (CPU y RAM)";
                case "notes": return "Notas";
            }
            return kind;
        }

        class Saved { public bool On; public Point? Pos; }

        static readonly List<DesktopWidget> live = new List<DesktopWidget>();
        static Dictionary<string, Saved> saved;
        static float s = 1f; static bool light, running;
        static string IniPath { get { return Path.Combine(Program.DataDir, "widgets.ini"); } }
        public static string NotesPath { get { return Path.Combine(Program.DataDir, "notas.txt"); } }

        public static bool IsOn(string kind) { Load(); Saved v; return saved.TryGetValue(kind, out v) && v.On; }

        public static void Start(float scale, bool lightTheme)
        {
            if (running) Stop();
            s = scale; light = lightTheme; running = true;
            Load();
            foreach (var k in Kinds) if (IsOn(k)) Open(k);
        }

        public static void Stop()
        {
            running = false;
            foreach (var w in live.ToList()) try { w.Close(); w.Dispose(); } catch { }
            live.Clear();
        }

        // El Explorador se reinicio: hay que volver a pegarlos al escritorio nuevo.
        public static void Restart() { if (!running) return; Stop(); Start(s, light); }

        // Con un juego o video a pantalla completa no se ven: se detienen sus relojes para no gastar nada.
        static bool paused;
        public static void SetPaused(bool p)
        {
            if (p == paused) return;
            paused = p;
            foreach (var w in live) w.SetPaused(p);
        }

        public static void SetTheme(bool lightTheme)
        {
            light = lightTheme;
            foreach (var w in live) w.SetTheme(light);
        }

        public static void SetEnabled(string kind, bool on)
        {
            Load();
            Saved v; if (!saved.TryGetValue(kind, out v)) saved[kind] = v = new Saved();
            v.On = on; Save();
            if (!running) return;
            var cur = live.FirstOrDefault(w => w.Kind == kind);
            if (on && cur == null) Open(kind);
            else if (!on && cur != null) { live.Remove(cur); cur.Close(); cur.Dispose(); }
        }

        public static void ResetPositions()
        {
            Load();
            foreach (var v in saved.Values) v.Pos = null;
            Save();
            if (running) { Stop(); Start(s, light); }
        }

        internal static void Moved(DesktopWidget w)
        {
            Load();
            Saved v; if (!saved.TryGetValue(w.Kind, out v)) saved[w.Kind] = v = new Saved { On = true };
            v.Pos = w.Pos; Save();
        }

        static void Open(string kind)
        {
            DesktopWidget w;
            switch (kind)
            {
                case "clock": w = new ClockWidget(); break;
                case "calendar": w = new CalendarWidget(); break;
                case "battery": w = new BatteryWidget(); break;
                case "system": w = new SystemWidget(); break;
                case "notes": w = new NotesWidget(); break;
                default: return;
            }
            w.Init(kind, s, light);
            Saved v; saved.TryGetValue(kind, out v);
            w.Pos = Clamp(v != null && v.Pos.HasValue ? v.Pos.Value : DefaultPos(kind), w.CardSize);
            live.Add(w);
            w.ShowOnDesktop();
            if (paused) w.SetPaused(true);
        }

        // Posicion inicial: columna a la izquierda, debajo de la barra superior (como en macOS).
        static Point DefaultPos(string kind)
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int x0 = wa.Left + (int)(24 * s), y0 = wa.Top + (int)(24 * s);
            int small = (int)(DesktopWidget.SmallPx * s), gap = (int)(DesktopWidget.GapPx * s);
            switch (kind)
            {
                case "clock": return new Point(x0, y0);
                case "battery": return new Point(x0 + small + gap, y0);
                case "calendar": return new Point(x0, y0 + small + gap);
                case "system": return new Point(x0, y0 + (small + gap) * 2);
                case "notes": return new Point(x0 + small + gap, y0 + (small + gap) * 2);
            }
            return new Point(x0, y0);
        }

        static Point Clamp(Point p, Size card)
        {
            var b = Screen.PrimaryScreen.Bounds;
            return new Point(Math.Max(b.Left, Math.Min(b.Right - card.Width, p.X)), Math.Max(b.Top, Math.Min(b.Bottom - card.Height, p.Y)));
        }

        // widgets.ini:  reloj=1,24,60   (encendido, x, y)
        static void Load()
        {
            if (saved != null) return;
            saved = new Dictionary<string, Saved>(StringComparer.OrdinalIgnoreCase);
            bool any = false;
            try
            {
                if (File.Exists(IniPath))
                    foreach (var line in File.ReadAllLines(IniPath))
                    {
                        var kv = line.Split(new[] { '=' }, 2); if (kv.Length < 2) continue;
                        var parts = kv[1].Split(',');
                        var v = new Saved { On = parts[0].Trim() == "1" };
                        int x, y;
                        if (parts.Length >= 3 && int.TryParse(parts[1], out x) && int.TryParse(parts[2], out y)) v.Pos = new Point(x, y);
                        saved[kv[0].Trim()] = v; any = true;
                    }
            }
            catch (Exception ex) { Program.Log(ex); }
            if (!any) foreach (var k in DefaultOn) saved[k] = new Saved { On = true };
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                var lines = new List<string> { "# Widgets del escritorio: encendido(1/0), x, y. Borra este archivo para volver a lo de fabrica." };
                foreach (var k in Kinds)
                {
                    Saved v; if (!saved.TryGetValue(k, out v)) continue;
                    lines.Add(k + "=" + (v.On ? 1 : 0) + (v.Pos.HasValue ? "," + v.Pos.Value.X + "," + v.Pos.Value.Y : ""));
                }
                File.WriteAllLines(IniPath, lines);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        // ---- menu del clic derecho sobre un widget ----
        internal static void ShowMenu(DesktopWidget w, Point at)
        {
            var m = MenuStyle.New(light);
            Action<string, Action> add = (text, act) => { var mi = new ToolStripMenuItem(text); mi.Click += (o, e) => { try { act(); } catch (Exception ex) { Program.Log(ex); } }; m.Items.Add(mi); };
            if (w is NotesWidget) { add("Editar nota", ((NotesWidget)w).Edit); m.Items.Add(new ToolStripSeparator()); }
            add("Quitar este widget", () => SetEnabled(w.Kind, false));
            var more = new ToolStripMenuItem("Widgets");
            foreach (var k in Kinds)
            {
                string kind = k; bool on = IsOn(kind);
                var mi = new ToolStripMenuItem(NameOf(kind)) { Checked = on };
                mi.Click += (o, e) => SetEnabled(kind, !on);
                more.DropDownItems.Add(mi);
            }
            m.Items.Add(more);
            add("Acomodar como al principio", ResetPositions);
            m.Show(at);
        }
    }

    // ===================== base: tarjeta de vidrio que se pega al escritorio =====================
    abstract class DesktopWidget : Form
    {
        public const float SmallPx = 158f, GapPx = 18f;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
        [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy, flags; }

        public string Kind;
        public Point Pos;                // esquina de la tarjeta (sin la sombra)
        protected float s; protected bool light;
        readonly Timer timer = new Timer();
        string lastKey;
        Point downScreen, downPos; bool down, moved;

        protected virtual bool Wide { get { return false; } }
        protected virtual int RefreshMs { get { return 30000; } }
        protected abstract string StateKey();                         // si no cambia, no se redibuja
        protected abstract void DrawContent(Graphics g, RectangleF r);
        protected virtual void OnClickCard() { }

        public Size CardSize { get { int sm = (int)(SmallPx * s); return new Size(Wide ? sm * 2 + (int)(GapPx * s) : sm, sm); } }
        int ShadowM { get { return (int)Math.Ceiling(14 * s); } }   // espacio para la sombra

        public void Init(string kind, float scale, bool lightTheme)
        {
            Kind = kind; s = scale; light = lightTheme;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Text = "MacDockWidget";
            timer.Interval = RefreshMs;
            timer.Tick += (o, e) => Redraw(false);
        }

        public void SetTheme(bool lightTheme) { light = lightTheme; Redraw(true); }
        public void SetPaused(bool p) { if (p) timer.Stop(); else { timer.Start(); Redraw(false); } }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8000000; return cp; }   // capas, sin Alt+Tab, no roba el foco
        }

        public void ShowOnDesktop()
        {
            // El "dueno" es el escritorio: asi queda encima del fondo, debajo de las ventanas, y Win + D no lo esconde.
            IntPtr desk = N.FindWindow("Progman", null);
            if (desk != IntPtr.Zero) SetWindowLongPtr(Handle, -8, desk);
            Redraw(true);
            N.ShowWindow(Handle, 4);
            N.SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // al fondo
            timer.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0046)   // WM_WINDOWPOSCHANGING: siempre al fondo, nunca encima de tus ventanas
            {
                var wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                if ((wp.flags & 0x0004) == 0) { wp.hwndInsertAfter = new IntPtr(1); Marshal.StructureToPtr(wp, m.LParam, false); }
            }
            else if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }   // WM_MOUSEACTIVATE: MA_NOACTIVATE
            base.WndProc(ref m);
        }

        protected void Redraw(bool force)
        {
            if (IsDisposed || !IsHandleCreated && !force) return;
            string key = StateKey() + "|" + light + "|" + Pos;
            if (!force && key == lastKey) return;
            lastKey = key;
            var card = CardSize; int M = ShadowM;
            using (var bmp = new Bitmap(card.Width + M * 2, card.Height + M * 2, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    var r = new RectangleF(M, M, card.Width, card.Height);
                    float rad = 22 * s;
                    // sombra suave
                    for (int i = 6; i >= 1; i--)
                    {
                        var sr = RectangleF.Inflate(r, i * 1.6f * s, i * 1.6f * s); sr.Offset(0, 3 * s);
                        using (var p = Round(sr, rad + i * 1.6f * s)) using (var b = new SolidBrush(Color.FromArgb(light ? 5 : 8, 0, 0, 0))) g.FillPath(b, p);
                    }
                    using (var p = Round(r, rad))
                    {
                        using (var b = new SolidBrush(light ? Color.FromArgb(208, 250, 250, 252) : Color.FromArgb(200, 30, 30, 34))) g.FillPath(b, p);
                        using (var pen = new Pen(light ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(46, 255, 255, 255), Math.Max(1f, 0.8f * s))) g.DrawPath(pen, p);
                    }
                    try { DrawContent(g, r); } catch (Exception ex) { Program.Log(ex); }
                }
                Push(bmp, new Point(Pos.X - M, Pos.Y - M));
            }
        }

        void Push(Bitmap bmp, Point at)
        {
            IntPtr screenDc = N.GetDC(IntPtr.Zero), memDc = N.CreateCompatibleDC(screenDc), hb = bmp.GetHbitmap(Color.FromArgb(0)), old = N.SelectObject(memDc, hb);
            try
            {
                var size = new N.SIZE { cx = bmp.Width, cy = bmp.Height }; var src = new N.POINT(); var dst = new N.POINT { x = at.X, y = at.Y };
                var bl = new N.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                N.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bl, 2);
            }
            finally { N.ReleaseDC(IntPtr.Zero, screenDc); N.SelectObject(memDc, old); N.DeleteObject(hb); N.DeleteDC(memDc); }
        }

        // ---- arrastrar para mover ----
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            down = true; moved = false; downScreen = Cursor.Position; downPos = Pos; Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!down) return;
            var c = Cursor.Position;
            int dx = c.X - downScreen.X, dy = c.Y - downScreen.Y;
            if (!moved && Math.Abs(dx) + Math.Abs(dy) < 5 * s) return;
            moved = true;
            Pos = new Point(downPos.X + dx, downPos.Y + dy);
            Redraw(true);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { Widgets.ShowMenu(this, Cursor.Position); return; }
            if (!down) return;
            down = false; Capture = false;
            if (moved)
            {
                // se acomoda a una cuadricula invisible para que queden alineados
                int step = (int)(6 * s);
                Pos = new Point((int)Math.Round(Pos.X / (double)step) * step, (int)Math.Round(Pos.Y / (double)step) * step);
                Redraw(true);
                Widgets.Moved(this);
            }
            else OnClickCard();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Stop(); timer.Dispose(); base.OnFormClosed(e); }

        // ---- ayudas de dibujo ----
        protected Color Fg { get { return light ? Color.FromArgb(28, 28, 30) : Color.FromArgb(245, 245, 247); } }
        protected Color Dim { get { return light ? Color.FromArgb(130, 28, 28, 30) : Color.FromArgb(150, 245, 245, 247); } }
        protected static readonly Color Red = Color.FromArgb(255, 59, 48), Green = Color.FromArgb(52, 199, 89), Yellow = Color.FromArgb(255, 204, 0), Blue = Color.FromArgb(10, 132, 255);

        protected Font F(float px, bool semi = false) { return new Font(semi ? UiFont.Semi : UiFont.Reg, px * s, FontStyle.Regular, GraphicsUnit.Pixel); }

        protected void Txt(Graphics g, string t, Font f, Color c, RectangleF r, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(t, f, b, r, sf);
        }

        // anillo de progreso (bateria, CPU, RAM)
        protected void Ring(Graphics g, PointF c, float radius, float width, float value, Color color)
        {
            var rr = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var pen = new Pen(light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255), width)) g.DrawEllipse(pen, rr);
            if (value > 0.005f)
                using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, rr, -90, 360f * Math.Min(1f, value));
        }

        protected static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Max(0.1f, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    // ===================== reloj analogico =====================
    class ClockWidget : DesktopWidget
    {
        protected override int RefreshMs { get { return 1000; } }
        protected override string StateKey() { return DateTime.Now.ToString("HH:mm:ss"); }

        protected override void DrawContent(Graphics g, RectangleF r)
        {
            var now = DateTime.Now;
            var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            float R = r.Width / 2f - 12 * s;
            using (var b = new SolidBrush(light ? Color.White : Color.FromArgb(22, 22, 24))) g.FillEllipse(b, c.X - R, c.Y - R, R * 2, R * 2);
            // marcas de minutos y numeros
            for (int i = 0; i < 60; i++)
            {
                double a = i * Math.PI / 30;
                bool hour = i % 5 == 0;
                float r1 = R - 4 * s, r2 = R - (hour ? 9 : 6) * s;
                using (var pen = new Pen(hour ? Fg : Dim, (hour ? 1.6f : 0.8f) * s))
                    g.DrawLine(pen, c.X + (float)Math.Sin(a) * r1, c.Y - (float)Math.Cos(a) * r1, c.X + (float)Math.Sin(a) * r2, c.Y - (float)Math.Cos(a) * r2);
            }
            using (var f = F(12.5f, true))
                for (int h = 1; h <= 12; h++)
                {
                    double a = h * Math.PI / 6; float rn = R - 19 * s;
                    var p = new PointF(c.X + (float)Math.Sin(a) * rn, c.Y - (float)Math.Cos(a) * rn);
                    Txt(g, h.ToString(), f, Fg, new RectangleF(p.X - 12 * s, p.Y - 9 * s, 24 * s, 18 * s), StringAlignment.Center, StringAlignment.Center);
                }
            using (var f = F(10f, true))
                Txt(g, now.ToString("tt", CultureInfo.InvariantCulture) == "AM" ? "AM" : "PM", f, Dim, new RectangleF(c.X - 20 * s, c.Y - R * 0.48f, 40 * s, 14 * s), StringAlignment.Center);
            Hand(g, c, (now.Hour % 12 + now.Minute / 60f) * 30f, R * 0.5f, 4.2f * s, Fg);
            Hand(g, c, (now.Minute + now.Second / 60f) * 6f, R * 0.78f, 3.2f * s, Fg);
            Hand(g, c, now.Second * 6f, R * 0.85f, 1.3f * s, Color.FromArgb(255, 149, 0));
            using (var b = new SolidBrush(Color.FromArgb(255, 149, 0))) g.FillEllipse(b, c.X - 3.2f * s, c.Y - 3.2f * s, 6.4f * s, 6.4f * s);
            using (var b = new SolidBrush(light ? Color.White : Color.FromArgb(22, 22, 24))) g.FillEllipse(b, c.X - 1.2f * s, c.Y - 1.2f * s, 2.4f * s, 2.4f * s);
        }

        static void Hand(Graphics g, PointF c, float deg, float len, float w, Color col)
        {
            double a = deg * Math.PI / 180;
            using (var pen = new Pen(col, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, c.X - (float)Math.Sin(a) * len * 0.12f, c.Y + (float)Math.Cos(a) * len * 0.12f, c.X + (float)Math.Sin(a) * len, c.Y - (float)Math.Cos(a) * len);
        }
    }

    // ===================== calendario (mediano): el dia de hoy y el mes =====================
    class CalendarWidget : DesktopWidget
    {
        static readonly string[] Days = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };
        static readonly string[] Ini = { "D", "L", "M", "X", "J", "V", "S" };
        static readonly string[] Months = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

        protected override bool Wide { get { return true; } }
        protected override int RefreshMs { get { return 60000; } }
        protected override string StateKey() { return DateTime.Today.ToString("yyyyMMdd"); }

        protected override void DrawContent(Graphics g, RectangleF r)
        {
            var today = DateTime.Today;
            float pad = 16 * s;
            // izquierda: dia de la semana, numero grande, mes
            var left = new RectangleF(r.X + pad, r.Y + pad, r.Width * 0.36f, r.Height - pad * 2);
            using (var f = F(12f, true)) Txt(g, Days[(int)today.DayOfWeek].ToUpperInvariant(), f, Red, new RectangleF(left.X, left.Y, left.Width, 18 * s));
            using (var f = F(54f)) Txt(g, today.Day.ToString(), f, Fg, new RectangleF(left.X - 3 * s, left.Y + 14 * s, left.Width + 10 * s, 66 * s));
            using (var f = F(13f, true)) Txt(g, Cap(Months[today.Month - 1]), f, Fg, new RectangleF(left.X, left.Bottom - 34 * s, left.Width, 18 * s));
            using (var f = F(12f)) Txt(g, today.Year.ToString(), f, Dim, new RectangleF(left.X, left.Bottom - 17 * s, left.Width, 18 * s));

            // derecha: la cuadricula del mes
            int first = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            var right = new RectangleF(r.X + r.Width * 0.42f, r.Y + pad - 2 * s, r.Width * 0.58f - pad, r.Height - pad * 2 + 4 * s);
            var month1 = new DateTime(today.Year, today.Month, 1);
            int lead = ((int)month1.DayOfWeek - first + 7) % 7;
            int daysIn = DateTime.DaysInMonth(today.Year, today.Month);
            int rowsNeeded = (lead + daysIn + 6) / 7;
            float cw = right.Width / 7f, ch = right.Height / (rowsNeeded + 1);
            using (var fh = F(10.5f, true))
                for (int i = 0; i < 7; i++)
                {
                    int dow = (first + i) % 7;
                    Txt(g, Ini[dow], fh, dow == 0 || dow == 6 ? Dim : Fg, new RectangleF(right.X + i * cw, right.Y, cw, ch), StringAlignment.Center, StringAlignment.Center);
                }
            using (var fd = F(11f))
            using (var ft = F(11f, true))
                for (int d = 1; d <= daysIn; d++)
                {
                    int cell = lead + d - 1;
                    var cr = new RectangleF(right.X + (cell % 7) * cw, right.Y + (cell / 7 + 1) * ch, cw, ch);
                    bool isToday = d == today.Day;
                    if (isToday)
                    {
                        float dd = Math.Min(cw, ch) * 0.92f;
                        using (var b = new SolidBrush(Red)) g.FillEllipse(b, cr.X + (cw - dd) / 2, cr.Y + (ch - dd) / 2, dd, dd);
                    }
                    var dow = (DayOfWeek)((first + cell) % 7);
                    Color col = isToday ? Color.White : (dow == DayOfWeek.Sunday || dow == DayOfWeek.Saturday ? Dim : Fg);
                    Txt(g, d.ToString(), isToday ? ft : fd, col, cr, StringAlignment.Center, StringAlignment.Center);
                }
        }

        static string Cap(string t) { return t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t.Substring(1); }
    }

    // ===================== bateria =====================
    class BatteryWidget : DesktopWidget
    {
        protected override int RefreshMs { get { return 5000; } }
        protected override string StateKey() { var b = BatteryGlyph.Read(); return (int)(b.Pct * 100) + "|" + b.Plugged + b.Charging + b.HasBattery; }

        // Diseno limpio: un solo anillo con el porcentaje grande adentro; el rayo pequeno arriba solo si esta conectada.
        protected override void DrawContent(Graphics g, RectangleF r)
        {
            var b = BatteryGlyph.Read();
            var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height * 0.44f);
            float R = r.Width * 0.3f;
            Color col = !b.HasBattery ? Dim : (b.Pct <= 0.2f && !b.Plugged ? Red : Green);
            Ring(g, c, R, 7 * s, b.HasBattery ? b.Pct : 1f, col);

            if (b.Plugged) Bolt(g, new PointF(c.X, c.Y - R * 0.52f), 16 * s, col);
            string big = b.HasBattery ? ((int)Math.Round(b.Pct * 100)).ToString() : "—";
            using (var f = F(28f, true))
            {
                var sz = g.MeasureString(big, f);
                float y = c.Y - sz.Height / 2f + (b.Plugged ? 5 * s : 0);
                Txt(g, big, f, Fg, new RectangleF(c.X - R, y, R * 2, sz.Height), StringAlignment.Center);
                if (b.HasBattery)
                    using (var fp = F(12f, true)) Txt(g, "%", fp, Dim, new RectangleF(c.X + sz.Width / 2f - 3 * s, y + 7 * s, 20 * s, 16 * s));
            }
            string small = !b.HasBattery ? "Sin batería" : b.Charging ? "Cargando" : b.Plugged ? "Conectada" : "Batería";
            using (var f = F(12f)) Txt(g, small, f, Dim, new RectangleF(r.X, r.Bottom - 26 * s, r.Width, 16 * s), StringAlignment.Center);
        }

        static void Bolt(Graphics g, PointF center, float h, Color col)
        {
            float w = h * 0.62f, top = center.Y - h / 2f, cx = center.X;
            var pts = new[] {
                new PointF(cx + w * 0.12f, top), new PointF(cx - w * 0.5f, top + h * 0.56f), new PointF(cx - w * 0.02f, top + h * 0.56f),
                new PointF(cx - w * 0.14f, top + h), new PointF(cx + w * 0.5f, top + h * 0.42f), new PointF(cx + w * 0.02f, top + h * 0.42f) };
            using (var b = new SolidBrush(col)) g.FillPolygon(b, pts);
        }
    }

    // ===================== sistema: uso de procesador y memoria =====================
    class SystemWidget : DesktopWidget
    {
        [StructLayout(LayoutKind.Sequential)] struct FILETIME { public uint Low, High; public ulong V { get { return ((ulong)High << 32) | Low; } } }
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [StructLayout(LayoutKind.Sequential)] class MEMORYSTATUSEX { public uint dwLength = 64, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        ulong lastIdle, lastTotal; float cpu, ram; double usedGb, totalGb;

        protected override int RefreshMs { get { return 2000; } }

        protected override string StateKey()
        {
            Sample();
            return (int)(cpu * 100) + "|" + (int)(ram * 100);
        }

        void Sample()
        {
            FILETIME idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                ulong total = kernel.V + user.V;   // el tiempo de kernel ya incluye el inactivo
                if (lastTotal != 0 && total > lastTotal)
                {
                    double busy = 1.0 - (double)(idle.V - lastIdle) / (total - lastTotal);
                    cpu = (float)Math.Max(0, Math.Min(1, busy));
                }
                lastIdle = idle.V; lastTotal = total;
            }
            var m = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(m) && m.ullTotalPhys > 0)
            {
                ram = (float)(1.0 - (double)m.ullAvailPhys / m.ullTotalPhys);
                totalGb = m.ullTotalPhys / 1073741824.0; usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            }
        }

        protected override void DrawContent(Graphics g, RectangleF r)
        {
            float pad = 14 * s;
            using (var f = F(12f, true)) Txt(g, "Sistema", f, Fg, new RectangleF(r.X + pad, r.Y + pad - 2 * s, r.Width - pad * 2, 18 * s));
            float R = r.Width * 0.17f, cy = r.Y + r.Height * 0.52f;
            var c1 = new PointF(r.X + r.Width * 0.28f, cy); var c2 = new PointF(r.X + r.Width * 0.72f, cy);
            Ring(g, c1, R, 6 * s, cpu, cpu > 0.85f ? Red : Blue);
            Ring(g, c2, R, 6 * s, ram, ram > 0.85f ? Red : Color.FromArgb(175, 82, 222));
            using (var f = F(13f, true))
            {
                Txt(g, (int)Math.Round(cpu * 100) + "%", f, Fg, new RectangleF(c1.X - R, c1.Y - R, R * 2, R * 2), StringAlignment.Center, StringAlignment.Center);
                Txt(g, (int)Math.Round(ram * 100) + "%", f, Fg, new RectangleF(c2.X - R, c2.Y - R, R * 2, R * 2), StringAlignment.Center, StringAlignment.Center);
            }
            using (var f = F(11f))
            {
                Txt(g, "CPU", f, Dim, new RectangleF(c1.X - R * 1.6f, cy + R + 6 * s, R * 3.2f, 15 * s), StringAlignment.Center);
                Txt(g, "RAM", f, Dim, new RectangleF(c2.X - R * 1.6f, cy + R + 6 * s, R * 3.2f, 15 * s), StringAlignment.Center);
            }
            if (totalGb > 0)
                using (var f = F(10f)) Txt(g, usedGb.ToString("0.0") + " de " + totalGb.ToString("0") + " GB", f, Dim, new RectangleF(r.X, r.Bottom - 20 * s, r.Width, 14 * s), StringAlignment.Center);
        }
    }

    // ===================== notas rapidas (clic para escribir) =====================
    class NotesWidget : DesktopWidget
    {
        string text = "";
        DateTime readAt = DateTime.MinValue;

        protected override int RefreshMs { get { return 60000; } }

        protected override string StateKey()
        {
            try
            {
                var p = Widgets.NotesPath;
                var t = File.Exists(p) ? File.GetLastWriteTime(p) : DateTime.MinValue;
                if (t != readAt) { readAt = t; text = File.Exists(p) ? File.ReadAllText(p) : ""; }
            }
            catch { }
            return text.GetHashCode().ToString();
        }

        protected override void OnClickCard() { Edit(); }

        public void Edit()
        {
            var ed = new NoteEditor(text, light, s, new Rectangle(Pos, CardSize));
            ed.FormClosed += (o, e) =>
            {
                if (ed.Result == null || ed.Result == text) return;
                try { Directory.CreateDirectory(Program.DataDir); File.WriteAllText(Widgets.NotesPath, ed.Result); } catch (Exception ex) { Program.Log(ex); }
                Redraw(true);
            };
            ed.Show();
            ed.Activate();
        }

        protected override void DrawContent(Graphics g, RectangleF r)
        {
            float pad = 14 * s;
            // icono amarillo de notas + titulo
            using (var p = Round(new RectangleF(r.X + pad, r.Y + pad, 16 * s, 16 * s), 4 * s)) using (var b = new SolidBrush(Yellow)) g.FillPath(b, p);
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1.2f * s))
                for (int i = 0; i < 2; i++) g.DrawLine(pen, r.X + pad + 4 * s, r.Y + pad + (7 + i * 4) * s, r.X + pad + 12 * s, r.Y + pad + (7 + i * 4) * s);
            using (var f = F(12f, true)) Txt(g, "Notas", f, Fg, new RectangleF(r.X + pad + 22 * s, r.Y + pad, r.Width - pad * 2, 18 * s));
            var body = new RectangleF(r.X + pad, r.Y + pad + 26 * s, r.Width - pad * 2, r.Height - pad * 2 - 26 * s);
            string t = text.Trim();
            using (var f = F(12f))
            using (var b = new SolidBrush(t == "" ? Dim : Fg))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                g.DrawString(t == "" ? "Haz clic para escribir una nota." : t, f, b, body, sf);
        }
    }

    // Ventanita para escribir la nota (normal, con foco). Se guarda al cerrarla o al hacer clic fuera.
    class NoteEditor : Form
    {
        public string Result;
        readonly TextBox box;

        public NoteEditor(string text, bool light, float s, Rectangle near)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; KeyPreview = true;
            Color bg = light ? Color.FromArgb(255, 252, 232) : Color.FromArgb(44, 42, 34), fg = light ? Color.FromArgb(30, 30, 30) : Color.FromArgb(245, 245, 240);
            BackColor = bg; Padding = new Padding((int)(14 * s));
            int w = (int)(300 * s), h = (int)(240 * s);
            var wa = Screen.FromRectangle(near).WorkingArea;
            Bounds = new Rectangle(Math.Max(wa.Left, Math.Min(wa.Right - w, near.Right + (int)(12 * s))), Math.Max(wa.Top, Math.Min(wa.Bottom - h, near.Top)), w, h);
            var title = new Label { Text = "Notas  ·  Esc para guardar y cerrar", Dock = DockStyle.Top, Height = (int)(24 * s), ForeColor = Color.FromArgb(200, 150, 0), Font = new Font(UiFont.Semi, 12f * s, FontStyle.Regular, GraphicsUnit.Pixel) };
            box = new TextBox { Multiline = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), BackColor = bg, ForeColor = fg, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = new Font(UiFont.Reg, 14f * s, FontStyle.Regular, GraphicsUnit.Pixel) };
            Controls.Add(box); Controls.Add(title);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); box.Focus(); box.SelectionStart = box.TextLength; }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); } }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (!IsDisposed) Close(); }
        protected override void OnFormClosing(FormClosingEventArgs e) { Result = box.Text; base.OnFormClosing(e); }
    }
}
