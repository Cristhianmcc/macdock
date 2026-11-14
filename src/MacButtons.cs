using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Botones de Mac (rojo/amarillo/verde) sobre TODAS las ventanas abiertas, como en macOS:
    // a color en la ventana activa y en gris en las demas. Cada capa queda "pegada" a su ventana
    // (Windows la mantiene justo encima de ella y la oculta si se minimiza), asi nunca flota sobre otras apps.
    // No modifica ni inyecta nada en las apps.
    class MacButtons : Form
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, int flags);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        WinEventProc proc; IntPtr hookSys, hookObj, hookLoc, hookCloak; uint hookThreadId;
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, int msg, IntPtr w, IntPtr l);
        readonly HashSet<IntPtr> tracked = new HashSet<IntPtr>();      // ventanas con botones (para filtrar rapido en el hilo de eventos)
        readonly HashSet<IntPtr> pendingMove = new HashSet<IntPtr>();  // movimientos agrupados
        bool moveScheduled, closing;

        // Corre en el hilo de eventos: descarta casi todo sin tocar el hilo principal.
        void OnEventBg(IntPtr hk, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0 || hwnd == IntPtr.Zero || closing) return;
            if (ev == 0x800B)
            {
                lock (tracked) { if (!tracked.Contains(hwnd)) return; }
                lock (pendingMove) { pendingMove.Add(hwnd); if (moveScheduled) return; moveScheduled = true; }
                try { BeginInvoke(new Action(FlushMoves)); } catch { }
                return;
            }
            if (ev != 0x0003 && ev != 0x000A && ev != 0x000B && ev != 0x0017 && ev != 0x8000 && ev != 0x8001 && ev != 0x8002 && ev != 0x8003 && ev != 0x8017 && ev != 0x8018) return;
            if ((ev == 0x000A || ev == 0x000B || ev == 0x0017 || ev == 0x8001 || ev == 0x8003 || ev == 0x8017)) { lock (tracked) { if (!tracked.Contains(hwnd)) return; } }
            try { BeginInvoke(new Action(() => OnEvent(hk, ev, hwnd, obj, child, thread, time))); } catch { }
        }

        void FlushMoves()
        {
            List<IntPtr> list; lock (pendingMove) { list = pendingMove.ToList(); pendingMove.Clear(); moveScheduled = false; }
            if (paused) return;
            ButtonOverlay ov;
            foreach (var h in list) if (overlays.TryGetValue(h, out ov)) ov.Place(h == active);
        }
        readonly Dictionary<IntPtr, ButtonOverlay> overlays = new Dictionary<IntPtr, ButtonOverlay>();
        IntPtr active = IntPtr.Zero;
        Timer cleanup = new Timer { Interval = 2000 };
        public static readonly uint MyPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        bool light;
        readonly HashSet<IntPtr> bordered = new HashSet<IntPtr>();   // ventanas a las que les pusimos el borde de Mac

        public MacButtons(bool lightTheme)
        {
            light = lightTheme;
            MacButtonRules.Load();
            MacButtonRules.Changed += OnRulesChanged;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(-200, -200, 1, 1);
            cleanup.Tick += (o, e) => Cleanup();
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8000000; return cp; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        public void SetTheme(bool lightTheme) { light = lightTheme; foreach (var h in bordered.ToList()) Border(h, true); }

        void OnRulesChanged() { try { if (!IsDisposed) BeginInvoke(new Action(ApplyRules)); } catch { } }

        // se cambiaron las apps elegidas o el borde: quitar/poner sin reiniciar nada
        void ApplyRules()
        {
            foreach (var h in overlays.Keys.ToList())
            {
                if (!MacButtonRules.Allowed(h)) Remove(h);
                else Border(h, MacButtonRules.MacBorder);
            }
            var all = new List<IntPtr>();
            N.EnumWindows((h, l) => { all.Add(h); return true; }, IntPtr.Zero);
            foreach (var h in all) Ensure(h);
        }

        // esquinas redondeadas y un borde gris fino, como las ventanas de Mac (Windows 11; en Windows 10 no hace nada)
        void Border(IntPtr h, bool on)
        {
            try
            {
                if (on)
                {
                    int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4);
                    int col = light ? 0x00C8C6C4 : 0x00544F4C;   // gris claro / gris oscuro (formato 0x00BBGGRR)
                    DwmSetWindowAttribute(h, 34, ref col, 4);
                    bordered.Add(h);
                }
                else if (bordered.Remove(h))
                {
                    int def = 0; DwmSetWindowAttribute(h, 33, ref def, 4);
                    int dc = unchecked((int)0xFFFFFFFF); DwmSetWindowAttribute(h, 34, ref dc, 4);   // color de Windows otra vez
                }
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            N.ShowWindow(Handle, 0);
            proc = OnEventBg;
            var th = new System.Threading.Thread(() =>
            {
                hookThreadId = GetCurrentThreadId();
                hookSys = SetWinEventHook(0x0003, 0x0017, IntPtr.Zero, proc, 0, 0, 0);
                hookObj = SetWinEventHook(0x8000, 0x8003, IntPtr.Zero, proc, 0, 0, 0);
                hookLoc = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, proc, 0, 0, 0);
                hookCloak = SetWinEventHook(0x8017, 0x8018, IntPtr.Zero, proc, 0, 0, 0);   // las apps modernas (WhatsApp, Teams...) se muestran "descubriendose"
                Application.Run();
                UnhookWinEvent(hookSys); UnhookWinEvent(hookObj); UnhookWinEvent(hookLoc); UnhookWinEvent(hookCloak);
            });
            th.IsBackground = true; th.SetApartmentState(System.Threading.ApartmentState.STA); th.Start();
            active = N.GetForegroundWindow();
            var all = new List<IntPtr>();
            N.EnumWindows((h, l) => { all.Add(h); return true; }, IntPtr.Zero);
            foreach (var h in all) Ensure(h);
            cleanup.Start();
        }

        // Con un juego o video a pantalla completa no se muestra ningun boton encima.
        bool paused;
        public void SetPaused(bool p)
        {
            if (paused == p) return;
            paused = p;
            foreach (var kv in overlays.ToList()) { if (p) kv.Value.Hide2(); else kv.Value.Place(kv.Key == active); }
        }

        void OnEvent(IntPtr hk, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0 || hwnd == IntPtr.Zero || paused) return;
            try
            {
                ButtonOverlay ov;
                switch (ev)
                {
                    case 0x0003:
                        var old = active; active = hwnd;
                        if (overlays.TryGetValue(old, out ov)) ov.Place(false);
                        if (!overlays.ContainsKey(hwnd)) Ensure(hwnd);
                        if (overlays.TryGetValue(hwnd, out ov)) ov.Place(true);
                        break;
                    case 0x000A: if (overlays.TryGetValue(hwnd, out ov)) ov.Hide2(); break;
                    case 0x000B: case 0x0017: if (overlays.TryGetValue(hwnd, out ov)) ov.Place(hwnd == active); break;
                    case 0x8002: case 0x8000: Ensure(hwnd); break;
                    case 0x8001: case 0x8003: Remove(hwnd); break;
                    case 0x8017: if (overlays.TryGetValue(hwnd, out ov)) ov.Hide2(); break;                 // se oculto (cloak)
                    case 0x8018: if (overlays.TryGetValue(hwnd, out ov)) ov.Place(hwnd == active); else Ensure(hwnd); break;   // aparecio

                }
            }
            catch { }
        }

        void Ensure(IntPtr h)
        {
            if (overlays.ContainsKey(h) || !Eligible(h) || !MacButtonRules.Allowed(h)) return;
            var ov = new ButtonOverlay(h);
            if (MacButtonRules.MacBorder) Border(h, true);
            overlays[h] = ov; lock (tracked) tracked.Add(h);
            ov.Show();
            ov.Place(h == active);
        }

        void Remove(IntPtr h)
        {
            ButtonOverlay ov;
            if (!overlays.TryGetValue(h, out ov)) return;
            overlays.Remove(h); lock (tracked) tracked.Remove(h);
            if (IsWindow(h)) Border(h, false); else bordered.Remove(h);
            try { if (!ov.IsDisposed) { ov.Close(); ov.Dispose(); } } catch { }
        }

        void Cleanup()
        {
            foreach (var h in overlays.Keys.ToList())
                if (!IsWindow(h) || !N.IsWindowVisible(h) || overlays[h].IsDisposed) Remove(h);
            if (paused) return;
            // Autorreparacion: si una ventana normal se quedo sin botones (o con ellos escondidos) por un aviso que no llego, se vuelven a poner.
            var all = new List<IntPtr>();
            N.EnumWindows((h, l) => { if (N.IsWindowVisible(h)) all.Add(h); return true; }, IntPtr.Zero);
            foreach (var h in all)
            {
                ButtonOverlay ov;
                if (!overlays.TryGetValue(h, out ov)) { Ensure(h); continue; }
                if (!ov.IsShown && !N.IsIconic(h)) ov.Place(h == active);
            }
        }

        public static bool Eligible(IntPtr h)
        {
            if (h == IntPtr.Zero || !N.IsWindowVisible(h)) return false;
            if (GetAncestor(h, 2) != h) return false;
            uint pid; N.GetWindowThreadProcessId(h, out pid); if (pid == MyPid) return false;
            int st = N.GetWindowLong(h, -16), ex = N.GetWindowLong(h, -20);
            if ((st & 0x20000) == 0 && (st & 0x10000) == 0) return false;
            if ((st & 0x40000) == 0 && (st & 0x00C00000) != 0x00C00000) return false;
            if ((ex & 0x80) != 0) return false;
            int cloaked; if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            string c = N.ClassOf(h);
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Windows.UI.Core.CoreWindow" || c == "XamlExplorerHostIslandWindow" || c == "#32770") return false;
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true; MacButtonRules.Changed -= OnRulesChanged;
            try { if (hookThreadId != 0) PostThreadMessage(hookThreadId, 0x12, IntPtr.Zero, IntPtr.Zero); } catch { }
            cleanup.Stop();
            foreach (var h in overlays.Keys.ToList()) Remove(h);
            base.OnFormClosing(e);
        }
    }

    // La capa de una ventana: tapa sus botones de Windows con los de Mac.
    class ButtonOverlay : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out N.RECT r, int size);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out N.POINT p);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);

        readonly IntPtr target;
        Rectangle area; Color bgColor = Color.Black; bool isActive, inside, drawn, drawnActive; float s = 1f;
        Timer hoverTimer = new Timer { Interval = 60 };

        public ButtonOverlay(IntPtr target)
        {
            this.target = target;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(-200, -200, 1, 1);
            hoverTimer.Tick += (o, e) =>
            {
                N.POINT p; GetCursorPos(out p);
                if (!area.Contains(p.x, p.y)) { inside = false; hoverTimer.Stop(); Render(); }
            };
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8000000; return cp; } }
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowLongPtr(Handle, -8, target);   // pegada a su ventana
        }

        public void Hide2() { if (IsHandleCreated && !IsDisposed) N.ShowWindow(Handle, 0); }
        public bool IsShown { get { return IsHandleCreated && !IsDisposed && N.IsWindowVisible(Handle); } }

        public void Place(bool activeWin)
        {
            if (IsDisposed) return;
            try
            {
                IntPtr h = target;
                if (!MacButtons.Eligible(h) || N.IsIconic(h)) { Hide2(); return; }
                isActive = activeWin;
                N.RECT fr; if (DwmGetWindowAttribute(h, 9, out fr, 16) != 0) N.GetWindowRect(h, out fr);
                var scr = Screen.FromHandle(h);
                if (!N.IsZoomed(h) && fr.left <= scr.Bounds.Left && fr.top <= scr.Bounds.Top && fr.right >= scr.Bounds.Right && fr.bottom >= scr.Bounds.Bottom && (N.GetWindowLong(h, -16) & 0x00C00000) != 0x00C00000) { Hide2(); return; }
                float dpi = 1f; try { dpi = N.GetDpiForWindow(h) / 96f; } catch { }
                s = dpi;
                N.RECT cb, wr; N.GetWindowRect(h, out wr);
                Rectangle a;
                if (DwmGetWindowAttribute(h, 5, out cb, 16) == 0 && cb.right - cb.left > 30 * dpi)
                    a = new Rectangle(wr.left + cb.left, Math.Max(fr.top, wr.top + cb.top), cb.right - cb.left, Math.Max(20, cb.bottom - cb.top));
                else
                {
                    string wc = N.ClassOf(h); bool tall = wc == "WinUIDesktopWin32WindowClass" || wc == "ApplicationFrameWindow";
                    int w = (int)(138 * dpi), hh = (int)((tall ? 46 : 31) * dpi);
                    a = new Rectangle(fr.right - w, fr.top + 1, w - 1, hh);
                }
                if (a.Width < 40 || a.Height < 14 || fr.right - fr.left < 300) { Hide2(); return; }
                // Solo se movio: mover la capa sin redibujar ni leer la pantalla (lo que causaba el lag)
                if (drawn && a.Size == area.Size && activeWin == drawnActive && N.IsWindowVisible(Handle))
                {
                    if (a.Location != area.Location) { area = a; N.SetWindowPos(Handle, IntPtr.Zero, a.Left, a.Top, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x0200); }
                    return;
                }
                area = a; drawn = true; drawnActive = activeWin;
                IntPtr dc = N.GetDC(IntPtr.Zero);
                uint px = GetPixel(dc, a.Left - (int)(6 * dpi), a.Top + Math.Max(1, (int)(2 * dpi)));
                N.ReleaseDC(IntPtr.Zero, dc);
                if (px != 0xFFFFFFFF) bgColor = Color.FromArgb(255, (int)(px & 0xFF), (int)((px >> 8) & 0xFF), (int)((px >> 16) & 0xFF));
                Render();
                N.ShowWindow(Handle, 4);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        PointF Origin() { return new PointF((area.Width - TrafficLights.Width(s)) / 2f, (area.Height - TrafficLights.D(s)) / 2f); }

        void Render()
        {
            if (area.Width <= 0 || IsDisposed) return;
            using (var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(bgColor);
                    var o = Origin();
                    TrafficLights.Draw(g, o.X, o.Y, s, isActive, inside, bgColor.GetBrightness() > 0.55f);
                }
                IntPtr screenDc = N.GetDC(IntPtr.Zero), memDc = N.CreateCompatibleDC(screenDc), hb = bmp.GetHbitmap(Color.FromArgb(0)), old = N.SelectObject(memDc, hb);
                try
                {
                    var size = new N.SIZE { cx = area.Width, cy = area.Height }; var src = new N.POINT(); var dst = new N.POINT { x = area.Left, y = area.Top };
                    var bl = new N.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                    N.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bl, 2);
                }
                finally { N.ReleaseDC(IntPtr.Zero, screenDc); N.SelectObject(memDc, old); N.DeleteObject(hb); N.DeleteDC(memDc); }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (!inside) { inside = true; Render(); hoverTimer.Start(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var o = Origin();
            int i = TrafficLights.Hit(e.Location, o.X, o.Y, s);
            if (i < 0) return;
            IntPtr h = target;
            if (i == 0) N.PostMessage(h, 0x0112, new IntPtr(0xF060), IntPtr.Zero);
            else if (i == 1) N.PostMessage(h, 0x0112, new IntPtr(0xF020), IntPtr.Zero);
            else
            {
                N.RECT fr; if (DwmGetWindowAttribute(h, 9, out fr, 16) != 0) N.GetWindowRect(h, out fr);
                var wa = Screen.FromHandle(h).WorkingArea;
                bool max = N.IsZoomed(h) || (fr.left <= wa.Left + 2 && fr.top <= wa.Top + 2 && fr.right >= wa.Right - 2 && fr.bottom >= wa.Bottom - 2);
                N.PostMessage(h, 0x0112, new IntPtr(max ? 0xF120 : 0xF030), IntPtr.Zero);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { hoverTimer.Stop(); base.OnFormClosing(e); }
    }
}
