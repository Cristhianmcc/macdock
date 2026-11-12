using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MacDock
{
    // Spotlight estilo Mac (Alt + Espacio): apps, archivos (indice de Windows), carpetas, ajustes y calculadora.
    class Spotlight : Form
    {
        public static Spotlight Current;

        public static void Toggle(bool light, float s)
        {
            if (Current != null && !Current.IsDisposed) { Current.Close(); return; }
            Current = new Spotlight(light, s);
            Current.FormClosed += (o, e) => { Current = null; GC.Collect(); };
            Current.Show();
            Current.Activate();
            N.SetForegroundWindow(Current.Handle);
        }

        class Result { public string Title, Sub, Path, Args = null; public Bitmap Icon; public int Kind; }   // Kind: 0 app, 1 archivo, 2 carpeta, 3 calculo, 4 ajuste

        static List<Result> appIndex;
        static DateTime appIndexAt;
        static readonly object Lock = new object();

        static readonly string[][] SettingsList = {
            new[] { "Configuración", "ms-settings:" }, new[] { "Wi-Fi", "ms-settings:network-wifi" }, new[] { "Bluetooth", "ms-settings:bluetooth" },
            new[] { "Pantalla y brillo", "ms-settings:display" }, new[] { "Sonido", "ms-settings:sound" }, new[] { "Batería y energía", "ms-settings:batterysaver" },
            new[] { "Notificaciones", "ms-settings:notifications" }, new[] { "Fondo de pantalla", "ms-settings:personalization-background" },
            new[] { "Aplicaciones instaladas", "ms-settings:appsfeatures" }, new[] { "Actualizaciones de Windows", "ms-settings:windowsupdate" },
            new[] { "Impresoras", "ms-settings:printers" }, new[] { "Almacenamiento", "ms-settings:storagesense" }, new[] { "Fecha y hora", "ms-settings:dateandtime" },
            new[] { "Administrador de tareas", "taskmgr.exe" }, new[] { "Panel de control", "control.exe" }, new[] { "Papelera de reciclaje", "shell:RecycleBinFolder" },
        };

        float s; bool light;
        TextBox box;
        List<Result> results = new List<Result>();
        int sel = 0, top = 0;
        System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 140 };
        int searchSeq = 0;
        Color bg, fg, sub, selBg;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        Spotlight(bool lightTheme, float scale)
        {
            light = lightTheme; s = scale;
            bg = light ? Color.FromArgb(248, 248, 250) : Color.FromArgb(34, 34, 38);
            fg = light ? Color.FromArgb(24, 24, 26) : Color.FromArgb(240, 240, 244);
            sub = light ? Color.FromArgb(120, 120, 128) : Color.FromArgb(150, 150, 160);
            selBg = Color.FromArgb(10, 122, 255);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual; DoubleBuffered = true; BackColor = bg;
            var b = Screen.FromPoint(Cursor.Position).Bounds;
            int w = (int)(680 * s);
            Bounds = new Rectangle(b.Left + (b.Width - w) / 2, b.Top + (int)(b.Height * 0.22), w, BoxH);

            box = new TextBox { BorderStyle = BorderStyle.None, BackColor = bg, ForeColor = fg, Font = new Font(UiFont.Reg, 19f * s, FontStyle.Regular, GraphicsUnit.Pixel) };
            box.SetBounds((int)(52 * s), (int)((BoxH - box.PreferredHeight) / 2), w - (int)(70 * s), box.PreferredHeight);
            box.TextChanged += (o, e) => { debounce.Stop(); debounce.Start(); };
            box.KeyDown += BoxKeyDown;
            box.PreviewKeyDown += (o, e) => { if (e.KeyCode == Keys.Tab) e.IsInputKey = true; };   // Tab completa rutas
            Controls.Add(box);
            debounce.Tick += (o, e) => { debounce.Stop(); Search(box.Text); };
            EnsureAppIndexAsync();
        }

        int BoxH { get { return (int)(58 * s); } }
        int RowH { get { return (int)(44 * s); } }
        const int MaxRows = 8;

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; cp.ClassStyle |= 0x20000; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); box.Focus(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }

        // ---------- indice de apps (menu Inicio + escritorio) ----------
        static void EnsureAppIndexAsync()
        {
            lock (Lock) { if (appIndex != null && (DateTime.Now - appIndexAt).TotalMinutes < 10) return; }
            var th = new Thread(() =>
            {
                var list = new List<Result>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dirs = new[] {
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Path.Combine(Program.DataDir, "Apps") };
                foreach (var d in dirs)
                {
                    if (!Directory.Exists(d)) continue;
                    var files = new List<string>();
                    bool deep = d.IndexOf("Start Menu", StringComparison.OrdinalIgnoreCase) >= 0;   // del Escritorio y del dock solo el primer nivel
                    SafeFind(d, files, deep ? 0 : 99);
                    foreach (var f in files)
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        string low = name.ToLowerInvariant();
                        if (low.Contains("uninstall") || low.Contains("desinstal") || low.Contains("readme") || low.Contains("léame") || low.Contains("help") || low.Contains("ayuda")) continue;
                        if (!seen.Add(name)) continue;
                        list.Add(new Result { Title = name, Sub = "Aplicación", Path = f, Kind = 0 });
                    }
                }
                lock (Lock) { appIndex = list; appIndexAt = DateTime.Now; }
                var cur = Current;
                if (cur != null && !cur.IsDisposed) try { cur.BeginInvoke(new Action(() => { if (!cur.IsDisposed && cur.box.Text.Trim() != "") cur.Search(cur.box.Text); })); } catch { }
            });
            th.IsBackground = true; th.Start();
        }

        // recorre carpetas saltando las protegidas y los enlaces antiguos (como "Programas")
        static void SafeFind(string dir, List<string> outList, int depth)
        {
            if (depth > 6 && depth < 99) return;
            try
            {
                foreach (var f in Directory.GetFiles(dir)) { string e = Path.GetExtension(f).ToLowerInvariant(); if (e == ".lnk" || e == ".url") outList.Add(f); }
                if (depth >= 99) return;
                foreach (var d in Directory.GetDirectories(dir))
                {
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    SafeFind(d, outList, depth + 1);
                }
            }
            catch { }
        }

        // ---------- busqueda ----------
        void Search(string q)
        {
            q = q.Trim();
            int seq = ++searchSeq;
            var quick = new List<Result>();
            if (q == "") { SetResults(quick); return; }

            // una ruta pegada o escrita (C:\..., \\servidor\..., ~\..., %APPDATA%\...): se muestra la carpeta
            // y lo que hay dentro filtrado por lo que se va escribiendo. En otro hilo por si es una ruta de red lenta.
            if (LooksLikePath(q))
            {
                var pth = new Thread(() =>
                {
                    var res = PathResults(q);
                    try { BeginInvoke(new Action(() => { if (seq == searchSeq && !IsDisposed) SetResults(res); })); } catch { }
                });
                pth.IsBackground = true; pth.Start();
                return;
            }

            string calc = TryCalc(q);
            if (calc != null) quick.Add(new Result { Title = "= " + calc, Sub = "Calculadora · Enter para copiar", Path = calc, Kind = 3 });

            List<Result> apps; lock (Lock) apps = appIndex ?? new List<Result>();
            quick.AddRange(apps.Select(a => new { a, sc = Score(a.Title, q) }).Where(x => x.sc > 0).OrderByDescending(x => x.sc).ThenBy(x => x.a.Title.Length).Take(5).Select(x => x.a));
            foreach (var st in SettingsList) if (Score(st[0], q) > 0) quick.Add(new Result { Title = st[0], Sub = "Ajuste de Windows", Path = st[1], Kind = 4 });
            SetResults(quick);

            if (q.Length < 2) return;
            var th = new Thread(() =>
            {
                var files = SearchIndex(q);
                try { BeginInvoke(new Action(() => { if (seq == searchSeq && !IsDisposed) SetResults(quick.Concat(files).ToList()); })); } catch { }
            });
            th.IsBackground = true; th.Start();
        }

        static bool LooksLikePath(string t)
        {
            t = (t ?? "").Trim().Trim('"');
            return t.Length >= 2 && ((char.IsLetter(t[0]) && t[1] == ':') || t.StartsWith("\\\\") || t.StartsWith("~") || t.StartsWith("%"));
        }

        static string ExpandPath(string t)
        {
            string p = t.Trim().Trim('"');
            if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            p = Environment.ExpandEnvironmentVariables(p).Replace('/', '\\');
            if (p.Length == 2 && p[1] == ':') p += "\\";
            return p;
        }

        // "C:\Users\Cris\Desk" -> la carpeta C:\Users\Cris filtrada por "Desk".
        // "C:\Users\Cris\" o una carpeta que existe -> esa carpeta primero y luego su contenido.
        static List<Result> PathResults(string q)
        {
            var list = new List<Result>();
            try
            {
                string p = ExpandPath(q);
                if (File.Exists(p))
                {
                    list.Add(new Result { Title = Path.GetFileName(p), Sub = "Archivo · " + Path.GetDirectoryName(p), Path = p, Kind = 1 });
                    return list;
                }
                string dir, filter;
                if (Directory.Exists(p)) { dir = p; filter = ""; }
                else { dir = Path.GetDirectoryName(p); filter = Path.GetFileName(p); }
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;

                if (filter == "")
                    list.Add(new Result { Title = FolderName(dir), Sub = "Abrir carpeta · " + dir, Path = dir, Kind = 2 });
                var di = new DirectoryInfo(dir);
                var items = di.EnumerateFileSystemInfos()
                    .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .Where(f => filter == "" || f.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(f => filter != "" && f.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(f => (f.Attributes & FileAttributes.Directory) != 0 ? 0 : 1)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(40);
                foreach (var f in items)
                {
                    bool isDir = (f.Attributes & FileAttributes.Directory) != 0;
                    list.Add(new Result { Title = f.Name, Sub = (isDir ? "Carpeta · " : "") + dir, Path = f.FullName, Kind = isDir ? 2 : 1 });
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            return list;
        }

        static string FolderName(string dir)
        {
            string n = Path.GetFileName(dir.TrimEnd('\\'));
            return n == "" ? dir : n;
        }

        static int Score(string title, string q)
        {
            string t = title.ToLowerInvariant(), k = q.ToLowerInvariant();
            if (t == k) return 100;
            if (t.StartsWith(k)) return 80;
            if (t.Split(' ', '-', '_', '.').Any(w => w.StartsWith(k))) return 60;
            // iniciales: "vsc" -> Visual Studio Code
            string ini = new string(t.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]).ToArray());
            if (k.Length >= 2 && ini.StartsWith(k)) return 50;
            if (t.Contains(k)) return 30;
            return 0;
        }

        static string TryCalc(string q)
        {
            string e = q.Replace(",", ".").Replace("x", "*").Replace("X", "*").Replace("÷", "/").Replace("^", "**");
            if (!e.Any(char.IsDigit) || !e.Any(c => "+-*/%".Contains(c))) return null;
            if (e.Any(c => !("0123456789.+-*/%() ".Contains(c)))) return null;
            try
            {
                if (e.Contains("**")) return null;
                var v = new DataTable().Compute(e, null);
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsInfinity(d) || double.IsNaN(d)) return null;
                return Math.Round(d, 10).ToString("0.##########", CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        static List<Result> SearchIndex(string q)
        {
            var list = new List<Result>();
            try
            {
                string safe = q.Replace("'", "''").Replace("\"", "");
                string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
                using (var cn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
                {
                    cn.Open();
                    var cmd = cn.CreateCommand();
                    cmd.CommandText = "SELECT TOP 12 System.ItemUrl, System.ItemNameDisplay, System.ItemType FROM SYSTEMINDEX " +
                        "WHERE CONTAINS(System.FileName, '\"" + safe + "*\"') AND SCOPE='file:" + user + "' " +
                        "AND NOT System.ItemUrl LIKE '%/node_modules/%' AND NOT System.ItemUrl LIKE '%/.git/%' AND NOT System.ItemUrl LIKE '%/AppData/%' " +
                        "ORDER BY System.DateModified DESC";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            string url = Convert.ToString(r[0]);
                            if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                            string path = Uri.UnescapeDataString(url.Substring(5)).Replace('/', '\\');
                            bool dir = Directory.Exists(path);
                            string folder = Path.GetDirectoryName(path) ?? "";
                            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                            if (folder.StartsWith(home, StringComparison.OrdinalIgnoreCase)) folder = "~" + folder.Substring(home.Length);
                            list.Add(new Result { Title = Path.GetFileName(path), Sub = (dir ? "Carpeta · " : "") + folder, Path = path, Kind = dir ? 2 : 1 });
                        }
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            return list;
        }

        void SetResults(List<Result> list)
        {
            results = list; sel = 0; top = 0;
            int rows = Math.Min(MaxRows, results.Count);
            Height = BoxH + (rows > 0 ? rows * RowH + (int)(10 * s) : 0);
            LoadIconsAsync(results.Take(MaxRows + 4).ToList());
            Invalidate();
        }

        void LoadIconsAsync(List<Result> list)
        {
            var th = new Thread(() =>
            {
                foreach (var r in list)
                {
                    if (IsDisposed) return;
                    if (r.Icon != null || r.Kind == 3) continue;
                    string p = r.Kind == 4 ? (r.Path.StartsWith("ms-settings", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Environment.SystemDirectory, "control.exe") : r.Path) : r.Path;
                    if (r.Kind == 4 && !File.Exists(p)) p = Path.Combine(Environment.SystemDirectory, p);
                    r.Icon = Shell.GetIcon(p, 48, r.Kind == 1 ? 0 : 4);
                    try { BeginInvoke(new Action(Invalidate)); } catch { }
                }
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }

        // ---------- teclado ----------
        void BoxKeyDown(object o, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; if (box.Text != "") box.Text = ""; else Close(); }
            else if (e.KeyCode == Keys.Down) { e.SuppressKeyPress = true; MoveSel(1); }
            else if (e.KeyCode == Keys.Up) { e.SuppressKeyPress = true; MoveSel(-1); }
            else if (e.KeyCode == Keys.Tab && results.Count > 0 && results[sel].Kind == 2 && LooksLikePath(box.Text))
            {
                // Tab completa la carpeta elegida y sigue mostrando lo que hay dentro (como en la terminal)
                e.SuppressKeyPress = true;
                box.Text = results[sel].Path.TrimEnd('\\') + "\\";
                box.SelectionStart = box.Text.Length;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (results.Count == 0) { debounce.Stop(); Search(box.Text); if (results.Count == 0) return; }
                Run(results[sel], e.Control);
            }
            else if (e.KeyCode == Keys.Space && e.Shift && results.Count > 0 && (results[sel].Kind == 1 || results[sel].Kind == 2))
            {
                e.SuppressKeyPress = true; var p = results[sel].Path; Close(); QuickLook.Toggle(p, light);
            }
        }

        void MoveSel(int d)
        {
            if (results.Count == 0) return;
            sel = Math.Max(0, Math.Min(results.Count - 1, sel + d));
            if (sel < top) top = sel;
            if (sel >= top + MaxRows) top = sel - MaxRows + 1;
            Invalidate();
        }

        void Run(Result r, bool reveal)
        {
            Close();
            try
            {
                if (r.Kind == 3) { Clipboard.SetText(r.Path); return; }
                if (reveal && (r.Kind == 1 || r.Kind == 2)) { FileOps.ShowInExplorer(r.Path); return; }
                if (r.Kind == 4 && r.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) { Process.Start("explorer.exe", r.Path); return; }
                if (r.Kind == 2 && Directory.Exists(r.Path)) { Finder.OpenOrFocus(light, s, r.Path); return; }
                FileOps.Open(r.Path);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = top + (e.Y - BoxH - (int)(5 * s)) / RowH;
            if (e.Y > BoxH && i >= 0 && i < results.Count) Run(results[i], false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = top + (e.Y - BoxH - (int)(5 * s)) / RowH;
            if (e.Y > BoxH && i >= 0 && i < results.Count && i != sel) { sel = i; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e) { MoveSel(e.Delta > 0 ? -1 : 1); }

        // ---------- dibujo ----------
        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // lupa
            using (var pen = new Pen(sub, 2.2f * s))
            {
                float cx = 26 * s, cy = BoxH / 2f - 2 * s, r = 8 * s;
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                g.DrawLine(pen, cx + r * 0.7f, cy + r * 0.7f, cx + r * 1.6f, cy + r * 1.6f);
            }
            if (box.Text == "")
                using (var f = new Font(UiFont.Reg, 19f * s, FontStyle.Regular, GraphicsUnit.Pixel)) using (var b = new SolidBrush(sub))
                    g.DrawString("Búsqueda Spotlight", f, b, box.Left - 2, box.Top - 1);
            if (results.Count == 0) return;
            using (var p = new Pen(light ? Color.FromArgb(225, 225, 230) : Color.FromArgb(56, 56, 62))) g.DrawLine(p, 0, BoxH - 1, Width, BoxH - 1);

            using (var ft = new Font(UiFont.Reg, 14f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fs = new Font(UiFont.Reg, 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                for (int i = top; i < Math.Min(results.Count, top + MaxRows); i++)
                {
                    var r = results[i];
                    float y = BoxH + 5 * s + (i - top) * RowH;
                    bool on = i == sel;
                    if (on) using (var path = Round(new RectangleF(6 * s, y, Width - 12 * s, RowH - 2 * s), 7 * s)) using (var b = new SolidBrush(selBg)) g.FillPath(b, path);
                    float ix = 16 * s, isz = 28 * s;
                    if (r.Kind == 3)
                        using (var f = new Font(UiFont.Semi, 18f * s, FontStyle.Regular, GraphicsUnit.Pixel)) using (var b = new SolidBrush(on ? Color.White : selBg))
                            g.DrawString("=", f, b, ix + 6 * s, y + 6 * s);
                    else if (r.Icon != null) g.DrawImage(r.Icon, ix, y + (RowH - isz) / 2 - 1 * s, isz, isz);
                    float tx = ix + isz + 12 * s;
                    var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    using (var b = new SolidBrush(on ? Color.White : fg)) g.DrawString(r.Title, ft, b, new RectangleF(tx, y + 4 * s, Width - tx - 16 * s, 20 * s), sf);
                    using (var b = new SolidBrush(on ? Color.FromArgb(220, 255, 255, 255) : sub)) g.DrawString(r.Sub, fs, b, new RectangleF(tx, y + 22 * s, Width - tx - 16 * s, 18 * s), sf);
                }
            }
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            debounce.Stop();
            foreach (var r in results) if (r.Icon != null && r.Kind != 0) { try { r.Icon.Dispose(); } catch { } r.Icon = null; } // los iconos de apps se reutilizan
            base.OnFormClosed(e);
        }
    }
}
