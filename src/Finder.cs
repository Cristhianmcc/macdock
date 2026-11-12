using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MacDock
{
    // "Finder": explorador de archivos propio con estilo Mac. Solo gasta memoria mientras esta abierto.
    class Finder : Form
    {
        public static Finder Current;

        public static void OpenOrFocus(bool light, float s, string path = null)
        {
            if (Current != null && !Current.IsDisposed)
            {
                if (Current.WindowState == FormWindowState.Minimized) Current.WindowState = FormWindowState.Normal;
                Current.Activate(); N.SetForegroundWindow(Current.Handle);
                if (path != null) Current.Navigate(path, true);
                return;
            }
            Current = new Finder(light, s);
            Current.FormClosed += (o, e) => { Current = null; GC.Collect(); };
            Current.Show();
            Current.Navigate(path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), true);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] static extern IntPtr SendMessageStr(IntPtr h, int msg, IntPtr w, string l);

        class SideItem { public string Title, Path; public bool Header; public Bitmap Icon; public RectangleF Rect; }

        float s; bool light;
        Color bg, side, fg, sub, line, accent = Color.FromArgb(10, 122, 255);
        Panel topBar, sideBar, pathBar;
        ListView list;
        TextBox search;
        ImageList bigImgs, smallImgs;
        string current = "";
        bool searchMode, suppressFilter, showDot, renaming;
        List<string> back = new List<string>(), fwd = new List<string>();
        List<SideItem> sideItems = new List<SideItem>();
        RectangleF rBack, rFwd, rIcons, rList, rSort;
        List<KeyValuePair<RectangleF, string>> crumbs = new List<KeyValuePair<RectangleF, string>>();
        int loadSeq;
        string statusText = "";
        Font fUi, fUiSemi, fSmall;

        Finder(bool lightTheme, float scale)
        {
            light = lightTheme; s = scale;
            bg = light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(30, 30, 34);
            side = light ? Color.FromArgb(240, 240, 244) : Color.FromArgb(38, 38, 43);
            fg = light ? Color.FromArgb(28, 28, 30) : Color.FromArgb(236, 236, 240);
            sub = light ? Color.FromArgb(120, 120, 128) : Color.FromArgb(150, 150, 160);
            line = light ? Color.FromArgb(222, 222, 226) : Color.FromArgb(52, 52, 58);
            fUi = new Font(UiFont.Reg, 9.75f); fUiSemi = new Font(UiFont.Semi, 11f); fSmall = new Font(UiFont.Reg, 8.75f);

            Text = "Finder"; BackColor = side; ForeColor = fg; Font = fUi; KeyPreview = true; FormBorderStyle = FormBorderStyle.None; Padding = new Padding(3);
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.Manual;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = Math.Min(wa.Width - 80, (int)(1060 * s)), h = Math.Min(wa.Height - 80, (int)(660 * s));
            Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
            MinimumSize = new Size((int)(560 * s), (int)(360 * s));

            // --- barra superior ---
            topBar = new DoubleBufferedPanel { Dock = DockStyle.Top, Height = (int)(50 * s), BackColor = side };
            topBar.Paint += PaintTop; topBar.MouseUp += TopClick;
            topBar.MouseDown += (o, e) =>
            {
                if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
                var lp = LightsPos;
                if (TrafficLights.Hit(e.Location, lp.X, lp.Y, s) >= 0 || rBack.Contains(e.Location) || rFwd.Contains(e.Location) || rIcons.Contains(e.Location) || rList.Contains(e.Location) || rSort.Contains(e.Location) || e.X > search.Left - 30 * s) return;
                Borderless.BeginDrag(this);
            };
            topBar.MouseDoubleClick += (o, e) => { var lp = LightsPos; if (TrafficLights.Hit(e.Location, lp.X, lp.Y, s) < 0 && e.X < search.Left - 90 * s) Borderless.ToggleMax(this); };
            topBar.MouseMove += (o, e) => { var lp = LightsPos; int hh = new RectangleF(lp.X - 4 * s, lp.Y - 4 * s, TrafficLights.Width(s) + 8 * s, TrafficLights.D(s) + 8 * s).Contains(e.Location) ? 1 : -1; if (hh != lightsHover) { lightsHover = hh; topBar.Invalidate(); } };
            topBar.MouseLeave += (o, e) => { if (lightsHover != -1) { lightsHover = -1; topBar.Invalidate(); } };
            search = new TextBox { BorderStyle = BorderStyle.None, BackColor = light ? Color.FromArgb(226, 226, 232) : Color.FromArgb(56, 56, 62), ForeColor = fg, Font = fUi };
            search.Width = (int)(200 * s);
            search.TextChanged += (o, e) => { if (!searchMode && !suppressFilter && !LooksLikePath(search.Text)) ApplyFilter(); topBar.Invalidate(); };
            search.KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Enter && LooksLikePath(search.Text)) { e.SuppressKeyPress = true; GoToTypedPath(search.Text); }
                else if (e.KeyCode == Keys.Enter && search.Text.Trim().Length > 1) { e.SuppressKeyPress = true; SearchEverywhere(search.Text.Trim()); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; search.Text = ""; if (searchMode) Navigate(current, false); list.Focus(); }
                else if (e.KeyCode == Keys.Down) { e.SuppressKeyPress = true; list.Focus(); if (list.Items.Count > 0 && list.SelectedIndices.Count == 0) list.Items[0].Selected = true; }
            };
            topBar.Controls.Add(search);
            search.HandleCreated += (o, e) => SendMessageStr(search.Handle, 0x1501, new IntPtr(1), "Buscar o pegar una ruta  (Ctrl+L)");
            topBar.Resize += (o, e) => PlaceSearch();

            // --- barra lateral ---
            sideBar = new DoubleBufferedPanel { Dock = DockStyle.Left, Width = (int)(196 * s), BackColor = side };
            sideBar.Paint += PaintSide; sideBar.MouseUp += SideClick;
            sideBar.MouseMove += (o, e) => { sideBar.Cursor = sideItems.Any(i => !i.Header && i.Rect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default; };
            sideBar.AllowDrop = true;
            sideBar.DragOver += (o, e) => { var it = SideAt(sideBar.PointToClient(new Point(e.X, e.Y))); e.Effect = it != null && it.Path != null && Directory.Exists(it.Path) ? DropEffect(e, it.Path) : DragDropEffects.None; };
            sideBar.DragDrop += (o, e) => { var it = SideAt(sideBar.PointToClient(new Point(e.X, e.Y))); if (it != null && it.Path != null) DoDrop(e, it.Path); };

            // --- barra de ruta (abajo) ---
            pathBar = new DoubleBufferedPanel { Dock = DockStyle.Bottom, Height = (int)(28 * s), BackColor = side };
            pathBar.Paint += PaintPath; pathBar.MouseUp += PathClick;
            pathBar.MouseMove += (o, e) => { pathBar.Cursor = crumbs.Any(c => c.Key.Contains(e.Location)) ? Cursors.Hand : Cursors.Default; };

            // --- lista de archivos ---
            bigImgs = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size((int)(64 * s), (int)(64 * s)) };
            smallImgs = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size((int)(20 * s), (int)(20 * s)) };
            list = new ListView
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = bg, ForeColor = fg, Font = fUi,
                LargeImageList = bigImgs, SmallImageList = smallImgs, View = View.LargeIcon, LabelEdit = true,
                AllowDrop = true, FullRowSelect = true, HideSelection = false, MultiSelect = true
            };
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(list, true, null);
            list.Columns.Add("Nombre", (int)(340 * s)); list.Columns.Add("Fecha de modificación", (int)(160 * s));
            list.Columns.Add("Tipo", (int)(140 * s)); list.Columns.Add("Tamaño", (int)(90 * s), HorizontalAlignment.Right);
            list.ItemActivate += (o, e) => OpenSelected();
            list.KeyDown += ListKeyDown;
            list.BeforeLabelEdit += (o, e) => renaming = true;
            list.AfterLabelEdit += AfterRename;
            list.ItemDrag += (o, e) => { var paths = SelectedPaths(); if (paths.Count > 0) { var d = new DataObject(); var sc = new System.Collections.Specialized.StringCollection(); sc.AddRange(paths.ToArray()); d.SetFileDropList(sc); list.DoDragDrop(d, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); } };
            list.DragOver += (o, e) => { e.Effect = DropEffect(e, DropTarget(e)); };
            list.DragDrop += (o, e) => DoDrop(e, DropTarget(e));
            list.MouseUp += (o, e) => { if (e.Button == MouseButtons.Right) ShowContext(list.HitTest(e.Location).Item, e.Location); };
            list.ColumnClick += (o, e) => SortBy(e.Column);
            list.SelectedIndexChanged += (o, e) => UpdateStatus();
            list.HandleCreated += (o, e) => ThemeList();

            var sep = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = line };
            Controls.Add(list); Controls.Add(sep); Controls.Add(sideBar); Controls.Add(pathBar); Controls.Add(topBar);
            BuildSidebar();
            PlaceSearch();
        }

        class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style |= 0x20000 | 0x10000 | 0x80000; cp.ClassStyle |= 0x20000; return cp; }   // minimizar/maximizar/menu + sombra
        }

        protected override void WndProc(ref Message m)
        {
            if (Borderless.HitTest(this, ref m, (int)(6 * s))) return;
            base.WndProc(ref m);
        }

        // botones de Mac (arriba a la izquierda)
        int lightsHover = -1; bool lightsActive = true;
        PointF LightsPos { get { return new PointF(18 * s, topBar.Height / 2f - TrafficLights.D(s) / 2); } }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); lightsActive = true; if (topBar != null) topBar.Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); lightsActive = false; if (topBar != null) topBar.Invalidate(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
        }

        void ThemeList()
        {
            SetWindowTheme(list.Handle, light ? "Explorer" : "DarkMode_Explorer", null);
            IntPtr header = SendMessage(list.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);   // LVM_GETHEADER
            if (header != IntPtr.Zero) SetWindowTheme(header, light ? "Explorer" : "DarkMode_ItemsView", null);
        }

        void PlaceSearch()
        {
            int h = search.PreferredHeight;
            search.Location = new Point(topBar.Width - search.Width - (int)(22 * s), (topBar.Height - h) / 2);
        }

        // ---------------- navegacion ----------------
        public void Navigate(string path, bool addHistory)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            if (addHistory && current != "" && !string.Equals(current, path, StringComparison.OrdinalIgnoreCase)) { back.Add(current); fwd.Clear(); }
            current = path; searchMode = false;
            if (search.Text != "") { suppressFilter = true; search.Text = ""; suppressFilter = false; }
            LoadFolder();
        }

        // Ir a una ruta escrita o pegada (como la barra de direcciones de Windows o "Ir a la carpeta" de Mac).
        // Acepta C:\..., \\servidor\..., ~\..., %APPDATA%\... y la ruta de un archivo (abre su carpeta y lo marca).
        static bool LooksLikePath(string t)
        {
            t = (t ?? "").Trim().Trim('"');
            return t.Length >= 2 && ((char.IsLetter(t[0]) && t[1] == ':') || t.StartsWith("\\\\") || t.StartsWith("~") || t.StartsWith("%"));
        }

        void GoToTypedPath(string t)
        {
            string p = t.Trim().Trim('"');
            if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            p = Environment.ExpandEnvironmentVariables(p).Replace('/', '\\');
            if (p.Length == 2 && p[1] == ':') p += "\\";
            string select = null;
            if (File.Exists(p)) { select = p; p = Path.GetDirectoryName(p); }
            if (!Directory.Exists(p))
            {
                MessageBox.Show(this, "No encontre esa carpeta:\n" + p, "Finder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                search.Focus(); search.SelectAll();
                return;
            }
            Navigate(p, true);
            list.Focus();
            if (select != null)
                foreach (ListViewItem it in list.Items)
                    if (string.Equals((string)it.Tag, select, StringComparison.OrdinalIgnoreCase)) { it.Selected = true; it.Focused = true; it.EnsureVisible(); break; }
        }

        // Ctrl+L: pone la ruta actual en el buscador, lista para editarla o reemplazarla
        void EditPath()
        {
            suppressFilter = true; search.Text = current; suppressFilter = false;
            search.Focus(); search.SelectAll();
        }

        void GoBack() { if (back.Count == 0) return; fwd.Add(current); string p = back[back.Count - 1]; back.RemoveAt(back.Count - 1); current = p; searchMode = false; LoadFolder(); }
        void GoFwd() { if (fwd.Count == 0) return; back.Add(current); string p = fwd[fwd.Count - 1]; fwd.RemoveAt(fwd.Count - 1); current = p; searchMode = false; LoadFolder(); }
        void GoUp() { var p = Directory.GetParent(current.TrimEnd('\\') + "\\"); var up = p != null ? p.Parent : null; if (up != null) Navigate(up.FullName, true); }

        List<string> allEntries = new List<string>();

        void LoadFolder()
        {
            var entries = new List<string>();
            try
            {
                var di = new DirectoryInfo(current);
                foreach (var d in di.EnumerateDirectories().Where(x => (x.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && (showDot || !x.Name.StartsWith("."))).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) entries.Add(d.FullName);
                foreach (var f in di.EnumerateFiles().Where(x => (x.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && (showDot || !x.Name.StartsWith(".")) && !x.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) entries.Add(f.FullName);
            }
            catch (Exception ex) { MessageBox.Show(this, "No se puede abrir esta carpeta:\n" + ex.Message, "Finder"); }
            allEntries = Ordered(entries);
            if (search.Text.Trim() != "" && !searchMode) ApplyFilter(); else Fill(allEntries);
            Watch(current);
            Text = FolderTitle(current) + " — Finder";
            topBar.Invalidate(); pathBar.Invalidate(); sideBar.Invalidate();
        }

        // se actualiza solo cuando algo cambia en la carpeta (como el Finder de Mac)
        FileSystemWatcher watcher; System.Windows.Forms.Timer watchTimer;
        void Watch(string folder)
        {
            try
            {
                if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
                if (watchTimer == null) { watchTimer = new System.Windows.Forms.Timer { Interval = 500 }; watchTimer.Tick += (o, e) => { watchTimer.Stop(); if (!searchMode && !renaming) Reload(); }; }
                watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                FileSystemEventHandler h = (o, e) => { try { BeginInvoke(new Action(() => { watchTimer.Stop(); watchTimer.Start(); })); } catch { } };
                watcher.Created += h; watcher.Deleted += h; watcher.Renamed += (o, e) => h(o, null);
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        static string FolderTitle(string p)
        {
            string n = Path.GetFileName(p.TrimEnd('\\'));
            if (string.IsNullOrEmpty(n)) n = p;
            if (string.Equals(p, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StringComparison.OrdinalIgnoreCase)) return "Escritorio";
            if (string.Equals(p, N.Downloads(), StringComparison.OrdinalIgnoreCase)) return "Descargas";
            return n;
        }

        void ApplyFilter()
        {
            string q = search.Text.Trim();
            Fill(q == "" ? allEntries : allEntries.Where(p => Path.GetFileName(p).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList());
        }

        void SearchEverywhere(string q)
        {
            searchMode = true;
            Text = "Buscar: " + q + " — Finder";
            var res = new List<string>();
            try
            {
                string safe = q.Replace("'", "''").Replace("\"", "");
                string scope = current.Replace('\\', '/');
                using (var cn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
                {
                    cn.Open();
                    var cmd = cn.CreateCommand();
                    cmd.CommandText = "SELECT TOP 300 System.ItemUrl FROM SYSTEMINDEX WHERE CONTAINS(System.FileName, '\"" + safe + "*\"') AND SCOPE='file:" + scope + "' " +
                                      "AND NOT System.ItemUrl LIKE '%/node_modules/%' AND NOT System.ItemUrl LIKE '%/.git/%' ORDER BY System.DateModified DESC";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            string url = Convert.ToString(r[0]);
                            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) res.Add(Uri.UnescapeDataString(url.Substring(5)).Replace('/', '\\'));
                        }
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            Fill(res);
            statusText = res.Count + " resultados en " + FolderTitle(current) + " y subcarpetas";
            pathBar.Invalidate();
        }

        // ---------------- lista ----------------
        static readonly Dictionary<string, string> TypeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attr; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, int size, uint flags);

        static string TypeName(string path, bool dir)
        {
            if (dir) return "Carpeta";
            string ext = Path.GetExtension(path);
            string t;
            if (TypeNames.TryGetValue(ext, out t)) return t;
            var info = new SHFILEINFO();
            SHGetFileInfo(ext == "" ? "x" : ext, 0x80, ref info, Marshal.SizeOf(typeof(SHFILEINFO)), 0x400 | 0x10);   // SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES
            t = string.IsNullOrEmpty(info.type) ? ext.TrimStart('.').ToUpperInvariant() : info.type;
            TypeNames[ext] = t;
            return t;
        }

        void Fill(List<string> paths)
        {
            int seq = ++loadSeq;
            list.BeginUpdate();
            list.Items.Clear();
            bigImgs.Images.Clear(); smallImgs.Images.Clear();
            AddPlaceholder("~dir", true); AddPlaceholder("~file", false);
            var items = new List<ListViewItem>();
            foreach (var p in paths)
            {
                bool dir = Directory.Exists(p);
                var it = new ListViewItem(Path.GetFileName(p.TrimEnd('\\'))) { Tag = p, ImageKey = dir ? "~dir" : "~file" };
                DateTime mt = DateTime.MinValue; long size = -1;
                try { if (dir) mt = Directory.GetLastWriteTime(p); else { var fi = new FileInfo(p); mt = fi.LastWriteTime; size = fi.Length; } } catch { }
                it.SubItems.Add(mt == DateTime.MinValue ? "" : mt.ToString("d MMM yyyy, HH:mm"));
                it.SubItems.Add(TypeName(p, dir));
                it.SubItems.Add(size < 0 ? "--" : FileOps.HumanSize(size));
                items.Add(it);
            }
            list.Items.AddRange(items.ToArray());
            list.EndUpdate();
            UpdateStatus();
            // iconos y miniaturas en segundo plano
            var todo = paths.ToList();
            int px = (int)(64 * s) <= 64 ? 96 : 128;
            var th = new Thread(() =>
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    if (seq != loadSeq || IsDisposed) return;
                    string p = todo[i];
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    bool thumb = ".png.jpg.jpeg.gif.bmp.webp.mp4.mov.mkv.avi.pdf.heic".Contains(ext) && ext != "";
                    var bmp = Shell.GetIcon(p, px, thumb ? 0 : 4);
                    if (bmp == null) continue;
                    int idx = i;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (seq != loadSeq || IsDisposed) { bmp.Dispose(); return; }
                            string key = "i" + idx;
                            bigImgs.Images.Add(key, Fit(bmp, bigImgs.ImageSize.Width));
                            smallImgs.Images.Add(key, Fit(bmp, smallImgs.ImageSize.Width));
                            bmp.Dispose();
                            if (idx < list.Items.Count && (string)list.Items[idx].Tag == p) list.Items[idx].ImageKey = key;
                        }));
                    }
                    catch { return; }
                }
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }

        static Bitmap phDir, phFile;
        void AddPlaceholder(string key, bool dir)
        {
            if (phDir == null) phDir = Shell.GetIcon(Path.GetTempPath().TrimEnd('\\'), 96, 4);
            if (phFile == null) phFile = Shell.GetIcon(Path.Combine(Environment.SystemDirectory, "drivers\\etc\\hosts"), 96, 4);
            var b = (dir ? phDir : phFile) ?? new Bitmap(16, 16);
            bigImgs.Images.Add(key, Fit(b, bigImgs.ImageSize.Width));
            smallImgs.Images.Add(key, Fit(b, smallImgs.ImageSize.Width));
        }
        // centra la imagen en un cuadrado sin deformarla (las fotos verticales u horizontales se ven bien)
        static Bitmap Fit(Bitmap src, int size)
        {
            var dst = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float k = Math.Min((float)size / src.Width, (float)size / src.Height);
                float w = src.Width * k, h = src.Height * k;
                g.DrawImage(src, (size - w) / 2, (size - h), w, h);
            }
            return dst;
        }

        int sortCol = 0; bool sortDesc;
        static readonly string[] SortNames = { "Nombre", "Fecha de modificación", "Tipo", "Tamaño" };

        List<string> Ordered(List<string> src)
        {
            Func<string, IComparable> key;
            if (sortCol == 1) key = p => { try { return Directory.Exists(p) ? Directory.GetLastWriteTime(p) : File.GetLastWriteTime(p); } catch { return DateTime.MinValue; } };
            else if (sortCol == 2) key = p => TypeName(p, Directory.Exists(p));
            else if (sortCol == 3) key = p => { try { return Directory.Exists(p) ? -1L : new FileInfo(p).Length; } catch { return -1L; } };
            else key = p => Path.GetFileName(p).ToLowerInvariant();
            var dirsFirst = src.OrderByDescending(p => Directory.Exists(p));
            return (sortDesc ? dirsFirst.ThenByDescending(p => key(p)) : dirsFirst.ThenBy(p => key(p))).ToList();
        }

        void SortBy(int col)   // clic en la columna: misma columna invierte el orden
        {
            if (col == sortCol) sortDesc = !sortDesc; else { sortCol = col; sortDesc = col == 1 || col == 3; }
            ApplySort();
        }

        void SetSort(int col, bool desc) { sortCol = col; sortDesc = desc; ApplySort(); }

        void ApplySort()
        {
            allEntries = Ordered(allEntries);
            if (search.Text.Trim() != "" && !searchMode) ApplyFilter();
            else Fill(searchMode ? Ordered(list.Items.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList()) : allEntries);
            topBar.Invalidate();
        }

        void SortMenu(Control c, Point p)
        {
            var m = MenuStyle.New(light);
            for (int i = 0; i < SortNames.Length; i++)
            {
                int col = i;
                var mi = new ToolStripMenuItem(SortNames[i]) { Checked = sortCol == i };
                mi.Click += (o, e) => SetSort(col, col == 1 || col == 3);
                m.Items.Add(mi);
            }
            m.Items.Add(new ToolStripSeparator());
            var asc = new ToolStripMenuItem("Ascendente") { Checked = !sortDesc }; asc.Click += (o, e) => SetSort(sortCol, false); m.Items.Add(asc);
            var des = new ToolStripMenuItem("Descendente") { Checked = sortDesc }; des.Click += (o, e) => SetSort(sortCol, true); m.Items.Add(des);
            m.ShowCheckMargin = true;
            m.Show(c, p);
        }
        List<string> SelectedPaths() { return list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList(); }

        void OpenSelected()
        {
            var sel = SelectedPaths();
            if (sel.Count == 1 && Directory.Exists(sel[0])) { Navigate(sel[0], true); return; }
            foreach (var p in sel.Take(10)) FileOps.Open(p);
        }

        void UpdateStatus()
        {
            if (searchMode) return;
            int n = list.Items.Count, k = list.SelectedItems.Count;
            string free = "";
            try { var d = new DriveInfo(Path.GetPathRoot(current)); free = "   ·   " + FileOps.HumanSize(d.AvailableFreeSpace) + " disponibles"; } catch { }
            statusText = (k > 0 ? k + " de " : "") + n + " elementos" + free;
            pathBar.Invalidate();
        }

        void ListKeyDown(object o, KeyEventArgs e)
        {
            var sel = SelectedPaths();
            if (e.KeyCode == Keys.Space && sel.Count > 0) { e.SuppressKeyPress = true; QuickLook.Toggle(sel[0], light); }
            else if (e.KeyCode == Keys.Back || (e.Alt && e.KeyCode == Keys.Left)) { e.SuppressKeyPress = true; GoBack(); }
            else if (e.Alt && e.KeyCode == Keys.Right) { e.SuppressKeyPress = true; GoFwd(); }
            else if (e.Alt && e.KeyCode == Keys.Up) { e.SuppressKeyPress = true; GoUp(); }
            else if (e.KeyCode == Keys.Delete && sel.Count > 0) { e.SuppressKeyPress = true; if (FileOps.Recycle(Handle, sel)) Reload(); }
            else if (e.KeyCode == Keys.F2 && list.SelectedItems.Count == 1) { e.SuppressKeyPress = true; list.SelectedItems[0].BeginEdit(); }
            else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; Reload(); }
            else if (e.Control && e.KeyCode == Keys.C && sel.Count > 0) { e.SuppressKeyPress = true; FileOps.ToClipboard(sel, false); }
            else if (e.Control && e.KeyCode == Keys.X && sel.Count > 0) { e.SuppressKeyPress = true; FileOps.ToClipboard(sel, true); }
            else if (e.Control && e.KeyCode == Keys.V) { e.SuppressKeyPress = true; FileOps.Paste(Handle, current); Reload(); }
            else if (e.Control && e.KeyCode == Keys.A) { e.SuppressKeyPress = true; list.BeginUpdate(); foreach (ListViewItem i in list.Items) i.Selected = true; list.EndUpdate(); }
            else if (e.Control && e.Shift && e.KeyCode == Keys.N) { e.SuppressKeyPress = true; NewFolder(); }
            else if (e.Control && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; search.Focus(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.L) || keyData == (Keys.Control | Keys.Shift | Keys.G) || keyData == (Keys.Alt | Keys.D)) { EditPath(); return true; }
            if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
            if (keyData == Keys.F5) { Reload(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.OemPeriod)) { showDot = !showDot; Reload(); return true; }
            if (keyData == (Keys.Control | Keys.D1)) { SetView(View.LargeIcon); return true; }
            if (keyData == (Keys.Control | Keys.D2)) { SetView(View.Details); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Reload()
        {
            var keep = SelectedPaths();
            LoadFolder();
            foreach (ListViewItem i in list.Items) if (keep.Contains((string)i.Tag)) i.Selected = true;
        }

        void NewFolder()
        {
            try
            {
                string p = FileOps.NewFolder(current);
                LoadFolder();
                var it = list.Items.Cast<ListViewItem>().FirstOrDefault(i => (string)i.Tag == p);
                if (it != null) { it.Selected = true; it.EnsureVisible(); it.BeginEdit(); }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Finder"); }
        }

        void AfterRename(object o, LabelEditEventArgs e)
        { renaming = false;
            if (e.Label == null || e.Label.Trim() == "") { e.CancelEdit = true; return; }
            string p = (string)list.Items[e.Item].Tag;
            if (!FileOps.Rename(Handle, p, e.Label.Trim())) { e.CancelEdit = true; return; }
            string np = Path.Combine(Path.GetDirectoryName(p.TrimEnd('\\')), e.Label.Trim());
            list.Items[e.Item].Tag = np;
            int idx = allEntries.IndexOf(p); if (idx >= 0) allEntries[idx] = np;
        }

        void SetView(View v)
        {
            list.View = v;
            if (v == View.Details) ThemeList();
            topBar.Invalidate();
        }

        // ---------------- arrastrar y soltar ----------------
        string DropTarget(DragEventArgs e)
        {
            var pt = list.PointToClient(new Point(e.X, e.Y));
            var hit = list.HitTest(pt).Item;
            if (hit != null) { string p = (string)hit.Tag; if (Directory.Exists(p)) return p; }
            return current;
        }

        static DragDropEffects DropEffect(DragEventArgs e, string dest)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || string.IsNullOrEmpty(dest)) return DragDropEffects.None;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return DragDropEffects.None;
            if ((e.KeyState & 8) != 0) return DragDropEffects.Copy;   // Ctrl
            if ((e.KeyState & 4) != 0) return DragDropEffects.Move;   // Shift
            bool same = string.Equals(Path.GetPathRoot(files[0]), Path.GetPathRoot(dest), StringComparison.OrdinalIgnoreCase);
            return same ? DragDropEffects.Move : DragDropEffects.Copy;
        }

        void DoDrop(DragEventArgs e, string dest)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            Keys mods = Keys.None;
            if ((e.KeyState & 8) != 0) mods |= Keys.Control;
            if ((e.KeyState & 4) != 0) mods |= Keys.Shift;
            BeginInvoke(new Action(() => { FileOps.Drop(Handle, files, dest, mods); Reload(); }));
        }

        // ---------------- menu contextual ----------------
        void ShowContext(ListViewItem item, Point pt)
        {
            var m = MenuStyle.New(light);
            Action<string, Action> add = (t, a) => { var mi = new ToolStripMenuItem(t); mi.Click += (o, e) => { try { a(); } catch (Exception ex) { Program.Log(ex); } }; m.Items.Add(mi); };
            if (item != null)
            {
                if (!item.Selected) { list.SelectedItems.Clear(); item.Selected = true; }
                var sel = SelectedPaths();
                string p = sel[0];
                add("Abrir", OpenSelected);
                if (!Directory.Exists(p)) add("Abrir con...", () => FileOps.OpenWith(p));
                add("Vista rápida (Espacio)", () => QuickLook.Toggle(p, light));
                m.Items.Add(new ToolStripSeparator());
                add("Copiar", () => FileOps.ToClipboard(sel, false));
                add("Cortar", () => FileOps.ToClipboard(sel, true));
                add("Copiar ruta", () => Clipboard.SetText(string.Join("\r\n", sel.ToArray())));
                add("Renombrar", () => item.BeginEdit());
                m.Items.Add(new ToolStripSeparator());
                add("Mover a la papelera", () => { if (FileOps.Recycle(Handle, sel)) Reload(); });
                m.Items.Add(new ToolStripSeparator());
                add("Mostrar en el Explorador", () => FileOps.ShowInExplorer(p));
                add("Propiedades", () => FileOps.Properties(p));
            }
            else
            {
                add("Nueva carpeta", NewFolder);
                var sortItem = new ToolStripMenuItem("Ordenar por");
                for (int i = 0; i < SortNames.Length; i++) { int col = i; var si = new ToolStripMenuItem(SortNames[i]) { Checked = sortCol == i }; si.Click += (o, e) => SetSort(col, col == 1 || col == 3); sortItem.DropDownItems.Add(si); }
                sortItem.DropDownItems.Add(new ToolStripSeparator());
                var ascI = new ToolStripMenuItem("Ascendente") { Checked = !sortDesc }; ascI.Click += (o, e) => SetSort(sortCol, false); sortItem.DropDownItems.Add(ascI);
                var desI = new ToolStripMenuItem("Descendente") { Checked = sortDesc }; desI.Click += (o, e) => SetSort(sortCol, true); sortItem.DropDownItems.Add(desI);
                m.Items.Add(sortItem);
                if (Clipboard.ContainsFileDropList()) add("Pegar", () => { FileOps.Paste(Handle, current); Reload(); });
                m.Items.Add(new ToolStripSeparator());
                add(list.View == View.LargeIcon ? "Ver como lista" : "Ver como iconos", () => SetView(list.View == View.LargeIcon ? View.Details : View.LargeIcon));
                add("Actualizar", Reload);
                add("Abrir esta carpeta en el Explorador", () => Process.Start("explorer.exe", "\"" + current + "\""));
                add("Abrir terminal aquí", () => Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = current, UseShellExecute = true }));
                add("Propiedades de la carpeta", () => FileOps.Properties(current));
            }
            m.Show(list, pt);
        }

        // ---------------- barra lateral ----------------
        void BuildSidebar()
        {
            sideItems.Clear();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            sideItems.Add(new SideItem { Title = "Favoritos", Header = true });
            var favs = new List<KeyValuePair<string, string>> {
                new KeyValuePair<string, string>("Inicio (" + Environment.UserName + ")", home),
                new KeyValuePair<string, string>("Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                new KeyValuePair<string, string>("Descargas", N.Downloads()),
                new KeyValuePair<string, string>("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                new KeyValuePair<string, string>("Imágenes", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                new KeyValuePair<string, string>("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
                new KeyValuePair<string, string>("Música", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)) };
            // carpetas fijadas en el acceso rapido (tus proyectos)
            try
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                dynamic sh = Activator.CreateInstance(t);
                dynamic qa = sh.Namespace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
                foreach (dynamic it in qa.Items())
                {
                    string p = it.Path;
                    if (string.IsNullOrEmpty(p) || !Directory.Exists(p) || favs.Any(f => string.Equals(f.Value, p, StringComparison.OrdinalIgnoreCase))) continue;
                    favs.Add(new KeyValuePair<string, string>(Path.GetFileName(p.TrimEnd('\\')), p));
                    if (favs.Count > 14) break;
                }
                Marshal.ReleaseComObject(sh);
            }
            catch { }
            foreach (var f in favs) if (Directory.Exists(f.Value)) sideItems.Add(new SideItem { Title = f.Key, Path = f.Value });
            sideItems.Add(new SideItem { Title = "Ubicaciones", Header = true });
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && (x.DriveType == DriveType.Fixed || x.DriveType == DriveType.Removable || x.DriveType == DriveType.Network)))
            {
                string label = ""; try { label = d.VolumeLabel; } catch { }
                sideItems.Add(new SideItem { Title = (label == "" ? "Disco local" : label) + " (" + d.Name.TrimEnd('\\') + ")", Path = d.RootDirectory.FullName });
            }
            sideItems.Add(new SideItem { Title = "Papelera", Path = "shell:RecycleBinFolder" });
            var th = new Thread(() =>
            {
                foreach (var it in sideItems.ToList())
                {
                    if (it.Header || IsDisposed) continue;
                    it.Icon = it.Path.StartsWith("shell:") ? Shell.GetIcon("::{645FF040-5081-101B-9F08-00AA002F954E}", 32, 4) : Shell.GetIcon(it.Path, 32, 4);
                }
                try { BeginInvoke(new Action(() => sideBar.Invalidate())); } catch { }
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }

        static string GlyphFor(SideItem it)
        {
            string p = it.Path ?? "", home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Func<Environment.SpecialFolder, bool> eq = sf => string.Equals(p.TrimEnd('\\'), Environment.GetFolderPath(sf).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (p.StartsWith("shell:")) return "\uE74D";                                   // papelera
            if (string.Equals(p.TrimEnd('\\'), home, StringComparison.OrdinalIgnoreCase)) return "\uE80F";   // inicio
            if (eq(Environment.SpecialFolder.DesktopDirectory)) return "\uE7F4";            // escritorio
            if (string.Equals(p.TrimEnd('\\'), N.Downloads().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "\uE896";   // descargas
            if (eq(Environment.SpecialFolder.MyDocuments)) return "\uE8A5";                 // documentos
            if (eq(Environment.SpecialFolder.MyPictures)) return "\uEB9F";                  // imagenes
            if (eq(Environment.SpecialFolder.MyVideos)) return "\uE714";                    // videos
            if (eq(Environment.SpecialFolder.MyMusic)) return "\uE8D6";                     // musica
            if (p.Length <= 3) return "\uEDA2";                                             // disco
            return "\uE8B7";                                                                // carpeta
        }

        SideItem SideAt(Point p) { return sideItems.FirstOrDefault(i => !i.Header && i.Rect.Contains(p)); }

        void PaintSide(object o, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float y = 12 * s, rowH = 28 * s;
            using (var hf = new Font(UiFont.Semi, 8.5f)) using (var hb = new SolidBrush(sub)) using (var fb = new SolidBrush(fg))
            {
                foreach (var it in sideItems)
                {
                    if (it.Header) { y += 8 * s; g.DrawString(it.Title, hf, hb, 16 * s, y); y += 22 * s; it.Rect = RectangleF.Empty; continue; }
                    it.Rect = new RectangleF(8 * s, y, sideBar.Width - 16 * s, rowH);
                    bool on = !searchMode && string.Equals(it.Path.TrimEnd('\\'), current.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                    if (on) using (var p = Round(it.Rect, 6 * s)) using (var b = new SolidBrush(light ? Color.FromArgb(214, 214, 222) : Color.FromArgb(64, 64, 72))) g.FillPath(b, p);
                    using (var gf = new Font("Segoe MDL2 Assets", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel)) using (var gb = new SolidBrush(accent))
                    { var sfG = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }; g.DrawString(GlyphFor(it), gf, gb, new RectangleF(14 * s, y, 22 * s, rowH), sfG); }
                    var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
                    g.DrawString(it.Title, fUi, fb, new RectangleF(42 * s, y, sideBar.Width - 52 * s, rowH), sf);
                    y += rowH + 1 * s;
                }
            }
        }

        void SideClick(object o, MouseEventArgs e)
        {
            var it = SideAt(e.Location);
            if (it == null) return;
            if (it.Path.StartsWith("shell:")) { Process.Start("explorer.exe", it.Path); return; }
            if (e.Button == MouseButtons.Right)
            {
                var m = MenuStyle.New(light);
                var a = new ToolStripMenuItem("Abrir en el Explorador"); a.Click += (s2, e2) => Process.Start("explorer.exe", "\"" + it.Path + "\""); m.Items.Add(a);
                m.Show(sideBar, e.Location); return;
            }
            Navigate(it.Path, true);
        }

        // ---------------- barra superior ----------------
        void PaintTop(object o, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float cy = topBar.Height / 2f;
            { var lp = LightsPos; TrafficLights.Draw(g, lp.X, lp.Y, s, lightsActive, lightsHover >= 0, light); }
            float x = sideBar.Width + 14 * s;
            rBack = new RectangleF(x, cy - 14 * s, 28 * s, 28 * s); rFwd = new RectangleF(x + 30 * s, cy - 14 * s, 28 * s, 28 * s);
            DrawChevron(g, rBack, true, back.Count > 0); DrawChevron(g, rFwd, false, fwd.Count > 0);
            string title = searchMode ? "Resultados" : FolderTitle(current);
            using (var b = new SolidBrush(fg)) g.DrawString(title, fUiSemi, b, x + 70 * s, cy - fUiSemi.Height / 2f);
            // selector de vista: iconos | lista
            float vx = search.Left - 84 * s;
            rIcons = new RectangleF(vx, cy - 13 * s, 34 * s, 26 * s); rList = new RectangleF(vx + 36 * s, cy - 13 * s, 34 * s, 26 * s);
            rSort = new RectangleF(vx - 44 * s, cy - 13 * s, 34 * s, 26 * s);
            using (var p = Round(rSort, 7 * s)) using (var b = new SolidBrush(light ? Color.FromArgb(226, 226, 232) : Color.FromArgb(52, 52, 58))) g.FillPath(b, p);
            using (var fi = new Font("Segoe MDL2 Assets", 11f * s, FontStyle.Regular, GraphicsUnit.Pixel)) using (var b = new SolidBrush(fg))
            { var sf0 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }; g.DrawString("\uE8CB", fi, b, rSort, sf0); }
            using (var p = Round(new RectangleF(vx - 2 * s, cy - 15 * s, 74 * s, 30 * s), 7 * s)) using (var b = new SolidBrush(light ? Color.FromArgb(226, 226, 232) : Color.FromArgb(52, 52, 58))) g.FillPath(b, p);
            var onR = list.View == View.LargeIcon ? rIcons : rList;
            using (var p = Round(onR, 6 * s)) using (var b = new SolidBrush(light ? Color.White : Color.FromArgb(84, 84, 92))) g.FillPath(b, p);
            using (var b = new SolidBrush(fg))
            {
                float q = 4.2f * s, gap = 1.6f * s, ox = rIcons.X + rIcons.Width / 2 - q - gap / 2, oy = cy - q - gap / 2;
                g.FillRectangle(b, ox, oy, q, q); g.FillRectangle(b, ox + q + gap, oy, q, q); g.FillRectangle(b, ox, oy + q + gap, q, q); g.FillRectangle(b, ox + q + gap, oy + q + gap, q, q);
                float lx = rList.X + rList.Width / 2 - 7 * s;
                for (int i = -1; i <= 1; i++) g.FillRectangle(b, lx, cy + i * 4.5f * s - 0.8f * s, 14 * s, 1.7f * s);
            }
            // caja de busqueda
            var sr = new RectangleF(search.Left - 28 * s, search.Top - 6 * s, search.Width + 36 * s, search.Height + 12 * s);
            using (var p = Round(sr, 7 * s)) using (var b = new SolidBrush(search.BackColor)) g.FillPath(b, p);
            using (var pen = new Pen(sub, 1.6f * s))
            {
                float mx = sr.X + 14 * s, my = cy - 1 * s, r = 5 * s;
                g.DrawEllipse(pen, mx - r, my - r, r * 2, r * 2); g.DrawLine(pen, mx + r * 0.7f, my + r * 0.7f, mx + r * 1.5f, my + r * 1.5f);
            }

            using (var pen = new Pen(line)) g.DrawLine(pen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
        }

        void DrawChevron(Graphics g, RectangleF r, bool left, bool enabled)
        {
            using (var pen = new Pen(enabled ? fg : Color.FromArgb(90, sub), 2f * s))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, d = 5 * s;
                if (left) g.DrawLines(pen, new[] { new PointF(cx + d / 2, cy - d), new PointF(cx - d / 2, cy), new PointF(cx + d / 2, cy + d) });
                else g.DrawLines(pen, new[] { new PointF(cx - d / 2, cy - d), new PointF(cx + d / 2, cy), new PointF(cx - d / 2, cy + d) });
            }
        }

        void TopClick(object o, MouseEventArgs e)
        {
            var lpp = LightsPos; int li = TrafficLights.Hit(e.Location, lpp.X, lpp.Y, s);
            if (li == 0) { Close(); return; }
            if (li == 1) { WindowState = FormWindowState.Minimized; return; }
            if (li == 2) { Borderless.ToggleMax(this); return; }
            if (rBack.Contains(e.Location)) GoBack();
            else if (rFwd.Contains(e.Location)) GoFwd();
            else if (rSort.Contains(e.Location)) SortMenu(topBar, new Point((int)rSort.Left, (int)rSort.Bottom + 4));
            else if (rIcons.Contains(e.Location)) SetView(View.LargeIcon);
            else if (rList.Contains(e.Location)) SetView(View.Details);
            else if (e.X > search.Left - 30 * s) search.Focus();
        }

        // ---------------- barra de ruta ----------------
        void PaintPath(object o, PaintEventArgs e)
        {
            var g = e.Graphics; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var pen = new Pen(line)) g.DrawLine(pen, 0, 0, pathBar.Width, 0);
            crumbs.Clear();
            var parts = new List<string>();
            var di = new DirectoryInfo(current.TrimEnd('\\') + "\\");
            for (var d = di; d != null; d = d.Parent) parts.Insert(0, d.FullName);
            float x = sideBar.Width + 12 * s, cy = pathBar.Height / 2f;
            using (var fb = new SolidBrush(fg)) using (var sb = new SolidBrush(sub))
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    string name = FolderTitle(parts[i]).TrimEnd('\\');
                    var sz = g.MeasureString(name, fSmall);
                    var r = new RectangleF(x, cy - sz.Height / 2, sz.Width, sz.Height);
                    g.DrawString(name, fSmall, i == parts.Count - 1 ? fb : sb, r.X, r.Y);
                    crumbs.Add(new KeyValuePair<RectangleF, string>(r, parts[i]));
                    x += sz.Width + 2 * s;
                    if (i < parts.Count - 1) { g.DrawString("›", fSmall, sb, x, r.Y); x += 12 * s; }
                }
                var st = g.MeasureString(statusText, fSmall);
                g.DrawString(statusText, fSmall, sb, pathBar.Width - st.Width - 14 * s, cy - st.Height / 2);
            }
        }

        void PathClick(object o, MouseEventArgs e)
        {
            foreach (var c in crumbs) if (c.Key.Contains(e.Location)) { Navigate(c.Value, true); return; }
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
            loadSeq++;
            try { if (watcher != null) watcher.Dispose(); if (watchTimer != null) watchTimer.Dispose(); } catch { }
            try { if (fUi != null) fUi.Dispose(); if (fUiSemi != null) fUiSemi.Dispose(); if (fSmall != null) fSmall.Dispose(); } catch { }
            bigImgs.Dispose(); smallImgs.Dispose();
            base.OnFormClosed(e);
        }
    }
}
