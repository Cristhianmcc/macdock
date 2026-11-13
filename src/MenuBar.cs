using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacDock
{
    // Barra superior estilo Mac: logo (menu del sistema), app activa, y a la derecha Wi-Fi, volumen, bateria, Centro de Control y fecha/hora.
    // Soporta multiples monitores y se dibuja solo cuando algo cambia (casi cero CPU).
    class MenuBar : Form
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);

        float s; bool light;
        Screen targetScreen;
        bool isSecondary;
        List<MenuBar> secondaryBars = new List<MenuBar>();
        bool multiMonitor;
        int W, H, X, Y;
        bool full, appBarOn;
        int appBarMsg;
        WinEventProc proc; IntPtr hook;
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        string appName = "Escritorio", title = "", lastKey = "";
        IntPtr fgWnd = IntPtr.Zero;
        RectangleF rLogo, rApp, rWinMenu, rWifi, rVol, rBat, rCc, rClock, rTheme, rTray;
        public Action ToggleTheme;   // lo conecta el dock: cambia claro/oscuro
        public Action ShowTray;      // lo conecta el dock: muestra los iconos de la bandeja (Surfshark, Drive...)

        // Apps en segundo plano dibujadas en la barra, como los iconos de la barra de menus de Mac.
        // Se leen en otro hilo cada 10 s; null = no hay lista (Windows 10) y queda el boton ^ con el panel de Windows.
        List<TrayApp> trayApps = new List<TrayApp>(); bool trayUnsupported, trayLoading; string traySig = "";
        readonly List<KeyValuePair<RectangleF, TrayApp>> trayHits = new List<KeyValuePair<RectangleF, TrayApp>>();
        RectangleF rTrayMore; List<TrayApp> trayOverflow = new List<TrayApp>();
        const int TrayMaxShown = 6;
        public bool TrayIcons = true;      // opcion del dock: apps en segundo plano en la barra (si no, el boton ^ de Windows)
        public bool NotifCenterOn = true;  // opcion del dock: clic en la fecha abre el Centro de Notificaciones (si no, el de Windows)

        public void SetOptions(bool trayIcons, bool notifCenter)
        {
            TrayIcons = trayIcons; NotifCenterOn = notifCenter;
            if (TrayIcons) RefreshTrayAsync();
            Redraw(true);
            foreach (var sb in secondaryBars) sb.SetOptions(trayIcons, notifCenter);
        }

        void RefreshTrayAsync()
        {
            if (trayLoading || trayUnsupported || !TrayIcons) return;
            trayLoading = true;
            int px = (int)Math.Round(16 * s);
            var th = new System.Threading.Thread(() =>
            {
                List<TrayApp> list = null;
                try { list = TrayApp.Read(); if (list != null) foreach (var a in list) a.PrepareGlyph(px); } catch (Exception ex) { Program.Log(ex); }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        trayLoading = false;
                        if (IsDisposed) return;
                        if (list == null) { trayUnsupported = true; Redraw(true); return; }
                        string sig = string.Join("|", list.Select(a => a.Name).ToArray());
                        if (sig == traySig) { foreach (var a in list) if (a.Icon != null) a.Icon.Dispose(); foreach (var a in trayApps) { var n = list.FirstOrDefault(x => x.Name == a.Name); if (n != null) a.Pids = n.Pids; } return; }
                        foreach (var a in trayApps) if (a.Icon != null) a.Icon.Dispose();
                        trayApps = list; traySig = sig;
                        Redraw(true);
                    }));
                }
                catch { trayLoading = false; }
            });
            th.IsBackground = true; th.Start();
        }
        Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;

        public MenuBar(float scale, bool lightTheme, Screen scr = null, bool secondary = false, bool multiMon = true)
        {
            s = scale; light = lightTheme;
            targetScreen = scr ?? Screen.PrimaryScreen;
            isSecondary = secondary;
            multiMonitor = multiMon;
            Text = secondary ? "MacDockMenuBarSec" : "MacDockMenuBar";
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(0, 0, 1, 1);
            appBarMsg = N.RegisterWindowMessage("MacDockMenuBarMsg_" + targetScreen.DeviceName.GetHashCode());
            timer.Interval = 10000; timer.Tick += (o, e) => { Redraw(false); RefreshTrayAsync(); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8000000 | 0x8; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public int BarHeight { get { return (int)Math.Round(26 * s); } }
        public Screen CurrentScreen { get { return targetScreen; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var b = targetScreen.Bounds;
            W = b.Width; H = BarHeight; X = b.Left; Y = b.Top;
            RegisterAppBar();
            if (!isSecondary)
            {
                proc = OnWinEvent;
                hook = SetWinEventHook(3, 3, IntPtr.Zero, proc, 0, 0, 0);      // EVENT_SYSTEM_FOREGROUND
                NetworkChange.NetworkAvailabilityChanged += (o, a) => { try { BeginInvoke(new Action(() => { Redraw(true); foreach (var sb in secondaryBars) sb.Redraw(true); })); } catch { } };
                NetworkChange.NetworkAddressChanged += (o, a) => { try { BeginInvoke(new Action(() => { Redraw(true); foreach (var sb in secondaryBars) sb.Redraw(true); })); } catch { } };
                SyncSecondaryBars();
            }
            UpdateActive(Win32Fg());
            timer.Start();
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerChanged;   // conectar o quitar el cargador se ve al instante
            RefreshTrayAsync();   // iconos de las apps en segundo plano desde el inicio
        }

        void OnPowerChanged(object o, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode != Microsoft.Win32.PowerModes.StatusChange) return;
            try { BeginInvoke(new Action(() => Redraw(true))); } catch { }
        }

        public void SetMultiMonitor(bool on)
        {
            if (isSecondary) return;
            multiMonitor = on;
            SyncSecondaryBars();
        }

        void SyncSecondaryBars()
        {
            if (isSecondary) return;
            foreach (var sb in secondaryBars) { try { sb.Close(); sb.Dispose(); } catch { } }
            secondaryBars.Clear();
            if (!multiMonitor) return;
            foreach (var scr in Screen.AllScreens)
            {
                if (scr.Primary) continue;
                float sc = s;
                var sb = new MenuBar(sc, light, scr, true, false);
                sb.ToggleTheme = ToggleTheme;
                sb.ShowTray = ShowTray;
                sb.TrayIcons = TrayIcons; sb.NotifCenterOn = NotifCenterOn;
                sb.Show();
                secondaryBars.Add(sb);
            }
        }

        // Menu de una app de la barra, como el de los iconos de la barra de menus de Mac
        void TrayAppMenu(KeyValuePair<RectangleF, TrayApp> hit)
        {
            var a = hit.Value;
            var m = MenuStyle.New(light);
            Item(m, "Abrir " + a.Name, a.Open);
            Item(m, "Menú de " + a.Name + "…", () => { if (ShowTray != null) ShowTray(); });   // ahi esta su "Salir" y sus opciones
            if (a.Pids.Count > 0)
            {
                m.Items.Add(new ToolStripSeparator());
                Item(m, "Forzar cierre", () =>
                {
                    if (MessageBox.Show("¿Forzar el cierre de " + a.Name + "?\n\nSe cerrará de inmediato y se perderá lo que no hayas guardado.", "MacDock",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes) { a.ForceQuit(); RefreshTrayAsync(); }
                });
            }
            m.Show(new Point(X + (int)hit.Key.X, Y + H));
        }

        // » : las apps que no caben en la barra
        void TrayOverflowMenu()
        {
            var m = MenuStyle.New(light);
            foreach (var a in trayOverflow)
            {
                var app = a;
                var mi = new ToolStripMenuItem(app.Name);
                if (app.Icon != null && !app.IsTemplate) mi.Image = app.Icon;
                mi.Click += (o, e) => { try { app.Open(); } catch (Exception ex) { Program.Log(ex); } };
                m.Items.Add(mi);
            }
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Menús de las apps…", () => { if (ShowTray != null) ShowTray(); });
            m.Show(new Point(X + (int)rTrayMore.X, Y + H));
        }

        static IntPtr Win32Fg() { return N.GetForegroundWindow(); }

        void OnWinEvent(IntPtr hk, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0) return;
            UpdateActive(hwnd);
            foreach (var sb in secondaryBars) sb.UpdateActive(hwnd);
        }

        public void UpdateActive(IntPtr h)
        {
            try
            {
                if (h == IntPtr.Zero || h == Handle) return;
                uint pid; N.GetWindowThreadProcessId(h, out pid);
                if (pid == myPid) return;
                string cls = N.ClassOf(h);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow" || cls == "XamlExplorerHostIslandWindow" || cls == "#32768") return;
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

        public void CheckFullscreenState()
        {
            SetFullOne(FullscreenApp.Active(Handle, targetScreen));
            foreach (var sb in secondaryBars) sb.SetFullOne(FullscreenApp.Active(sb.Handle, sb.targetScreen));
        }

        void SetFullOne(bool f)
        {
            if (f == full) return;
            full = f;
            if (full)
            {
                N.ShowWindow(Handle, 0);
            }
            else
            {
                N.ShowWindow(Handle, 8);
                N.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                Redraw(true);
            }
        }

        public void SetFull(bool f)
        {
            CheckFullscreenState();
        }

        public void SetTheme(bool lightTheme)
        {
            light = lightTheme;
            Redraw(true);
            foreach (var sb in secondaryBars) sb.SetTheme(lightTheme);
        }

        // ---------- dibujo ----------
        public void Redraw(bool force)
        {
            if (full || W <= 0 || IsDisposed) return;
            var bat = BatteryGlyph.Read();
            bool net = false, isEth = false; string netLbl;
            SystemStats.GetNetworkInfo(out net, out isEth, out netLbl);
            bool isDesk = DeviceProfile.IsDesktop;
            string clock = ClockText();
            string key = appName + "|" + title + "|" + clock + "|" + (isDesk ? -1 : (int)(bat.Pct * 100)) + bat.Plugged + bat.Charging + "|" + net + "|" + isEth + "|" + light + "|" + traySig + trayUnsupported + TrayIcons;
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
                        // izquierda: logo + app activa + menu Ventana (macOS Sequoia) + titulo
                        float x = 14 * s;
                        float lq = 5.5f * s, lg = 1.4f * s, ly = cy - lq - lg / 2;
                        rLogo = new RectangleF(x - 8 * s, 0, lq * 2 + lg + 16 * s, H);
                        g.FillRectangle(fgB, x, ly, lq, lq); g.FillRectangle(fgB, x + lq + lg, ly, lq, lq);
                        g.FillRectangle(fgB, x, ly + lq + lg, lq, lq); g.FillRectangle(fgB, x + lq + lg, ly + lq + lg, lq, lq);
                        x += lq * 2 + lg + 20 * s;

                        var an = g.MeasureString(appName, fBold);
                        rApp = new RectangleF(x - 8 * s, 0, an.Width + 14 * s, H);
                        g.DrawString(appName, fBold, fgB, x, cy - an.Height / 2);
                        x += an.Width + 14 * s;

                        if (appName != "Escritorio")
                        {
                            var wn = g.MeasureString("Ventana", fReg);
                            rWinMenu = new RectangleF(x - 6 * s, 0, wn.Width + 12 * s, H);
                            g.DrawString("Ventana", fReg, fgB, x, cy - wn.Height / 2);
                            x += wn.Width + 16 * s;
                        }
                        else rWinMenu = RectangleF.Empty;

                        // derecha: reloj, Centro de Control, bateria (solo laptop), volumen, red (Ethernet o Wi-Fi)
                        float rx = W - 14 * s;
                        var cs = g.MeasureString(clock, fReg);
                        rx -= cs.Width;
                        g.DrawString(clock, fReg, fgB, rx, cy - cs.Height / 2);
                        rClock = new RectangleF(rx - 8 * s, 0, cs.Width + 16 * s, H);
                        rx -= 16 * s;

                        // Icono Centro de Control estilo macOS (dos interruptores tipo pildora)
                        float ccW = 15 * s, ccH = 12 * s;
                        rx -= ccW;
                        DrawControlCenterIcon(g, rx, cy - ccH / 2, ccW, ccH, fg, s);
                        rCc = new RectangleF(rx - 6 * s, 0, ccW + 12 * s, H);
                        rx -= 16 * s;

                        if (!isDesk && bat.HasBattery)
                        {
                            string pt = (int)Math.Round(bat.Pct * 100) + "%";
                            float bw = 22 * s, bh = 11 * s;
                            rx -= bw + 2 * s;
                            float by = cy - bh / 2;
                            // con el cargador conectado aparece un rayo, como en Mac
                            BatteryGlyph.Draw(g, new RectangleF(rx, by, bw, bh), bat, fg, dim, light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(28, 28, 32), s);
                            var pts = g.MeasureString(pt, fReg);
                            rx -= pts.Width + 3 * s;
                            g.DrawString(pt, fReg, fgB, rx, cy - pts.Height / 2);
                            rBat = new RectangleF(rx - 6 * s, 0, pts.Width + bw + 18 * s, H);
                            rx -= 18 * s;
                        }
                        else rBat = RectangleF.Empty;

                        string vol = "\uE767";
                        var vs = g.MeasureString(vol, fIcon); rx -= vs.Width;
                        g.DrawString(vol, fIcon, fgB, rx, cy - vs.Height / 2);
                        rVol = new RectangleF(rx - 6 * s, 0, vs.Width + 12 * s, H);
                        rx -= 16 * s;

                        string netGlyph = (net && isEth) ? "\uE839" : "\uE701"; // \uE839 = Ethernet LAN, \uE701 = Wi-Fi
                        var ws = g.MeasureString(netGlyph, fIcon); rx -= ws.Width;
                        g.DrawString(netGlyph, fIcon, net ? fgB : dimB, rx, cy - ws.Height / 2);
                        if (!net) using (var pen = new Pen(dim, 1.4f * s)) g.DrawLine(pen, rx, cy + ws.Height / 3, rx + ws.Width, cy - ws.Height / 3);
                        rWifi = new RectangleF(rx - 6 * s, 0, ws.Width + 12 * s, H);
                        rx -= 16 * s;

                        // sol / luna: en oscuro muestra la luna, en claro el sol. Un clic cambia el modo.
                        string th = light ? "\uE706" : "\uE708";
                        var ts = g.MeasureString(th, fIcon); rx -= ts.Width;
                        g.DrawString(th, fIcon, fgB, rx, cy - ts.Height / 2);
                        rTheme = new RectangleF(rx - 6 * s, 0, ts.Width + 12 * s, H);
                        rx -= 16 * s;

                        // apps en segundo plano: sus iconos directo en la barra, como en Mac (de un color si el icono lo permite)
                        trayHits.Clear(); rTray = RectangleF.Empty; rTrayMore = RectangleF.Empty; trayOverflow = new List<TrayApp>();
                        if (trayUnsupported || !TrayIcons)
                        {
                            string tr = "\uE70E";   // Windows 10: el boton ^ abre el panel de Windows
                            var trs = g.MeasureString(tr, fIcon); rx -= trs.Width;
                            g.DrawString(tr, fIcon, fgB, rx, cy - trs.Height / 2);
                            rTray = new RectangleF(rx - 6 * s, 0, trs.Width + 12 * s, H);
                            rx -= 24 * s;
                        }
                        else
                        {
                            float isz = 16 * s, slot = isz + 14 * s;
                            var shown = trayApps.Take(trayApps.Count > TrayMaxShown ? TrayMaxShown - 1 : TrayMaxShown).ToList();
                            trayOverflow = trayApps.Skip(shown.Count).ToList();
                            if (trayOverflow.Count > 0)
                            {
                                string more = "\u00BB";   // » : las que no caben
                                var ms = g.MeasureString(more, fReg); rx -= ms.Width;
                                g.DrawString(more, fReg, fgB, rx, cy - ms.Height / 2);
                                rTrayMore = new RectangleF(rx - 6 * s, 0, ms.Width + 12 * s, H);
                                rx -= 10 * s;
                            }
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            foreach (var a in shown)
                            {
                                rx -= slot;
                                var ir = new RectangleF(rx + (slot - isz) / 2, cy - isz / 2, isz, isz);
                                a.Draw(g, ir, fg);
                                trayHits.Add(new KeyValuePair<RectangleF, TrayApp>(new RectangleF(rx, 0, slot, H), a));
                            }
                            rx -= 16 * s;
                        }

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

        static void DrawControlCenterIcon(Graphics g, float x, float y, float w, float h, Color fg, float s)
        {
            float ph = h * 0.42f;
            using (var pen = new Pen(fg, Math.Max(1f, 1.15f * s)))
            using (var b = new SolidBrush(fg))
            {
                var r1 = new RectangleF(x, y, w, ph);
                var r2 = new RectangleF(x, y + h - ph, w, ph);
                using (var p1 = Round(r1, ph / 2)) g.DrawPath(pen, p1);
                using (var p2 = Round(r2, ph / 2)) g.DrawPath(pen, p2);
                float d = ph - 1.6f * s;
                g.FillEllipse(b, x + w - d - 0.8f * s, y + 0.8f * s, d, d);
                g.FillEllipse(b, x + 0.8f * s, y + h - ph + 0.8f * s, d, d);
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
            if (dd <= 0) { p.AddRectangle(r); return p; }
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
                else if (!rTray.IsEmpty && rTray.Contains(e.Location)) { if (ShowTray != null) ShowTray(); }
                else if (!rTrayMore.IsEmpty && rTrayMore.Contains(e.Location)) TrayOverflowMenu();
                else if (trayHits.Any(t => t.Key.Contains(e.Location))) TrayAppMenu(trayHits.First(t => t.Key.Contains(e.Location)));
                else if (rLogo.Contains(e.Location)) SystemMenu();
                else if (rApp.Contains(e.Location)) AppMenu();
                else if (!rWinMenu.IsEmpty && rWinMenu.Contains(e.Location)) WindowTilingMenu(rWinMenu);
                else if (rCc.Contains(e.Location) || rWifi.Contains(e.Location) || rVol.Contains(e.Location) || rBat.Contains(e.Location))
                {
                    var anchor = new Point(X + W - (int)(12 * s), Y + H + (int)(6 * s));
                    ControlCenter.Toggle(s, light, anchor, targetScreen.Bounds, true, ToggleTheme);
                }
                else if (rClock.Contains(e.Location))   // como en Mac: la fecha abre el Centro de Notificaciones
                {
                    if (NotifCenterOn) NotificationCenter.Toggle(s, light, targetScreen.WorkingArea); else N.PressKeys(0x5B, 0x4E);
                }
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
            Item(m, "Centro de Control...", () => ControlCenter.Toggle(s, light, new Point(X + (int)(200 * s), Y + H + (int)(6 * s)), targetScreen.Bounds, true, ToggleTheme));
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
            AddTilingItems(m, h);
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Minimizar", () => N.ShowWindow(h, 6));
            Item(m, N.IsZoomed(h) ? "Restaurar tamaño" : "Maximizar", () => N.ShowWindow(h, N.IsZoomed(h) ? 9 : 3));
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Cerrar ventana", () => N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero));
            Show(m, rApp);
        }

        void WindowTilingMenu(RectangleF anchorRect)
        {
            IntPtr h = fgWnd;
            if (h == IntPtr.Zero || appName == "Escritorio") return;
            var m = NewMenu();
            AddTilingItems(m, h);
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Minimizar", () => N.ShowWindow(h, 6));
            Item(m, N.IsZoomed(h) ? "Restaurar tamaño" : "Maximizar", () => N.ShowWindow(h, N.IsZoomed(h) ? 9 : 3));
            Show(m, anchorRect);
        }

        // Acomodo de Ventanas estilo macOS Sequoia (Window Tiling con compensacion de bordes invisibles DWM de Windows 11)
        void AddTilingItems(ContextMenuStrip m, IntPtr h)
        {
            Item(m, "Mitad izquierda", () => TileWindow(h, 1));
            Item(m, "Mitad derecha", () => TileWindow(h, 2));
            Item(m, "Mitad superior", () => TileWindow(h, 3));
            Item(m, "Mitad inferior", () => TileWindow(h, 4));

            var quarters = new ToolStripMenuItem("Cuadrantes (Esquinas)");
            Action<string, int> addQ = (txt, mode) =>
            {
                var qi = new ToolStripMenuItem(txt);
                qi.Click += (o, e) => TileWindow(h, mode);
                quarters.DropDownItems.Add(qi);
            };
            addQ("Superior izquierda", 5);
            addQ("Superior derecha", 6);
            addQ("Inferior izquierda", 7);
            addQ("Inferior derecha", 8);
            m.Items.Add(quarters);

            m.Items.Add(new ToolStripSeparator());
            Item(m, "Centrar ventana (75%)", () => TileWindow(h, 9));
            Item(m, "Ocupar espacio de trabajo (Zoom Mac)", () => TileWindow(h, 10));
            if (Screen.AllScreens.Length > 1)
                Item(m, "Mover al siguiente monitor", () => MoveWindowNextScreen(h));
        }

        static void TileWindow(IntPtr h, int mode)
        {
            try
            {
                if (h == IntPtr.Zero) return;
                if (N.IsIconic(h) || N.IsZoomed(h)) N.ShowWindow(h, 9); // SW_RESTORE
                var scr = Screen.FromHandle(h);
                var wa = scr.WorkingArea;
                int x = wa.Left, y = wa.Top, w = wa.Width, hh = wa.Height;
                int hw = w / 2, hh2 = hh / 2;
                switch (mode)
                {
                    case 1: w = hw; break;                              // Mitad izquierda
                    case 2: x += hw; w = w - hw; break;                 // Mitad derecha
                    case 3: hh = hh2; break;                            // Mitad superior
                    case 4: y += hh2; hh = hh - hh2; break;             // Mitad inferior
                    case 5: w = hw; hh = hh2; break;                    // Superior izquierda
                    case 6: x += hw; w = w - hw; hh = hh2; break;       // Superior derecha
                    case 7: w = hw; y += hh2; hh = hh - hh2; break;     // Inferior izquierda
                    case 8: x += hw; w = w - hw; y += hh2; hh = hh - hh2; break; // Inferior derecha
                    case 9:                                             // Centrar 75%
                        w = (int)(wa.Width * 0.76f);
                        hh = (int)(wa.Height * 0.80f);
                        x = wa.Left + (wa.Width - w) / 2;
                        y = wa.Top + (wa.Height - hh) / 2;
                        break;
                    case 10: break;                                     // Todo el WorkingArea
                }
                SetWindowBoundsCompensated(h, x, y, w, hh);
                N.SetForegroundWindow(h);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        static void MoveWindowNextScreen(IntPtr h)
        {
            try
            {
                var screens = Screen.AllScreens;
                if (screens.Length < 2) return;
                var cur = Screen.FromHandle(h);
                int idx = 0;
                for (int i = 0; i < screens.Length; i++) if (screens[i].DeviceName == cur.DeviceName) { idx = i; break; }
                var next = screens[(idx + 1) % screens.Length];
                bool wasMax = N.IsZoomed(h);
                if (wasMax || N.IsIconic(h)) N.ShowWindow(h, 9);
                var wa = next.WorkingArea;
                int w = (int)(wa.Width * 0.82f), hh = (int)(wa.Height * 0.82f);
                SetWindowBoundsCompensated(h, wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - hh) / 2, w, hh);
                if (wasMax) N.ShowWindow(h, 3);
                N.SetForegroundWindow(h);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        static void SetWindowBoundsCompensated(IntPtr h, int x, int y, int w, int hh)
        {
            N.RECT win, ext;
            if (N.GetWindowRect(h, out win) && N.DwmGetWindowAttribute(h, 9, out ext, 16) == 0)
            {
                int dL = ext.left - win.left;
                int dT = ext.top - win.top;
                int dR = win.right - ext.right;
                int dB = win.bottom - ext.bottom;
                if (dL >= 0 && dL < 32 && dR >= 0 && dR < 32 && dB >= 0 && dB < 32)
                {
                    x -= dL; y -= dT; w += dL + dR; hh += dT + dB;
                }
            }
            N.SetWindowPos(h, IntPtr.Zero, x, y, w, hh, 0x0004 | 0x0020);
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
            var b = targetScreen.Bounds;
            var abd = new N.APPBARDATA(); abd.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA));
            abd.hWnd = Handle; abd.uEdge = 1;
            abd.rc.left = b.Left; abd.rc.right = b.Right; abd.rc.top = b.Top; abd.rc.bottom = b.Top + BarHeight;
            N.SHAppBarMessage(2, ref abd);
            abd.rc.bottom = abd.rc.top + BarHeight;
            N.SHAppBarMessage(3, ref abd);
        }

        public void ReRegister()
        {
            if (appBarOn) { Unregister(); }
            RegisterAppBar(); Redraw(true);
            foreach (var sb in secondaryBars) sb.ReRegister();
        }

        void Unregister()
        {
            if (!appBarOn) return;
            var abd = new N.APPBARDATA(); abd.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA)); abd.hWnd = Handle;
            N.SHAppBarMessage(1, ref abd);
            appBarOn = false;
        }

        public void OnDisplayChanged()
        {
            if (isSecondary) return;
            targetScreen = Screen.PrimaryScreen;
            var b = targetScreen.Bounds;
            W = b.Width; H = BarHeight; X = b.Left; Y = b.Top;
            SetPos(); Redraw(true);
            SyncSecondaryBars();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == appBarMsg && appBarMsg != 0 && m.WParam.ToInt32() == 1) SetPos();
            // WM_POWERBROADCAST / PBT_APMPOWERSTATUSCHANGE: por si el aviso de SystemEvents no llega
            else if (m.Msg == 0x0218 && m.WParam.ToInt32() == 0x0A) { try { BeginInvoke(new Action(() => Redraw(true))); } catch { } }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                timer.Stop();
                if (hook != IntPtr.Zero) UnhookWinEvent(hook);
                hook = IntPtr.Zero;
                Unregister();
                foreach (var sb in secondaryBars) { try { sb.Close(); sb.Dispose(); } catch { } }
                secondaryBars.Clear();
            }
            catch { }
            try { Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerChanged; } catch { }
            base.OnFormClosing(e);
        }
    }

    // Centro de Control recreado con el diseno modular Bento de macOS Sonoma / Sequoia:
    // Adaptativo PC de Escritorio vs Laptop, Red Ethernet/Wi-Fi, Bluetooth, Compartir, Modo Oscuro, Enfoque, Pantallas,
    // Modulo Multimedia ("Ahora suena"), deslizadores en vivo de Brillo (Laptop + Monitor Escritorio) y Sonido,
    // y Telemetria / Plan Alto Rendimiento (en Escritorio) o Bateria (en Laptop).
    class ControlCenter : Form
    {
        public static ControlCenter Current;
        static DateTime closedAt = DateTime.MinValue;

        public static void Toggle(float scale, bool light, Point anchor, Rectangle screenBounds, bool fromTop, Action toggleThemeAction)
        {
            if (Current != null && !Current.IsDisposed) { Current.Close(); return; }
            if ((DateTime.Now - closedAt).TotalMilliseconds < 250) return;
            Current = new ControlCenter(scale, light, anchor, screenBounds, fromTop, toggleThemeAction);
            Current.FormClosed += (o, e) => { closedAt = DateTime.Now; Current = null; };
            Current.Show();
            Current.Activate();
            N.SetForegroundWindow(Current.Handle);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        float s; bool light;
        Action onToggleTheme;
        float volume = 0.5f, brightness = 0.8f;
        bool muted = false, netOn = true, isEthernet = false, dndOn = false, isDesktop = true, highPerfPlan = false;
        // estado real de las antenas: -2 = leyendo, -1 = no hay, 0 = apagado, 1 = encendido
        int wifiState = -2, btState = -2;
        bool shareOn = false, busyWifi = false, busyBt = false;
        string wifiName = null, btDevice = null;   // red Wi-Fi conectada y aparato Bluetooth conectado
        string netLabel = "Conectado";
        string mediaTitle = "Reproductor multimedia", mediaApp = "Ahora suena";
        bool mediaPlaying = false;
        int cpuPct = 8, ramPct = 35;
        float ramUsedGb = 6f, ramTotalGb = 16f;
        int dragSlider = 0; // 1 = brillo, 2 = volumen
        int hoverPart = 0;
        RectangleF rConnCard, rWifiRow, rBtRow, rShareRow;
        RectangleF rDarkCard, rDndCard, rDispCard;
        RectangleF rBrightCard, rBrightTrack;
        RectangleF rSoundCard, rSoundTrack, rMuteBtn, rAudioOutBtn;
        RectangleF rMediaCard, rPrevBtn, rPlayBtn, rNextBtn;
        RectangleF rFootPower, rFootSnip;

        ControlCenter(float scale, bool lightTheme, Point anchor, Rectangle screen, bool fromTop, Action toggleTheme)
        {
            s = scale; light = lightTheme; onToggleTheme = toggleTheme;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
            BackColor = light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(30, 30, 36);

            int w = (int)(332 * s), h = (int)(430 * s);
            int x = fromTop ? Math.Min(screen.Right - w - (int)(10 * s), Math.Max(screen.Left + (int)(10 * s), anchor.X - w))
                            : Math.Min(screen.Right - w - (int)(10 * s), Math.Max(screen.Left + (int)(10 * s), anchor.X - w / 2));
            int y = fromTop ? anchor.Y : Math.Max(screen.Top + (int)(10 * s), anchor.Y - h - (int)(8 * s));
            Bounds = new Rectangle(x, y, w, h);

            RefreshState();
            LayoutCards();
        }

        void RefreshState()
        {
            isDesktop = DeviceProfile.IsDesktop;
            SystemStats.GetNetworkInfo(out netOn, out isEthernet, out netLabel);
            volume = AudioSessions.GetMasterVolume(out muted);
            brightness = DisplayBrightness.Get();
            ApplyMusic(NowPlaying.Last);   // lo ultimo conocido al instante; RefreshMediaSoon trae lo actual
            RefreshMediaSoon();
            if (isDesktop)
            {
                SystemStats.SampleCpuAndRam(out cpuPct, out ramPct, out ramUsedGb, out ramTotalGb);
                highPerfPlan = SystemStats.IsHighPerformancePlan();
            }
            dndOn = SysToggles.GetDnd();
            shareOn = SysToggles.GetNearShare();
        }

        // Las antenas se leen en segundo plano para que el panel abra al instante
        void ReadRadiosAsync()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                int w = SysToggles.GetRadio(SysToggles.WiFi), b = SysToggles.GetRadio(SysToggles.Bluetooth);
                bool n; bool eth; string lbl;
                SystemStats.GetNetworkInfo(out n, out eth, out lbl);
                string ssid = w == 1 ? SysToggles.GetWifiName() : null;
                var bts = b == 1 ? SysToggles.GetBluetoothConnected() : new List<string>();
                string btName = bts.Count == 0 ? null : bts.Count == 1 ? bts[0] : bts[0] + " +" + (bts.Count - 1);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        if (!busyWifi) { wifiState = w; wifiName = ssid; }
                        if (!busyBt) { btState = b; btDevice = btName; }
                        netOn = n; isEthernet = eth; netLabel = lbl;
                        Invalidate();
                    }));
                }
                catch { }
            });
        }

        void ToggleRadio(int kind)
        {
            bool isWifi = kind == SysToggles.WiFi;
            int cur = isWifi ? wifiState : btState;
            if (cur < 0) return;
            bool want = cur != 1;
            if (isWifi) { wifiState = want ? 1 : 0; busyWifi = true; } else { btState = want ? 1 : 0; busyBt = true; }
            Invalidate();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = SysToggles.SetRadio(kind, want);
                // al encender, se da tiempo a que se reconecte la red / los audifonos para mostrar su nombre
                if (want) System.Threading.Thread.Sleep(isWifi ? 2500 : 3500);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        busyWifi = false; busyBt = false;
                        if (!ok) MessageBox.Show(this, "Windows no dejo cambiar " + (isWifi ? "el Wi-Fi" : "el Bluetooth") + ".\nPuede que este bloqueado por el modo avion o por la configuracion del equipo.", "MacDock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        ReadRadiosAsync();
                    }));
                }
                catch { }
            });
        }

        void LayoutCards()
        {
            float p = 12 * s, gap = 9 * s;
            float colW = (Width - p * 2 - gap) / 2f;
            float topH = 134 * s;

            rConnCard = new RectangleF(p, p, colW, topH);
            float rh = topH / 3f;
            rWifiRow = new RectangleF(rConnCard.X, rConnCard.Y, colW, rh);
            rBtRow = new RectangleF(rConnCard.X, rConnCard.Y + rh, colW, rh);
            rShareRow = new RectangleF(rConnCard.X, rConnCard.Y + rh * 2, colW, rh);

            float rx = p + colW + gap;
            float halfH = (topH - gap) / 2f;
            rDarkCard = new RectangleF(rx, p, colW, halfH);
            float miniW = (colW - gap) / 2f;
            rDndCard = new RectangleF(rx, p + halfH + gap, miniW, halfH);
            rDispCard = new RectangleF(rx + miniW + gap, p + halfH + gap, miniW, halfH);

            float y = p + topH + gap;
            float slCardH = 66 * s;
            rBrightCard = new RectangleF(p, y, Width - p * 2, slCardH);
            rBrightTrack = new RectangleF(rBrightCard.X + 12 * s, rBrightCard.Y + 31 * s, rBrightCard.Width - 24 * s, 24 * s);

            y += slCardH + gap;
            rSoundCard = new RectangleF(p, y, Width - p * 2, slCardH);
            rSoundTrack = new RectangleF(rSoundCard.X + 12 * s, rSoundCard.Y + 31 * s, rSoundCard.Width - 54 * s, 24 * s);
            rMuteBtn = new RectangleF(rSoundTrack.X, rSoundTrack.Y, 28 * s, rSoundTrack.Height);
            rAudioOutBtn = new RectangleF(rSoundCard.Right - 34 * s, rSoundTrack.Y, 24 * s, 24 * s);

            y += slCardH + gap;
            float mediaH = 58 * s;
            rMediaCard = new RectangleF(p, y, Width - p * 2, mediaH);
            float btnSz = 28 * s, btnY = rMediaCard.Y + (mediaH - btnSz) / 2f;
            rNextBtn = new RectangleF(rMediaCard.Right - 10 * s - btnSz, btnY, btnSz, btnSz);
            rPlayBtn = new RectangleF(rNextBtn.X - 4 * s - btnSz, btnY, btnSz, btnSz);
            rPrevBtn = new RectangleF(rPlayBtn.X - 4 * s - btnSz, btnY, btnSz, btnSz);

            y += mediaH + gap;
            rFootPower = new RectangleF(Width - p - 154 * s, y + 1 * s, 88 * s, 25 * s);
            rFootSnip = new RectangleF(Width - p - 60 * s, y + 1 * s, 60 * s, 25 * s);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; cp.ClassStyle |= 0x20000; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDwmTheme();
            ReadRadiosAsync();   // aqui y no en el constructor: hace falta la ventana creada para devolver el resultado
        }

        void ApplyDwmTheme()
        {
            try
            {
                int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
                int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                int border = light ? 0x00CFCFD6 : 0x00484852; DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            catch { }
        }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color cardBg = light ? Color.FromArgb(250, 250, 253) : Color.FromArgb(44, 44, 52);
            Color cardBorder = light ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(32, 255, 255, 255);
            Color fg = light ? Color.FromArgb(24, 24, 28) : Color.FromArgb(242, 242, 246);
            Color sub = light ? Color.FromArgb(118, 118, 128) : Color.FromArgb(156, 156, 168);
            Color blue = Color.FromArgb(10, 132, 255);
            Color green = Color.FromArgb(48, 209, 88);
            Color purple = Color.FromArgb(94, 92, 230);
            Color orange = Color.FromArgb(255, 159, 10);
            Color offCircle = light ? Color.FromArgb(220, 220, 228) : Color.FromArgb(68, 68, 78);

            using (var fTitle = new Font(UiFont.Semi, 12f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fSub = new Font(UiFont.Reg, 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fMini = new Font(UiFont.Semi, 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fIcon = new Font("Segoe MDL2 Assets", 13.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fgB = new SolidBrush(fg)) using (var subB = new SolidBrush(sub))
            {
                // 1) Tarjeta Conectividad (Ethernet / Wi-Fi, Bluetooth, Compartir)
                DrawCard(g, rConnCard, cardBg, cardBorder);
                // sin antena Wi-Fi (PC de escritorio por cable): se muestra la conexion Ethernet
                bool showEth = wifiState == -1 && isEthernet;
                string connTitle = showEth ? "Ethernet" : "Wi-Fi";
                string connGlyph = showEth ? "\uE839" : "\uE701";
                string wifiSub = showEth ? netLabel
                               : wifiState == -2 ? "..."
                               : wifiState == -1 ? "No disponible"
                               : wifiState == 0 ? "Desactivado"
                               : busyWifi ? "Conectando..."
                               : wifiName ?? "Activado, sin conexion";
                bool wifiOn = showEth ? netOn : wifiState == 1;
                string btSub = btState == -2 ? "..." : btState == -1 ? "No disponible" : btState == 0 ? "Desactivado"
                             : busyBt ? "Activando..." : btDevice ?? "Activado";
                DrawConnRow(g, rWifiRow, connGlyph, connTitle, wifiSub, wifiOn, blue, offCircle, fg, fTitle, fSub, fIcon, hoverPart == 1);
                DrawConnRow(g, rBtRow, "\uE702", "Bluetooth", btSub, btState == 1, blue, offCircle, fg, fTitle, fSub, fIcon, hoverPart == 2);
                DrawConnRow(g, rShareRow, "\uE72D", "Compartir", shareOn ? "Mis dispositivos" : "Desactivado", shareOn, blue, offCircle, fg, fTitle, fSub, fIcon, hoverPart == 3);

                // 2) Tarjeta Modo Oscuro / Claro
                DrawCard(g, rDarkCard, cardBg, cardBorder);
                {
                    float d = 30 * s, cx = rDarkCard.X + 12 * s, cy = rDarkCard.Y + (rDarkCard.Height - d) / 2f;
                    using (var cb = new SolidBrush(!light ? blue : orange))
                        g.FillEllipse(cb, cx, cy, d, d);
                    var sfI = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(light ? "\uE706" : "\uE708", fIcon, Brushes.White, new RectangleF(cx, cy, d, d), sfI);
                    g.DrawString(light ? "Modo claro" : "Modo oscuro", fTitle, fgB, cx + d + 10 * s, cy + 1 * s);
                    g.DrawString("Cambiar tema", fSub, subB, cx + d + 10 * s, cy + 16 * s);
                }

                // 3) Mini tarjetas: Enfoque (No molestar) y Duplicar Pantalla
                DrawMiniCard(g, rDndCard, "\uE708", "Enfoque", dndOn, purple, cardBg, cardBorder, fg, fMini, fIcon);
                DrawMiniCard(g, rDispCard, "\uE7F4", "Pantallas", false, blue, cardBg, cardBorder, fg, fMini, fIcon);

                // 4) Tarjeta Pantalla (Brillo)
                DrawCard(g, rBrightCard, cardBg, cardBorder);
                g.DrawString(isDesktop ? "Pantalla (Monitor)" : "Pantalla", fTitle, fgB, rBrightCard.X + 12 * s, rBrightCard.Y + 8 * s);
                g.DrawString((int)Math.Round(brightness * 100) + "%", fSub, subB, rBrightCard.Right - 42 * s, rBrightCard.Y + 9 * s);
                DrawPillSlider(g, rBrightTrack, brightness, "\uE706", fIcon, light);

                // 5) Tarjeta Sonido (Volumen + Selector de Salida)
                DrawCard(g, rSoundCard, cardBg, cardBorder);
                g.DrawString("Sonido", fTitle, fgB, rSoundCard.X + 12 * s, rSoundCard.Y + 8 * s);
                g.DrawString(muted ? "Silenciado" : ((int)Math.Round(volume * 100) + "%"), fSub, subB, rSoundCard.Right - (muted ? 68 * s : 42 * s), rSoundCard.Y + 9 * s);
                string vGlyph = (muted || volume <= 0.01f) ? "\uE74F" : (volume < 0.45f ? "\uE993" : "\uE767");
                DrawPillSlider(g, rSoundTrack, muted ? 0f : volume, vGlyph, fIcon, light);
                using (var ob = new SolidBrush(offCircle)) g.FillEllipse(ob, rAudioOutBtn);
                var sfC = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("\uE7F5", fIcon, fgB, rAudioOutBtn, sfC);

                // 6) Tarjeta "Ahora suena" (Controles Multimedia)
                DrawCard(g, rMediaCard, cardBg, cardBorder);
                {
                    float d = 36 * s, cx = rMediaCard.X + 11 * s, cy = rMediaCard.Y + (rMediaCard.Height - d) / 2f;
                    using (var mb = new SolidBrush(mediaPlaying ? Color.FromArgb(255, 55, 95) : offCircle))
                        using (var mp = Round(new RectangleF(cx, cy, d, d), 8 * s)) g.FillPath(mb, mp);
                    g.DrawString("\uEC4F", fIcon, mediaPlaying ? Brushes.White : fgB, new RectangleF(cx, cy, d, d), sfC);

                    float tx = cx + d + 10 * s, maxTw = Math.Max(40 * s, rPrevBtn.X - tx - 6 * s);
                    var sfEll = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    g.DrawString(mediaTitle, fTitle, fgB, new RectangleF(tx, cy + 2 * s, maxTw, 18 * s), sfEll);
                    g.DrawString(mediaApp, fSub, subB, new RectangleF(tx, cy + 19 * s, maxTw, 16 * s), sfEll);

                    DrawCircleIconBtn(g, rPrevBtn, "\uE892", hoverPart == 4, offCircle, fgB, fIcon);
                    DrawCircleIconBtn(g, rPlayBtn, mediaPlaying ? "\uE769" : "\uE768", hoverPart == 5, offCircle, fgB, fIcon);
                    DrawCircleIconBtn(g, rNextBtn, "\uE893", hoverPart == 6, offCircle, fgB, fIcon);
                }

                // 7) Pie Adaptativo: PC de Escritorio (CPU/RAM + Plan Alto Rendimiento) vs Laptop (Bateria)
                if (isDesktop)
                {
                    string statTxt = string.Format("CPU {0}% · RAM {1:0.0}/{2:0} GB", cpuPct, ramUsedGb, ramTotalGb);
                    g.DrawString(statTxt, fSub, subB, 14 * s, rFootSnip.Y + 5 * s);
                    DrawPillButton(g, rFootPower, highPerfPlan ? "⚡ Turbo ON" : "Equilibrado", highPerfPlan ? green : cardBg, cardBorder, highPerfPlan ? Brushes.White : fgB, fSub);
                }
                else
                {
                    var ps = SystemInformation.PowerStatus;
                    float pct = ps.BatteryLifePercent;
                    string batTxt = (pct >= 0 && pct <= 1 && ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery)
                        ? ("Batería " + (int)Math.Round(pct * 100) + "% · " + (ps.PowerLineStatus == PowerLineStatus.Online ? "Cargando" : "Batería"))
                        : "Modo Laptop";
                    g.DrawString(batTxt, fSub, subB, 14 * s, rFootSnip.Y + 5 * s);
                    DrawPillButton(g, rFootPower, "Win + A", cardBg, cardBorder, fgB, fSub);
                }
                DrawPillButton(g, rFootSnip, "Captura", cardBg, cardBorder, fgB, fSub);
            }
        }

        void DrawCircleIconBtn(Graphics g, RectangleF r, string glyph, bool hov, Color hoverBg, Brush fgB, Font fIcon)
        {
            if (hov) using (var b = new SolidBrush(hoverBg)) g.FillEllipse(b, r);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, fIcon, fgB, r, sf);
        }

        void DrawCard(Graphics g, RectangleF r, Color bg, Color border)
        {
            using (var p = Round(r, 12 * s))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                using (var pen = new Pen(border, 1f)) g.DrawPath(pen, p);
            }
        }

        void DrawConnRow(Graphics g, RectangleF r, string glyph, string title, string status, bool on, Color onCol, Color offCol, Color fg, Font fTitle, Font fSub, Font fIcon, bool hov)
        {
            if (hov)
            {
                using (var p = Round(RectangleF.Inflate(r, -4 * s, -2 * s), 8 * s))
                using (var b = new SolidBrush(light ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(24, 255, 255, 255)))
                    g.FillPath(b, p);
            }
            float d = 28 * s, cx = r.X + 10 * s, cy = r.Y + (r.Height - d) / 2f;
            using (var b = new SolidBrush(on ? onCol : offCol)) g.FillEllipse(b, cx, cy, d, d);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var ib = new SolidBrush(on ? Color.White : fg)) g.DrawString(glyph, fIcon, ib, new RectangleF(cx, cy, d, d), sf);
            using (var fb = new SolidBrush(fg)) g.DrawString(title, fTitle, fb, cx + d + 9 * s, cy - 1 * s);
            using (var sb = new SolidBrush(light ? Color.FromArgb(115, 115, 125) : Color.FromArgb(150, 150, 162)))
                g.DrawString(status, fSub, sb, cx + d + 9 * s, cy + 14 * s);
        }

        void DrawMiniCard(Graphics g, RectangleF r, string glyph, string label, bool on, Color activeCol, Color cardBg, Color border, Color fg, Font fMini, Font fIcon)
        {
            DrawCard(g, r, on ? activeCol : cardBg, border);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var b = new SolidBrush(on ? Color.White : fg))
            {
                g.DrawString(glyph, fIcon, b, new RectangleF(r.X, r.Y + 6 * s, r.Width, 24 * s), sf);
                g.DrawString(label, fMini, b, new RectangleF(r.X, r.Bottom - 22 * s, r.Width, 18 * s), sf);
            }
        }

        void DrawPillSlider(Graphics g, RectangleF r, float val, string glyph, Font fIcon, bool isLight)
        {
            float rad = r.Height / 2f;
            Color trackBg = isLight ? Color.FromArgb(222, 222, 230) : Color.FromArgb(64, 64, 74);
            Color fillCol = isLight ? Color.FromArgb(255, 255, 255) : Color.FromArgb(240, 240, 245);
            using (var p = Round(r, rad))
            {
                using (var b = new SolidBrush(trackBg)) g.FillPath(b, p);
                float fw = Math.Max(r.Height, r.Width * Math.Max(0f, Math.Min(1f, val)));
                var oldClip = g.Clip;
                g.SetClip(p);
                using (var fp = Round(new RectangleF(r.X, r.Y, fw, r.Height), rad))
                using (var fb = new SolidBrush(fillCol)) g.FillPath(fb, fp);
                g.Clip = oldClip;
                using (var pen = new Pen(isLight ? Color.FromArgb(35, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255), 1f)) g.DrawPath(pen, p);
            }
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var ib = new SolidBrush(Color.FromArgb(95, 95, 105)))
                g.DrawString(glyph, fIcon, ib, new RectangleF(r.X + 4 * s, r.Y, 24 * s, r.Height), sf);
        }

        void DrawPillButton(Graphics g, RectangleF r, string text, Color bg, Color border, Brush fgB, Font f)
        {
            using (var p = Round(r, r.Height / 2f))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                using (var pen = new Pen(border, 1f)) g.DrawPath(pen, p);
            }
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, f, fgB, r, sf);
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        void SetSliderFromMouse(int which, int mx)
        {
            var tr = which == 1 ? rBrightTrack : rSoundTrack;
            float v = Math.Max(0f, Math.Min(1f, (mx - tr.X) / tr.Width));
            if (which == 1)
            {
                brightness = Math.Max(0.10f, v);
                DisplayBrightness.Set(brightness);
            }
            else
            {
                volume = v;
                muted = v <= 0.005f;
                AudioSessions.SetMasterVolume(volume, muted);
            }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (rMuteBtn.Contains(e.Location))
            {
                muted = !muted;
                AudioSessions.SetMasterVolume(volume, muted);
                Invalidate();
                return;
            }
            if (rBrightTrack.Contains(e.Location)) { dragSlider = 1; Capture = true; SetSliderFromMouse(1, e.X); return; }
            if (rSoundTrack.Contains(e.Location)) { dragSlider = 2; Capture = true; SetSliderFromMouse(2, e.X); return; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragSlider != 0) { SetSliderFromMouse(dragSlider, e.X); return; }
            int hp = rWifiRow.Contains(e.Location) ? 1 : rBtRow.Contains(e.Location) ? 2 : rShareRow.Contains(e.Location) ? 3
                   : rPrevBtn.Contains(e.Location) ? 4 : rPlayBtn.Contains(e.Location) ? 5 : rNextBtn.Contains(e.Location) ? 6 : 0;
            if (hp != hoverPart) { hoverPart = hp; Invalidate(); }
            bool hand = hp > 0 || rDarkCard.Contains(e.Location) || rDndCard.Contains(e.Location) || rDispCard.Contains(e.Location)
                     || rBrightTrack.Contains(e.Location) || rSoundTrack.Contains(e.Location) || rAudioOutBtn.Contains(e.Location)
                     || rFootPower.Contains(e.Location) || rFootSnip.Contains(e.Location);
            Cursor = hand ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragSlider != 0) { dragSlider = 0; Capture = false; return; }
            if (e.Button != MouseButtons.Left) return;
            try
            {
                // clic = encender/apagar ahi mismo (como en Mac). Si el equipo no lo tiene, abre la configuracion.
                if (rWifiRow.Contains(e.Location))
                {
                    if (wifiState >= 0) ToggleRadio(SysToggles.WiFi);
                    else if (wifiState == -1) { Close(); Process.Start(isEthernet ? "ms-settings:network-ethernet" : "ms-settings:network-wifi"); }
                }
                else if (rBtRow.Contains(e.Location))
                {
                    if (btState >= 0) ToggleRadio(SysToggles.Bluetooth);
                    else if (btState == -1) { Close(); Process.Start("ms-settings:bluetooth"); }
                }
                else if (rShareRow.Contains(e.Location)) { shareOn = !shareOn; SysToggles.SetNearShare(shareOn); Invalidate(); }
                else if (rDarkCard.Contains(e.Location))
                {
                    if (onToggleTheme != null) onToggleTheme();
                    light = !light;
                    BackColor = light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(30, 30, 36);
                    ApplyDwmTheme();
                    Invalidate();
                }
                else if (rDndCard.Contains(e.Location))
                {
                    // No molestar de verdad (el mismo de Windows): silencia y oculta las notificaciones
                    if (SysToggles.SetDnd(!dndOn)) dndOn = !dndOn;
                    Invalidate();
                }
                else if (rDispCard.Contains(e.Location))
                {
                    Close();
                    try { Process.Start("DisplaySwitch.exe"); } catch { Process.Start("ms-settings:display"); }
                }
                else if (rAudioOutBtn.Contains(e.Location))
                {
                    Close();
                    N.PressKeys(0x5B, 0x11, 0x56); // Win + Ctrl + V: selector nativo de salida de audio de Windows 11
                }
                else if (rPrevBtn.Contains(e.Location)) NowPlaying.Prev(RefreshMediaSoon);
                else if (rPlayBtn.Contains(e.Location)) { mediaPlaying = !mediaPlaying; Invalidate(); NowPlaying.PlayPause(RefreshMediaSoon); }
                else if (rNextBtn.Contains(e.Location)) NowPlaying.Next(RefreshMediaSoon);
                else if (rFootPower.Contains(e.Location))
                {
                    if (isDesktop)
                    {
                        highPerfPlan = SystemStats.ToggleHighPerformancePlan();
                        Invalidate();
                    }
                    else
                    {
                        Close();
                        N.PressKeys(0x5B, 0x41); // Win + A
                    }
                }
                else if (rFootSnip.Contains(e.Location))
                {
                    Hide();
                    var snipTimer = new System.Windows.Forms.Timer { Interval = 180 };
                    snipTimer.Tick += (s2, a2) => { snipTimer.Stop(); snipTimer.Dispose(); Close(); N.PressKeys(0x5B, 0x10, 0x53); };
                    snipTimer.Start();
                } // Win + Shift + S
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        // Pide a Windows que esta sonando (en segundo plano) y redibuja la tarjeta
        void RefreshMediaSoon()
        {
            if (!IsHandleCreated) { EventHandler h = null; h = (o, e) => { HandleCreated -= h; RefreshMediaSoon(); }; HandleCreated += h; return; }
            NowPlaying.Refresh(info => { try { BeginInvoke(new Action(() => { if (!IsDisposed) { ApplyMusic(info); Invalidate(); } })); } catch { } });
        }

        void ApplyMusic(NowPlayingInfo info)
        {
            if (info == null || !info.HasSession || info.Title == "") { mediaTitle = "Nada sonando"; mediaApp = "Spotify, YouTube Music, el navegador..."; mediaPlaying = false; return; }
            mediaTitle = info.Title;
            mediaApp = info.Artist != "" && info.App != "" ? info.Artist + " · " + info.App : (info.Artist != "" ? info.Artist : info.App);
            mediaPlaying = info.Playing;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float step = (e.Delta > 0 ? 0.05f : -0.05f);
            if (rBrightCard.Contains(e.Location))
            {
                brightness = Math.Max(0.10f, Math.Min(1f, brightness + step));
                DisplayBrightness.Set(brightness);
                Invalidate();
            }
            else if (rSoundCard.Contains(e.Location))
            {
                volume = Math.Max(0f, Math.Min(1f, volume + step));
                muted = volume <= 0.005f;
                AudioSessions.SetMasterVolume(volume, muted);
                Invalidate();
            }
        }
    }
}
