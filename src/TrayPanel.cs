using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacDock
{
    // Apps en segundo plano (iconos de la bandeja) con el mismo estilo del Centro de Control.
    // Windows no deja dibujar ni "presionar" los iconos reales de otra app, asi que:
    //  - la lista sale de lo que Windows recuerda de cada icono (HKCU\Control Panel\NotifyIconSettings: programa, nombre e imagen)
    //    y solo se muestran las apps que estan corriendo;
    //  - clic = abrir o traer al frente la app; clic derecho = Abrir / Forzar cierre / Menu de la app (abre el panel de Windows,
    //    donde esta el menu propio de cada icono, como "Salir").
    // En Windows 10 (sin esa lista) se abre el panel de Windows como antes.
    class TrayPanel : Form
    {
        static TrayPanel current; static DateTime closedAt = DateTime.MinValue;

        public static void Toggle(float s, bool light, Point anchor, Rectangle screen, Action windowsPanel)
        {
            if (current != null && !current.IsDisposed) { current.Close(); return; }
            if ((DateTime.Now - closedAt).TotalMilliseconds < 250) return;
            List<TrayApp> apps;
            try { apps = TrayApp.Read(); } catch (Exception ex) { Program.Log(ex); apps = null; }
            if (apps == null) { if (windowsPanel != null) windowsPanel(); return; }   // sin datos: el panel de Windows
            current = new TrayPanel(s, light, anchor, screen, apps, windowsPanel);
            current.FormClosed += (o, e) => { closedAt = DateTime.Now; current = null; };
            current.Show(); current.Activate(); N.SetForegroundWindow(current.Handle);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        readonly float s; readonly bool light; readonly List<TrayApp> apps; readonly Action windowsPanel;
        const int Cols = 4;
        int hover = -1, hoverFoot = 0;
        RectangleF gridCard, footWin, footSettings;
        readonly List<RectangleF> tiles = new List<RectangleF>();

        TrayPanel(float scale, bool lightTheme, Point anchor, Rectangle screen, List<TrayApp> list, Action fallback)
        {
            s = scale; light = lightTheme; apps = list; windowsPanel = fallback;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
            BackColor = light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(30, 30, 36);
            float pad = 12 * s, tile = 68 * s, gap = 8 * s;
            int rows = Math.Max(1, (apps.Count + Cols - 1) / Cols);
            int w = (int)(pad * 2 + Cols * tile + (Cols - 1) * gap + pad * 2);
            float gridH = apps.Count == 0 ? 64 * s : rows * (tile + 30 * s) + (rows - 1) * gap + pad * 2;
            int h = (int)(pad + 40 * s + gridH + pad + 30 * s + pad);
            int x = Math.Min(screen.Right - w - (int)(10 * s), Math.Max(screen.Left + (int)(10 * s), anchor.X - w / 2));
            Bounds = new Rectangle(x, anchor.Y, w, h);

            gridCard = new RectangleF(pad, pad + 40 * s, w - pad * 2, gridH);
            for (int i = 0; i < apps.Count; i++)
            {
                int r = i / Cols, c = i % Cols;
                tiles.Add(new RectangleF(gridCard.X + pad + c * (tile + gap), gridCard.Y + pad + r * (tile + 30 * s + gap), tile, tile + 30 * s));
            }
            float fy = gridCard.Bottom + pad;
            footSettings = new RectangleF(w - pad - 96 * s, fy, 96 * s, 28 * s);
            footWin = new RectangleF(footSettings.X - 8 * s - 132 * s, fy, 132 * s, 28 * s);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            int border = light ? 0x00CFCFD6 : 0x00484852; DwmSetWindowAttribute(Handle, 34, ref border, 4);
        }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (!menuOpen) Close(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) Close(); }
        protected override void OnFormClosed(FormClosedEventArgs e) { foreach (var a in apps) if (a.Icon != null) a.Icon.Dispose(); base.OnFormClosed(e); }

        Color CardBg { get { return light ? Color.FromArgb(250, 250, 253) : Color.FromArgb(44, 44, 52); } }
        Color CardBorder { get { return light ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(32, 255, 255, 255); } }
        Color Fg { get { return light ? Color.FromArgb(24, 24, 28) : Color.FromArgb(242, 242, 246); } }
        Color Sub { get { return light ? Color.FromArgb(118, 118, 128) : Color.FromArgb(156, 156, 168); } }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float pad = 12 * s;
            using (var fTitle = new Font(UiFont.Semi, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fSub = new Font(UiFont.Reg, 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fName = new Font(UiFont.Reg, 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fgB = new SolidBrush(Fg)) using (var subB = new SolidBrush(Sub))
            {
                g.DrawString("Apps en segundo plano", fTitle, fgB, pad + 4 * s, pad + 2 * s);
                g.DrawString(apps.Count == 0 ? "No hay apps en la bandeja" : apps.Count + (apps.Count == 1 ? " app abierta" : " apps abiertas"), fSub, subB, pad + 4 * s, pad + 20 * s);

                Card(g, gridCard, CardBg, CardBorder, 14 * s);
                if (apps.Count == 0)
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("Nada corriendo en segundo plano.", fSub, subB, gridCard, sf);

                using (var sfName = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })   // hasta 2 renglones
                    for (int i = 0; i < apps.Count; i++)
                    {
                        var t = tiles[i];
                        var iconBox = new RectangleF(t.X + (t.Width - 46 * s) / 2, t.Y + 10 * s, 46 * s, 46 * s);
                        if (i == hover) using (var p = Round(new RectangleF(t.X, t.Y, t.Width, t.Height), 12 * s)) using (var b = new SolidBrush(light ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(26, 255, 255, 255))) g.FillPath(b, p);
                        // "boton" circular como en el Centro de Control, con el icono de la app dentro
                        using (var b = new SolidBrush(light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(62, 62, 72))) g.FillEllipse(b, iconBox);
                        var ic = apps[i].Icon;
                        if (ic != null) g.DrawImage(ic, RectangleF.Inflate(iconBox, -11 * s, -11 * s));
                        else using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(apps[i].Name.Substring(0, 1).ToUpperInvariant(), fTitle, fgB, iconBox, sf);
                        g.DrawString(apps[i].Name, fName, fgB, new RectangleF(t.X - 4 * s, iconBox.Bottom + 5 * s, t.Width + 8 * s, fName.Height * 2.1f), sfName);
                    }

                Pill(g, footWin, "Menú de las apps", hoverFoot == 1, fSub);
                Pill(g, footSettings, "Configurar", hoverFoot == 2, fSub);
                g.DrawString("Clic: abrir", fSub, subB, pad + 4 * s, footWin.Y + 7 * s);
            }
        }

        void Pill(Graphics g, RectangleF r, string t, bool hot, Font f)
        {
            Card(g, r, hot ? (light ? Color.FromArgb(236, 236, 242) : Color.FromArgb(58, 58, 68)) : CardBg, CardBorder, r.Height / 2);
            using (var b = new SolidBrush(Fg)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(t, f, b, r, sf);
        }

        static void Card(Graphics g, RectangleF r, Color bg, Color border, float rad)
        {
            using (var p = Round(r, rad)) { using (var b = new SolidBrush(bg)) g.FillPath(b, p); using (var pen = new Pen(border)) g.DrawPath(pen, p); }
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
            int h = tiles.FindIndex(t => t.Contains(e.Location));
            int f = footWin.Contains(e.Location) ? 1 : footSettings.Contains(e.Location) ? 2 : 0;
            if (h != hover || f != hoverFoot) { hover = h; hoverFoot = f; Cursor = h >= 0 || f > 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover >= 0 || hoverFoot > 0) { hover = -1; hoverFoot = 0; Invalidate(); } }

        bool menuOpen;
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (footWin.Contains(e.Location)) { Close(); if (windowsPanel != null) windowsPanel(); return; }
            if (footSettings.Contains(e.Location)) { Close(); Try(() => Process.Start("ms-settings:taskbar")); return; }
            int i = tiles.FindIndex(t => t.Contains(e.Location));
            if (i < 0) return;
            var a = apps[i];
            if (e.Button == MouseButtons.Left) { Close(); a.Open(); return; }
            if (e.Button != MouseButtons.Right) return;
            var m = MenuStyle.New(light);
            Action<string, Action> add = (t, act) => { var mi = new ToolStripMenuItem(t); mi.Click += (o, ev) => { Close(); Try(act); }; m.Items.Add(mi); };
            add("Abrir " + a.Name, a.Open);
            add("Menú de " + a.Name + " (Salir, opciones...)", () => { if (windowsPanel != null) windowsPanel(); });
            m.Items.Add(new ToolStripSeparator());
            if (a.Pids.Count > 0)
                add("Forzar cierre", () =>
                {
                    if (MessageBox.Show("¿Forzar el cierre de " + a.Name + "?\n\nSe cerrará de inmediato y se perderá lo que no hayas guardado.", "MacDock",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    foreach (var pid in a.Pids) try { using (var p = Process.GetProcessById(pid)) p.Kill(); } catch { }
                });
            menuOpen = true;
            m.Closed += (o, ev) => { menuOpen = false; if (!ContainsFocus && !IsDisposed) Close(); };
            m.Show(this, e.Location);
        }

        static void Try(Action a) { try { a(); } catch (Exception ex) { Program.Log(ex); } }
    }

    // Una app con icono en la bandeja.
    class TrayApp
    {
        public string Name, Exe, Special; public Bitmap Icon; public List<int> Pids = new List<int>();

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        // Devuelve null si Windows no guarda esta lista (Windows 10): entonces se usa el panel de Windows.
        public static List<TrayApp> Read()
        {
            using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
            {
                if (root == null) return null;
                // programas corriendo ahora (ruta -> procesos)
                var running = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                foreach (var pr in Process.GetProcesses())
                    try
                    {
                        string path = N.ProcessPath((uint)pr.Id);
                        if (path == "") continue;
                        List<int> l; if (!running.TryGetValue(path, out l)) running[path] = l = new List<int>();
                        l.Add(pr.Id);
                    }
                    catch { }
                    finally { pr.Dispose(); }

                var list = new List<TrayApp>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sub in root.GetSubKeyNames())
                    using (var k = root.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        string exe = ResolvePath(Convert.ToString(k.GetValue("ExecutablePath", "")));
                        if (exe == "") continue;
                        string tip = Convert.ToString(k.GetValue("InitialTooltip", "")).Split('\n')[0].Trim();
                        string file = Path.GetFileName(exe).ToLowerInvariant();
                        var app = new TrayApp { Exe = exe };
                        if (file == "explorer.exe")
                        {
                            if (tip.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) < 0) continue;   // otros iconos del propio Windows ya estan en la barra
                            app.Special = "ms-settings:bluetooth"; app.Name = "Bluetooth";
                        }
                        else
                        {
                            List<int> pids;
                            if (!running.TryGetValue(exe, out pids)) continue;   // solo apps abiertas
                            app.Pids = pids;
                            if (file == "securityhealthsystray.exe") { app.Special = "windowsdefender:"; app.Name = "Seguridad de Windows"; }
                        }
                        if (!seen.Add(app.Special ?? exe)) continue;
                        if (app.Name == null) app.Name = NiceName(exe, tip);
                        app.Icon = Snapshot(k) ?? Shell.GetIcon(app.Pids.Count > 0 && Shell.AppIdOf((uint)app.Pids[0]) != "" ? Shell.AppsFolderPath(Shell.AppIdOf((uint)app.Pids[0])) : exe, 32);
                        list.Add(app);
                    }
                return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        // Abrir: si tiene una ventana, al frente; si no, se vuelve a abrir (las apps de la bandeja muestran su ventana al abrirlas otra vez).
        public void Open()
        {
            if (Special != null) { Process.Start(Special); return; }
            IntPtr win = IntPtr.Zero; var set = new HashSet<int>(Pids);
            N.EnumWindows((h, l) =>
            {
                uint pid; N.GetWindowThreadProcessId(h, out pid);
                if (set.Contains((int)pid) && N.IsWindowVisible(h) && N.GetWindowTextLength(h) > 0 && N.GetWindow(h, 4) == IntPtr.Zero) { win = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (win != IntPtr.Zero) { if (N.IsIconic(win)) N.ShowWindow(win, 9); N.SetForegroundWindow(win); return; }
            string appId = Pids.Count > 0 ? Shell.AppIdOf((uint)Pids[0]) : "";
            if (appId != "") Process.Start("explorer.exe", "\"" + Shell.AppsFolderPath(appId) + "\"");
            else Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Exe) });
        }

        static string NiceName(string exe, string tip)
        {
            try { var vi = FileVersionInfo.GetVersionInfo(exe); if (!string.IsNullOrWhiteSpace(vi.ProductName) && vi.ProductName.Length <= 24) return vi.ProductName.Trim(); if (!string.IsNullOrWhiteSpace(vi.FileDescription)) return vi.FileDescription.Trim(); } catch { }
            if (tip != "" && tip.Length <= 24) return tip;
            return Path.GetFileNameWithoutExtension(exe);
        }

        // Windows guarda una imagen PNG de cada icono de la bandeja
        static Bitmap Snapshot(RegistryKey k)
        {
            try
            {
                var bytes = k.GetValue("IconSnapshot") as byte[];
                if (bytes == null || bytes.Length < 8) return null;
                using (var ms = new MemoryStream(bytes)) using (var img = Image.FromStream(ms)) return new Bitmap(img);
            }
            catch { return null; }
        }

        // Las rutas vienen como "{GUID de carpeta conocida}\resto": se convierten a la ruta real
        static string ResolvePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            p = Environment.ExpandEnvironmentVariables(p);
            if (p.StartsWith("{") && p.IndexOf('}') > 0)
            {
                int end = p.IndexOf('}');
                Guid g;
                if (Guid.TryParse(p.Substring(0, end + 1), out g))
                {
                    IntPtr ptr;
                    if (SHGetKnownFolderPath(g, 0, IntPtr.Zero, out ptr) == 0)
                    {
                        string folder = Marshal.PtrToStringUni(ptr); Marshal.FreeCoTaskMem(ptr);
                        return Path.Combine(folder, p.Substring(end + 1).TrimStart('\\'));
                    }
                }
                return "";
            }
            return p;
        }
    }
}
