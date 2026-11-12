using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MacDock
{
    // Selector de apps estilo Cmd + Tab (con Alt + Tab): un icono por app, no por ventana, de la mas reciente a la mas antigua.
    // Mientras mantienes Alt:  Tab / Shift+Tab o flechas = moverse,  Q = cerrar la app,  H = minimizarla,  Esc = cancelar.
    // Al soltar Alt se abre la app elegida con todas sus ventanas al frente.
    // El teclado llega como atajos registrados del dock (RegisterHotKey en Dock.cs, sin ganchos de teclado);
    // soltar Alt se detecta aqui mirando la tecla cada pocos milisegundos. Esta ventana nunca se activa ni roba el foco.
    class AppSwitcher : Form
    {
        // Abierto: el dock registra las teclas de "dentro" (Esc, Q, H, flechas, Enter) mientras dure.
        public static volatile bool IsOpen;
        public static Action SwitcherClosed;   // el dock suelta esas teclas
        static AppSwitcher cur;

        class AppGroup { public string Key, Name, IconPath; public List<IntPtr> Windows = new List<IntPtr>(); }

        static readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly object cacheLock = new object();
        static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;
        static DateTime lastUsed = DateTime.MinValue;

        // Si no lo usas en un rato, suelta los iconos guardados (los vuelve a cargar la proxima vez).
        public static void TrimIfIdle(TimeSpan idle)
        {
            if (IsOpen || DateTime.Now - lastUsed < idle) return;
            lock (cacheLock) { if (iconCache.Count == 0) return; foreach (var b in iconCache.Values) if (b != null) b.Dispose(); iconCache.Clear(); }
        }

        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);

        readonly List<AppGroup> apps;
        int sel;
        readonly float s; readonly bool light;
        readonly int iconPx;
        Rectangle area;
        readonly System.Windows.Forms.Timer showTimer = new System.Windows.Forms.Timer { Interval = 110 };
        readonly System.Windows.Forms.Timer altWatch = new System.Windows.Forms.Timer { Interval = 25 };
        bool shown;

        // ===================== entrada desde el gancho de teclado (siempre en el hilo del dock) =====================
        public static void Step(int dir, bool light, float s)
        {
            try
            {
                if (cur == null || cur.IsDisposed)
                {
                    var list = RunningApps();
                    if (list.Count == 0) { IsOpen = false; return; }
                    IsOpen = true; lastUsed = DateTime.Now;
                    // Tecla "fantasma" sin uso: evita que al soltar Alt la app de enfrente active su barra de menus.
                    N.keybd_event(0xE8, 0, 0, IntPtr.Zero); N.keybd_event(0xE8, 0, 2, IntPtr.Zero);
                    cur = new AppSwitcher(list, light, s);
                    cur.sel = list.Count > 1 ? (dir > 0 ? 1 : list.Count - 1) : 0;
                    cur.showTimer.Start();   // un toque rapido de Alt+Tab cambia de app sin mostrar el panel
                    cur.altWatch.Start();
                    return;
                }
                cur.MoveSel(dir);
            }
            catch (Exception ex) { Program.Log(ex); Cancel(); }
        }

        public static void Commit()
        {
            var c = cur; cur = null; IsOpen = false; if (SwitcherClosed != null) SwitcherClosed();
            if (c == null || c.IsDisposed) return;
            var g = c.apps.Count > 0 ? c.apps[c.sel] : null;
            c.Shutdown();
            if (g != null) BringAppToFront(g);
        }

        public static void Cancel()
        {
            var c = cur; cur = null; IsOpen = false; if (SwitcherClosed != null) SwitcherClosed();
            if (c != null && !c.IsDisposed) c.Shutdown();
        }

        // Puede cerrarse antes de mostrarse (toque rapido de Alt+Tab): se paran los relojes y se libera siempre.
        void Shutdown()
        {
            showTimer.Stop(); altWatch.Stop();
            try { if (IsHandleCreated) Close(); } catch { }
            if (!IsDisposed) Dispose();
        }

        // Q: cierra la app elegida (todas sus ventanas), como Cmd + Q en el selector de Mac
        public static void QuitSelected() { WithSelected((c, g) => { foreach (var h in g.Windows) N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); c.RemoveSelected(); }); }

        // H: minimiza todas las ventanas de la app elegida (lo mas parecido a "ocultar" de Mac)
        public static void HideSelected() { WithSelected((c, g) => { foreach (var h in g.Windows) N.ShowWindow(h, 6); c.RemoveSelected(); }); }

        static void WithSelected(Action<AppSwitcher, AppGroup> a)
        {
            var c = cur;
            if (c == null || c.IsDisposed || c.apps.Count == 0) return;
            try { a(c, c.apps[c.sel]); } catch (Exception ex) { Program.Log(ex); }
        }

        // ===================== ventana =====================
        AppSwitcher(List<AppGroup> list, bool lightTheme, float scale)
        {
            apps = list; light = lightTheme; s = scale;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            var scr = Screen.FromPoint(Cursor.Position).Bounds;
            float maxW = scr.Width * 0.9f;
            iconPx = (int)Math.Min(96 * s, (maxW - Pad * 2) / Math.Max(1, apps.Count) - CellPad * 2);
            iconPx = Math.Max((int)(32 * s), iconPx);
            LayoutArea(scr);
            Bounds = area;
            showTimer.Tick += (o, e) => { showTimer.Stop(); if (IsDisposed || cur != this) return; shown = true; Render(); N.ShowWindow(Handle, 4); };
            // soltar Alt = abrir la app elegida
            altWatch.Tick += (o, e) => { if ((GetAsyncKeyState(0x12) & 0x8000) == 0 && cur == this) Commit(); };
            LoadIconsAsync();
        }

        float Pad { get { return 14 * s; } }
        float CellPad { get { return 10 * s; } }
        float Cell { get { return iconPx + CellPad * 2; } }
        float NameH { get { return 26 * s; } }

        void LayoutArea(Rectangle scr)
        {
            int w = (int)Math.Ceiling(Pad * 2 + Cell * apps.Count), h = (int)Math.Ceiling(Pad * 2 + Cell + NameH);
            area = new Rectangle(scr.Left + (scr.Width - w) / 2, scr.Top + (scr.Height - h) / 2, w, h);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x8000000;   // capas, sin Alt+Tab, siempre encima, nunca se activa
                return cp;
            }
        }

        void MoveSel(int d)
        {
            if (apps.Count == 0) return;
            sel = ((sel + d) % apps.Count + apps.Count) % apps.Count;
            if (shown) Render();
        }

        void RemoveSelected()
        {
            apps.RemoveAt(sel);
            if (apps.Count == 0) { Cancel(); return; }
            if (sel >= apps.Count) sel = apps.Count - 1;
            LayoutArea(Screen.FromRectangle(area).Bounds);
            if (shown) Render();
        }

        int HitIndex(Point p)
        {
            if (p.Y < Pad || p.Y > Pad + Cell) return -1;
            int i = (int)Math.Floor((p.X - Pad) / Cell);
            return i >= 0 && i < apps.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitIndex(e.Location);
            if (i >= 0 && i != sel) { sel = i; Render(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = HitIndex(e.Location);
            if (i >= 0 && cur == this) { sel = i; Commit(); }
        }

        void Render()
        {
            if (IsDisposed || area.Width <= 0) return;
            using (var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var panel = new RectangleF(0.5f, 0.5f, area.Width - 1, area.Height - 1);
                    using (var p = Round(panel, 18 * s))
                    {
                        using (var b = new SolidBrush(light ? Color.FromArgb(228, 238, 238, 242) : Color.FromArgb(222, 36, 36, 40))) g.FillPath(b, p);
                        using (var pen = new Pen(light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(60, 255, 255, 255), Math.Max(1f, s * 0.8f))) g.DrawPath(pen, p);
                    }
                    for (int i = 0; i < apps.Count; i++)
                    {
                        var cell = new RectangleF(Pad + i * Cell, Pad, Cell, Cell);
                        if (i == sel)
                            using (var p = Round(RectangleF.Inflate(cell, -2 * s, -2 * s), 12 * s))
                            using (var b = new SolidBrush(light ? Color.FromArgb(46, 0, 0, 0) : Color.FromArgb(64, 255, 255, 255))) g.FillPath(b, p);
                        var ir = new RectangleF(cell.X + CellPad, cell.Y + CellPad, iconPx, iconPx);
                        Bitmap ic; lock (cacheLock) iconCache.TryGetValue(apps[i].IconPath ?? "", out ic);
                        if (ic != null) g.DrawImage(ic, ir);
                        else DrawPlaceholder(g, ir, apps[i].Name);
                    }
                    if (apps.Count > 0)
                    {
                        var name = apps[sel].Name;
                        using (var f = new Font(UiFont.Reg, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(light ? Color.FromArgb(30, 30, 32) : Color.FromArgb(235, 235, 240)))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                        {
                            // el nombre va bajo el icono elegido, sin salirse del panel
                            float cx = Pad + sel * Cell + Cell / 2f, w = Math.Max(Cell * 2.2f, 160 * s);
                            float x = Math.Max(Pad / 2, Math.Min(area.Width - Pad / 2 - w, cx - w / 2));
                            g.DrawString(name, f, b, new RectangleF(x, Pad + Cell + 3 * s, w, NameH), sf);
                        }
                    }
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

        void DrawPlaceholder(Graphics g, RectangleF r, string name)
        {
            using (var p = Round(r, r.Width * 0.225f))
            using (var b = new SolidBrush(Color.FromArgb(130, 130, 136))) g.FillPath(b, p);
            using (var f = new Font(UiFont.Semi, r.Width * 0.42f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(), f, Brushes.White, r, sf);
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        // Los iconos se guardan entre usos (pocos, solo de apps abiertas) para que el panel salga al instante.
        void LoadIconsAsync()
        {
            var missing = new List<string>();
            lock (cacheLock)
            {
                if (iconCache.Count > 30) { foreach (var b in iconCache.Values) if (b != null) b.Dispose(); iconCache.Clear(); }
                foreach (var a in apps) if (a.IconPath != null && !iconCache.ContainsKey(a.IconPath) && !missing.Contains(a.IconPath)) missing.Add(a.IconPath);
            }
            if (missing.Count == 0) return;
            int px = Math.Max(iconPx, (int)(96 * s));
            var th = new Thread(() =>
            {
                foreach (var path in missing)
                {
                    var bmp = Shell.GetIcon(path, px, 4) ?? Shell.GetIcon(path, px, 0);
                    lock (cacheLock) { if (!iconCache.ContainsKey(path)) iconCache[path] = bmp; else if (bmp != null) bmp.Dispose(); }
                    try { if (!IsDisposed) BeginInvoke(new Action(() => { if (shown && !IsDisposed) Render(); })); } catch { }
                }
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            showTimer.Stop(); showTimer.Dispose(); altWatch.Stop(); altWatch.Dispose();
            base.OnFormClosed(e);
        }

        // ===================== apps abiertas, de la mas reciente a la mas antigua =====================
        // EnumWindows recorre las ventanas de arriba hacia abajo, asi que la primera app es la que estas usando.
        static List<AppGroup> RunningApps()
        {
            var groups = new List<AppGroup>();
            var byKey = new Dictionary<string, AppGroup>(StringComparer.OrdinalIgnoreCase);
            var pidPath = new Dictionary<uint, string>();
            IntPtr finder = Finder.Current != null && !Finder.Current.IsDisposed && Finder.Current.Visible ? Finder.Current.Handle : IntPtr.Zero;
            string explorerExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

            N.EnumWindows((h, l) =>
            {
                if (!N.IsWindowVisible(h)) return true;
                uint pid; N.GetWindowThreadProcessId(h, out pid);
                bool isFinder = h == finder;
                if (pid == myPid && !isFinder) return true;
                if (!isFinder)
                {
                    if (N.GetWindow(h, 4) != IntPtr.Zero) return true;                 // ventanas "hijas" (dialogos con dueno)
                    if ((N.GetWindowLong(h, -20) & 0x80) != 0) return true;            // ventanas de herramientas
                    if (N.GetWindowTextLength(h) == 0) return true;
                    int cloaked; N.DwmGetWindowAttribute(h, 14, out cloaked, 4);
                    if (cloaked != 0) return true;                                      // en otro escritorio virtual o escondidas
                }

                string key, name = null, iconPath;
                string path;
                if (!pidPath.TryGetValue(pid, out path)) { path = N.ProcessPath(pid); pidPath[pid] = path; }
                if (isFinder) { key = "macdock:finder"; name = "Finder"; iconPath = explorerExe; }
                else if (path == "") return true;
                else if (path.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (N.ClassOf(h) != "CabinetWClass") return true;                   // escritorio, barra de tareas...
                    key = path; name = "Explorador de archivos"; iconPath = path;
                }
                else if (path.EndsWith("\\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    // apps de la Tienda (Calculadora, Configuracion...): el programa real esta en una ventana interna
                    string title = N.TitleOf(h);
                    IntPtr core = FindWindowEx(h, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
                    string real = "";
                    if (core != IntPtr.Zero) { uint cpid; N.GetWindowThreadProcessId(core, out cpid); real = N.ProcessPath(cpid); }
                    var entry = FindStoreApp(title);
                    name = entry != null ? entry.Name : title;
                    key = real != "" ? real : "uwp:" + name;
                    iconPath = entry != null ? entry.Path : (real != "" ? real : path);
                }
                else
                {
                    key = path; iconPath = path;
                }

                AppGroup grp;
                if (!byKey.TryGetValue(key, out grp))
                {
                    if (name == null)
                    {
                        name = Path.GetFileNameWithoutExtension(path);
                        try { var vi = FileVersionInfo.GetVersionInfo(path); if (!string.IsNullOrEmpty(vi.FileDescription)) name = vi.FileDescription; } catch { }
                    }
                    grp = new AppGroup { Key = key, Name = name, IconPath = iconPath };
                    byKey[key] = grp; groups.Add(grp);
                }
                grp.Windows.Add(h);
                return true;
            }, IntPtr.Zero);
            return groups;
        }

        static AppEntry FindStoreApp(string title)
        {
            var list = AppIndex.Current;
            if (list == null || string.IsNullOrEmpty(title)) return null;
            return list.FirstOrDefault(a => a.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && string.Equals(a.Name, title, StringComparison.CurrentCultureIgnoreCase))
                ?? list.FirstOrDefault(a => a.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && a.Name.Length > 2 &&
                                            (title.EndsWith("- " + a.Name, StringComparison.CurrentCultureIgnoreCase) || title.StartsWith(a.Name + " -", StringComparison.CurrentCultureIgnoreCase)));
        }

        // Trae al frente todas las ventanas de la app (como en Mac) y activa la mas reciente.
        static void BringAppToFront(AppGroup g)
        {
            try
            {
                var wins = g.Windows.Where(N.IsWindowVisible).ToList();
                if (wins.Count == 0) return;
                var normal = wins.Where(h => !N.IsIconic(h)).ToList();
                IntPtr main = normal.Count > 0 ? normal[0] : wins[0];
                if (N.IsIconic(main)) N.ShowWindow(main, 9);
                // de abajo hacia arriba, sin activarlas, para que queden juntas detras de la principal
                for (int i = normal.Count - 1; i >= 1; i--)
                    N.SetWindowPos(normal[i], IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);   // NOSIZE NOMOVE NOACTIVATE NOOWNERZORDER
                ForceForeground(main);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        // Windows a veces no deja que otro programa cambie la ventana activa; uniendo la entrada con la de la ventana actual si lo permite.
        static void ForceForeground(IntPtr h)
        {
            if (N.SetForegroundWindow(h) && N.GetForegroundWindow() == h) return;
            IntPtr fg = N.GetForegroundWindow();
            uint pid, fgThread = fg == IntPtr.Zero ? 0 : N.GetWindowThreadProcessId(fg, out pid), me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            try { BringWindowToTop(h); N.SetForegroundWindow(h); }
            finally { if (attached) AttachThreadInput(me, fgThread, false); }
        }
    }
}
