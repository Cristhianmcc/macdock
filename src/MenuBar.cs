using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Barra superior estilo Mac: logo (menu del sistema), app activa, y a la derecha Wi-Fi, volumen, bateria y fecha/hora.
    // Se dibuja solo cuando algo cambia (app activa, minuto, bateria, red): casi cero CPU.
    class MenuBar : Form
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);

        float s; bool light;
        int W, H, X, Y;
        bool full, appBarOn;
        int appBarMsg;
        WinEventProc proc; IntPtr hook;
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        string appName = "Escritorio", title = "", lastKey = "";
        IntPtr fgWnd = IntPtr.Zero;
        RectangleF rLogo, rApp, rWifi, rVol, rBat, rClock, rTheme, rTray;
        public Action ToggleTheme;   // lo conecta el dock: cambia claro/oscuro
        public Action ShowTray;      // lo conecta el dock: muestra los iconos de la bandeja (Surfshark, Drive...)
        Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;

        public MenuBar(float scale, bool lightTheme)
        {
            s = scale; light = lightTheme;
            Text = "MacDockMenuBar";
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(0, 0, 1, 1);
            appBarMsg = N.RegisterWindowMessage("MacDockMenuBarMsg");
            timer.Interval = 10000; timer.Tick += (o, e) => Redraw(false);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8000000 | 0x8; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public int BarHeight { get { return (int)Math.Round(26 * s); } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var b = Screen.PrimaryScreen.Bounds;
            W = b.Width; H = BarHeight; X = b.Left; Y = b.Top;
            RegisterAppBar();
            proc = OnWinEvent;
            hook = SetWinEventHook(3, 3, IntPtr.Zero, proc, 0, 0, 0);      // EVENT_SYSTEM_FOREGROUND
            NetworkChange.NetworkAvailabilityChanged += (o, a) => { try { BeginInvoke(new Action(() => Redraw(true))); } catch { } };
            NetworkChange.NetworkAddressChanged += (o, a) => { try { BeginInvoke(new Action(() => Redraw(true))); } catch { } };
            UpdateActive(Win32Fg());
            timer.Start();
        }

        static IntPtr Win32Fg() { return N.GetForegroundWindow(); }

        void OnWinEvent(IntPtr hk, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0) return;
            UpdateActive(hwnd);
        }

        void UpdateActive(IntPtr h)
        {
            try
            {
                if (h == IntPtr.Zero || h == Handle) return;
                uint pid; N.GetWindowThreadProcessId(h, out pid);
                if (pid == myPid) return;
                string cls = N.ClassOf(h);
                if (cls == "Shell_TrayWnd" || cls == "Windows.UI.Core.CoreWindow" || cls == "XamlExplorerHostIslandWindow" || cls == "#32768") return;
                fgWnd = h;
                string path = N.ProcessPath(pid);
                if (cls == "Progman" || cls == "WorkerW") { appName = "Escritorio"; title = ""; }
                else if (path.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase)) { appName = "Explorador"; title = N.TitleOf(h); }
                else { appName = NiceName(path); title = N.TitleOf(h); }
                Redraw(false);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        string NiceName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "App";
            string n;
            if (nameCache.TryGetValue(path, out n)) return n;
            n = Path.GetFileNameWithoutExtension(path);
            try { var vi = FileVersionInfo.GetVersionInfo(path); if (!string.IsNullOrEmpty(vi.FileDescription) && vi.FileDescription.Length < 40) n = vi.FileDescription; } catch { }
            nameCache[path] = n;
            return n;
        }

        public void SetFull(bool f)
        {
            if (f == full) return;
            full = f;
            if (full) N.ShowWindow(Handle, 0);
            else { N.ShowWindow(Handle, 8); N.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13); Redraw(true); }
        }

        public void SetTheme(bool lightTheme) { light = lightTheme; Redraw(true); }

        // ---------- dibujo ----------
        void Redraw(bool force)
        {
            if (full || W <= 0 || IsDisposed) return;
            var ps = SystemInformation.PowerStatus;
            bool net = false; try { net = NetworkInterface.GetIsNetworkAvailable(); } catch { }
            string clock = ClockText();
            string key = appName + "|" + title + "|" + clock + "|" + (int)(ps.BatteryLifePercent * 100) + ps.PowerLineStatus + "|" + net + "|" + light;
            if (!force && key == lastKey) return;
            lastKey = key;

            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    Color bg = light ? Color.FromArgb(190, 246, 246, 248) : Color.FromArgb(150, 18, 18, 22);
                    Color fg = light ? Color.FromArgb(255, 20, 20, 22) : Color.FromArgb(255, 245, 245, 247);
                    Color dim = light ? Color.FromArgb(150, 20, 20, 22) : Color.FromArgb(160, 245, 245, 247);
                    using (var b = new SolidBrush(bg)) g.FillRectangle(b, 0, 0, W, H);
                    using (var p = new Pen(light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255))) g.DrawLine(p, 0, H - 1, W, H - 1);

                    float cy = H / 2f;
                    using (var fBold = new Font(UiFont.Semi, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var fReg = new Font(UiFont.Reg, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var fIcon = new Font("Segoe MDL2 Assets", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var fgB = new SolidBrush(fg)) using (var dimB = new SolidBrush(dim))
                    {
                        // izquierda: logo + app activa + titulo
                        float x = 14 * s;
                        float lq = 5.5f * s, lg = 1.4f * s, ly = cy - lq - lg / 2;
                        rLogo = new RectangleF(x - 8 * s, 0, lq * 2 + lg + 16 * s, H);
                        g.FillRectangle(fgB, x, ly, lq, lq); g.FillRectangle(fgB, x + lq + lg, ly, lq, lq);
                        g.FillRectangle(fgB, x, ly + lq + lg, lq, lq); g.FillRectangle(fgB, x + lq + lg, ly + lq + lg, lq, lq);
                        x += lq * 2 + lg + 20 * s;

                        var an = g.MeasureString(appName, fBold);
                        rApp = new RectangleF(x - 8 * s, 0, an.Width + 16 * s, H);
                        g.DrawString(appName, fBold, fgB, x, cy - an.Height / 2);
                        x += an.Width + 16 * s;

                        // derecha: reloj, bateria, volumen, wifi (de derecha a izquierda)
                        float rx = W - 14 * s;
                        var cs = g.MeasureString(clock, fReg);
                        rx -= cs.Width;
                        g.DrawString(clock, fReg, fgB, rx, cy - cs.Height / 2);
                        rClock = new RectangleF(rx - 8 * s, 0, cs.Width + 16 * s, H);
                        rx -= 18 * s;

                        float pct = ps.BatteryLifePercent;
                        if (pct >= 0 && pct <= 1 && ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery)
                        {
                            string pt = (int)Math.Round(pct * 100) + "%";
                            float bw = 22 * s, bh = 11 * s;
                            rx -= bw + 2 * s;
                            float by = cy - bh / 2;
                            using (var pen = new Pen(dim, Math.Max(1f, 1.1f * s)))
                            using (var path = Round(new RectangleF(rx, by, bw, bh), 3 * s)) g.DrawPath(pen, path);
                            g.FillRectangle(dimB, rx + bw + 0.8f * s, cy - 2.2f * s, 1.6f * s, 4.4f * s);
                            Color fill = pct <= 0.2f ? Color.FromArgb(255, 69, 58) : (ps.PowerLineStatus == PowerLineStatus.Online ? Color.FromArgb(52, 199, 89) : fg);
                            using (var fb = new SolidBrush(fill))
                            using (var path = Round(new RectangleF(rx + 2 * s, by + 2 * s, Math.Max(1.5f * s, (bw - 4 * s) * pct), bh - 4 * s), 1.5f * s)) g.FillPath(fb, path);
                            var pts = g.MeasureString(pt, fReg);
                            rx -= pts.Width + 3 * s;
                            g.DrawString(pt, fReg, fgB, rx, cy - pts.Height / 2);
                            rBat = new RectangleF(rx - 6 * s, 0, pts.Width + bw + 18 * s, H);
                            rx -= 18 * s;
                        }
                        else rBat = RectangleF.Empty;

                        string vol = "";
                        var vs = g.MeasureString(vol, fIcon); rx -= vs.Width;
                        g.DrawString(vol, fIcon, fgB, rx, cy - vs.Height / 2);
                        rVol = new RectangleF(rx - 6 * s, 0, vs.Width + 12 * s, H);
                        rx -= 16 * s;

                        string wifi = "";
                        var ws = g.MeasureString(wifi, fIcon); rx -= ws.Width;
                        g.DrawString(wifi, fIcon, net ? fgB : dimB, rx, cy - ws.Height / 2);
                        if (!net) using (var pen = new Pen(dim, 1.4f * s)) g.DrawLine(pen, rx, cy + ws.Height / 3, rx + ws.Width, cy - ws.Height / 3);
                        rWifi = new RectangleF(rx - 6 * s, 0, ws.Width + 12 * s, H);
                        rx -= 16 * s;

                        // sol / luna: en oscuro muestra la luna, en claro el sol. Un clic cambia el modo.
                        string th = light ? "" : "";
                        var ts = g.MeasureString(th, fIcon); rx -= ts.Width;
                        g.DrawString(th, fIcon, fgB, rx, cy - ts.Height / 2);
                        rTheme = new RectangleF(rx - 6 * s, 0, ts.Width + 12 * s, H);
                        rx -= 16 * s;

                        // ^ : iconos de la bandeja del sistema (apps en segundo plano)
                        string tr = "\uE70E";
                        var trs = g.MeasureString(tr, fIcon); rx -= trs.Width;
                        g.DrawString(tr, fIcon, fgB, rx, cy - trs.Height / 2);
                        rTray = new RectangleF(rx - 6 * s, 0, trs.Width + 12 * s, H);
                        rx -= 24 * s;

                        // titulo de la ventana, atenuado, en el espacio que sobra
                        if (title != "" && title != appName && rx - x > 80 * s)
                        {
                            string t = title;
                            float maxW = Math.Min(rx - x, W * 0.45f);
                            var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                            g.DrawString(t, fReg, dimB, new RectangleF(x, cy - fReg.Height / 2f - 1, maxW, fReg.Height + 2), sf);
                        }
                    }
                }
                Push(bmp);
            }
        }

        static string ClockText()
        {
            var n = DateTime.Now;
            string[] d = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };
            string[] m = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sept", "oct", "nov", "dic" };
            return d[(int)n.DayOfWeek] + " " + n.Day + " " + m[n.Month - 1] + "   " + n.ToString("HH:mm");
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float dd = rad * 2;
            p.AddArc(r.X, r.Y, dd, dd, 180, 90); p.AddArc(r.Right - dd, r.Y, dd, dd, 270, 90);
            p.AddArc(r.Right - dd, r.Bottom - dd, dd, dd, 0, 90); p.AddArc(r.X, r.Bottom - dd, dd, dd, 90, 90);
            p.CloseFigure(); return p;
        }

        void Push(Bitmap bmp)
        {
            IntPtr screenDc = N.GetDC(IntPtr.Zero), memDc = N.CreateCompatibleDC(screenDc), hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = bmp.GetHbitmap(Color.FromArgb(0)); old = N.SelectObject(memDc, hb);
                var size = new N.SIZE { cx = W, cy = H }; var src = new N.POINT(); var dst = new N.POINT { x = X, y = Y };
                var bl = new N.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                N.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bl, 2);
            }
            finally
            {
                N.ReleaseDC(IntPtr.Zero, screenDc);
                if (hb != IntPtr.Zero) { N.SelectObject(memDc, old); N.DeleteObject(hb); }
                N.DeleteDC(memDc);
            }
        }

        // ---------- clics ----------
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            try
            {
                if (rTheme.Contains(e.Location)) { if (ToggleTheme != null) ToggleTheme(); }
                else if (rTray.Contains(e.Location)) { if (ShowTray != null) ShowTray(); }
                else if (rLogo.Contains(e.Location)) SystemMenu();
                else if (rApp.Contains(e.Location)) AppMenu();
                else if (rWifi.Contains(e.Location) || rVol.Contains(e.Location) || rBat.Contains(e.Location)) N.PressKeys(0x5B, 0x41);
                else if (rClock.Contains(e.Location)) N.PressKeys(0x5B, 0x4E);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        ContextMenuStrip NewMenu()
        {
            return MenuStyle.New(light);
        }

        static void Item(ContextMenuStrip m, string text, Action a)
        {
            var mi = new ToolStripMenuItem(text);
            mi.Click += (o, e) => { try { a(); } catch (Exception ex) { Program.Log(ex); } };
            m.Items.Add(mi);
        }

        static bool Confirm(string what)
        {
            return MessageBox.Show("¿Seguro que quieres " + what + "? Guarda tu trabajo antes.", "MacDock", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        void SystemMenu()
        {
            var m = NewMenu();
            Item(m, "Menú Inicio", () => N.PressKeys(0x5B));
            Item(m, "Acerca de este equipo", () => Process.Start("ms-settings:about"));
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Configuración", () => Process.Start("ms-settings:"));
            Item(m, "Administrador de tareas", () => Process.Start("taskmgr.exe"));
            Item(m, "Ajustes de Windows (MacDock)", () =>
            {
                string ps = Path.Combine(Program.DataDir, "ajustes.ps1");
                if (File.Exists(ps)) Process.Start(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -ExecutionPolicy Bypass -File \"" + ps + "\"");
            });
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Suspender", () => Application.SetSuspendState(PowerState.Suspend, false, false));
            Item(m, "Reiniciar...", () => { if (Confirm("reiniciar")) Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); });
            Item(m, "Apagar...", () => { if (Confirm("apagar")) Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); });
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Bloquear pantalla", () => N.PressKeys(0x5B, 0x4C));
            Item(m, "Cerrar sesión...", () => { if (Confirm("cerrar sesión")) Process.Start(new ProcessStartInfo("shutdown.exe", "/l") { CreateNoWindow = true, UseShellExecute = false }); });
            Show(m, rLogo);
        }

        void AppMenu()
        {
            IntPtr h = fgWnd;
            if (h == IntPtr.Zero || appName == "Escritorio") return;
            var m = NewMenu();
            Item(m, "Minimizar", () => N.ShowWindow(h, 6));
            Item(m, N.IsZoomed(h) ? "Restaurar tamaño" : "Maximizar", () => N.ShowWindow(h, N.IsZoomed(h) ? 9 : 3));
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Cerrar ventana", () => N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero));
            Show(m, rApp);
        }

        void Show(ContextMenuStrip m, RectangleF r)
        {
            N.SetForegroundWindow(Handle);
            m.Show(new Point(X + (int)r.X, Y + H));
        }

        // ---------- reservar el espacio arriba (como Mac) ----------
        void RegisterAppBar()
        {
            var abd = new N.APPBARDATA(); abd.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA));
            abd.hWnd = Handle; abd.uCallbackMessage = appBarMsg;
            N.SHAppBarMessage(0, ref abd);
            appBarOn = true;
            SetPos();
        }

        void SetPos()
        {
            if (!appBarOn) return;
            var b = Screen.PrimaryScreen.Bounds;
            var abd = new N.APPBARDATA(); abd.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA));
            abd.hWnd = Handle; abd.uEdge = 1;
            abd.rc.left = b.Left; abd.rc.right = b.Right; abd.rc.top = b.Top; abd.rc.bottom = b.Top + BarHeight;
            N.SHAppBarMessage(2, ref abd);
            abd.rc.bottom = abd.rc.top + BarHeight;
            N.SHAppBarMessage(3, ref abd);
        }

        public void ReRegister() { if (appBarOn) { Unregister(); } RegisterAppBar(); Redraw(true); }

        void Unregister()
        {
            if (!appBarOn) return;
            var abd = new N.APPBARDATA(); abd.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA)); abd.hWnd = Handle;
            N.SHAppBarMessage(1, ref abd);
            appBarOn = false;
        }

        public void OnDisplayChanged()
        {
            var b = Screen.PrimaryScreen.Bounds;
            W = b.Width; H = BarHeight; X = b.Left; Y = b.Top;
            SetPos(); Redraw(true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == appBarMsg && appBarMsg != 0 && m.WParam.ToInt32() == 1) SetPos();
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { timer.Stop(); if (hook != IntPtr.Zero) UnhookWinEvent(hook); hook = IntPtr.Zero; Unregister(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
