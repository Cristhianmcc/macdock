using System;
using System.Collections.Generic;
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
    // Pila "Escritorio" estilo Mac: muestra lo que hay en el escritorio agrupado por tipo, sin mover nada.
    class StackPopup : Form
    {
        class Entry { public string Path, Name; public bool Dir; public string Group; public RectangleF Rect; }

        static readonly string[][] Groups = {
            new[] { "Carpetas" },
            new[] { "Accesos directos", ".lnk", ".url" },
            new[] { "Documentos", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".tsv", ".md", ".odt", ".rtf" },
            new[] { "Imagenes", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".heic", ".ico", ".psd", ".ai" },
            new[] { "Videos y audio", ".mp4", ".mov", ".mkv", ".avi", ".webm", ".mp3", ".wav", ".ogg", ".m4a", ".flac" },
            new[] { "Comprimidos e instaladores", ".zip", ".rar", ".7z", ".exe", ".msi", ".apk", ".iso" },
            new[] { "Codigo y scripts", ".ps1", ".cmd", ".bat", ".py", ".js", ".ts", ".sql", ".yml", ".yaml", ".json", ".code-workspace", ".html", ".css", ".sh", ".env", ".xml" },
            new[] { "Otros" },
        };

        // cache de iconos compartida entre aperturas (se refresca si el archivo cambia)
        static readonly Dictionary<string, Bitmap> Icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly object IconLock = new object();

        List<Entry> entries = new List<Entry>();
        float s; bool light;
        float scroll = 0, contentH = 0;
        int hover = -1;
        string filter = "";
        Thread loader; volatile bool closing;
        RectangleF openBtn;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        string stackTitle; string[] folders; bool byDate;
        static readonly string[] DateGroups = { "Hoy", "Ayer", "Esta semana", "Este mes", "Antes" };

        // Pila del Escritorio (agrupada por tipo)
        public StackPopup(float scale, bool lightTheme, Point anchor, Rectangle screen)
            : this(scale, lightTheme, anchor, screen, "Escritorio",
                   new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }, true) { }   // lo mas reciente primero

        // Pila generica: carpetas a mostrar y si se agrupa por fecha (Descargas) o por tipo
        public StackPopup(float scale, bool lightTheme, Point anchorBottomCenter, Rectangle screen, string title, string[] dirs, bool groupByDate)
        {
            s = scale; light = lightTheme; stackTitle = title; folders = dirs; byDate = groupByDate;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(32, 32, 36);
            Text = title;

            LoadEntries();
            int cols = 6;
            int w = (int)(Pad * 2 + CellW * cols);
            Arrange(w);
            int h = (int)Math.Min(screen.Height * 0.72f, HeaderH + contentH + Pad);
            h = Math.Max(h, (int)(160 * s));
            int x = Math.Max(screen.Left + 8, Math.Min(screen.Right - w - 8, anchorBottomCenter.X - w / 2));
            int y = Math.Max(screen.Top + 8, anchorBottomCenter.Y - h);
            Bounds = new Rectangle(x, y, w, h);
        }

        float Pad { get { return 14 * s; } }
        float CellW { get { return 96 * s; } }
        float CellH { get { return 100 * s; } }
        float IconSz { get { return 50 * s; } }
        float HeaderH { get { return 50 * s; } }
        float GroupH { get { return 28 * s; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; cp.ClassStyle |= 0x20000; return cp; }
        }

        void LoadEntries()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var times = new Dictionary<string, DateTime>();
            foreach (var d in folders)
            {
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
                IEnumerable<string> items;
                try { items = Directory.EnumerateFileSystemEntries(d); } catch { continue; }
                foreach (var p in items)
                {
                    try
                    {
                        var attr = File.GetAttributes(p);
                        if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        string name = Path.GetFileName(p);
                        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        bool dir = (attr & FileAttributes.Directory) != 0;
                        string shown = dir ? name : (Path.GetExtension(p).ToLowerInvariant() == ".lnk" || Path.GetExtension(p).ToLowerInvariant() == ".url" ? Path.GetFileNameWithoutExtension(p) : name);
                        if (!seen.Add(shown + "|" + dir)) continue;
                        if (name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        DateTime t = DateTime.MinValue;
                        if (byDate) { try { t = dir ? Directory.GetCreationTime(p) : File.GetCreationTime(p); var w = File.GetLastWriteTime(p); if (w > t) t = w; } catch { } }
                        times[p] = t;
                        entries.Add(new Entry { Path = p, Name = shown, Dir = dir, Group = byDate ? DateGroupOf(t) : GroupOf(p, dir) });
                    }
                    catch { }
                }
            }
            if (byDate)
                entries = entries.OrderByDescending(e => times[e.Path]).Take(300)
                                 .OrderBy(e => Array.IndexOf(DateGroups, e.Group)).ThenByDescending(e => times[e.Path]).ToList();
            else
                entries = entries.OrderBy(e => Array.FindIndex(Groups, g => g[0] == e.Group))
                                 .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static string DateGroupOf(DateTime t)
        {
            var today = DateTime.Today;
            if (t >= today) return "Hoy";
            if (t >= today.AddDays(-1)) return "Ayer";
            if (t >= today.AddDays(-7)) return "Esta semana";
            if (t >= today.AddDays(-30)) return "Este mes";
            return "Antes";
        }

        static string GroupOf(string p, bool dir)
        {
            if (dir) return "Carpetas";
            string ext = Path.GetExtension(p).ToLowerInvariant();
            foreach (var g in Groups) for (int i = 1; i < g.Length; i++) if (g[i] == ext) return g[0];
            return "Otros";
        }

        IEnumerable<Entry> VisibleEntries()
        {
            if (filter == "") return entries;
            return entries.Where(e => e.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        void Arrange(int width)
        {
            int cols = Math.Max(1, (int)((width - Pad * 2) / CellW));
            float y = 0; string group = null; int col = 0;
            foreach (var e in entries) e.Rect = RectangleF.Empty;
            foreach (var e in VisibleEntries())
            {
                if (e.Group != group)
                {
                    if (group != null) y += CellH;
                    group = e.Group; col = 0; y += GroupH;
                }
                else if (col == cols) { col = 0; y += CellH; }
                e.Rect = new RectangleF(Pad + col * CellW, y, CellW, CellH);
                col++;
            }
            contentH = group == null ? 40 * s : y + CellH;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            StartLoader();
        }

        void StartLoader()
        {
            var list = entries.Select(x => x.Path).ToList();
            int px = (int)Math.Ceiling(IconSz) <= 64 ? 64 : 96;
            loader = new Thread(() =>
            {
                foreach (var p in list)
                {
                    if (closing) return;
                    string key = p + "|" + SafeTime(p);
                    lock (IconLock) { if (Icons.ContainsKey(key)) continue; }
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    bool thumb = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".gif" || ext == ".bmp" || ext == ".mp4" || ext == ".mov";
                    var bmp = Shell.GetIcon(p, px, thumb ? 0 : 4);
                    lock (IconLock) { Icons[key] = bmp; }
                    if (!closing) try { BeginInvoke(new Action(Invalidate)); } catch { }
                }
            });
            loader.IsBackground = true;
            loader.SetApartmentState(ApartmentState.STA);
            loader.Start();
        }

        static string SafeTime(string p) { try { return File.GetLastWriteTimeUtc(p).Ticks.ToString(); } catch { return ""; } }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (!draggingOut) Close(); }

        // ---- arrastrar archivos fuera de la pila (como en el Explorador) ----
        int downIdx = -1; Point downPt; bool draggingOut;

        float ViewH { get { return Height - HeaderH; } }
        RectangleF ThumbRect()
        {
            float view = ViewH; if (contentH <= view) return RectangleF.Empty;
            float barH = Math.Max(30 * s, view * view / contentH);
            float barY = HeaderH + (view - barH) * (scroll / (contentH - view));
            return new RectangleF(Width - 12 * s, barY, 12 * s, barH);
        }
        bool draggingBar; float barStartY, barStartScroll;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && contentH > ViewH && e.X >= Width - 16 * s && e.Y > HeaderH)
            {
                var t = ThumbRect();
                if (!t.Contains(e.X, e.Y))   // clic en la pista: salta a esa posicion
                {
                    float frac = Math.Max(0, Math.Min(1, (e.Y - HeaderH - t.Height / 2) / (ViewH - t.Height)));
                    scroll = frac * (contentH - ViewH); Invalidate(); t = ThumbRect();
                }
                draggingBar = true; barStartY = e.Y; barStartScroll = scroll; downIdx = -1; Capture = true;
                return;
            }
            downIdx = e.Button == MouseButtons.Left ? HitTest(e.Location) : -1;
            downPt = e.Location;
        }

        void StartDrag(Entry en)
        {
            draggingOut = true;
            try
            {
                var data = new DataObject();
                var list = new System.Collections.Specialized.StringCollection(); list.Add(en.Path);
                data.SetFileDropList(list);
                Opacity = 0;   // la pila se esconde para que puedas soltar en lo que esta detras
                DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            catch (Exception ex) { Program.Log(ex); }
            finally { draggingOut = false; downIdx = -1; Close(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { closing = true; base.OnFormClosed(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { if (filter != "") { filter = ""; Refilter(); } else Close(); }
            else if (e.KeyCode == Keys.Back && filter.Length > 0) { filter = filter.Substring(0, filter.Length - 1); Refilter(); }
            else if (e.KeyCode == Keys.Back || (e.Alt && e.KeyCode == Keys.Left)) { GoBackLevel(); }
            else if (e.KeyCode == Keys.Space && hover >= 0 && filter == "")
            {
                e.SuppressKeyPress = true; e.Handled = true;
                string p = entries[hover].Path; bool lt = light;
                Close(); QuickLook.Toggle(p, lt); return;
            }
            else if (e.KeyCode == Keys.Enter) { var v = VisibleEntries().FirstOrDefault(); if (v != null) Open(v); }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !(e.KeyChar == ' ' && filter == "")) { filter += e.KeyChar; Refilter(); }
            base.OnKeyPress(e);
        }

        void Refilter() { scroll = 0; hover = -1; Arrange(Width); Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float max = Math.Max(0, contentH - (Height - HeaderH - Pad / 2));
            scroll = Math.Max(0, Math.Min(max, scroll - e.Delta * 0.6f * s));
            Invalidate();
        }

        int HitTest(Point p)
        {
            if (p.Y < HeaderH) return -1;
            float cy = p.Y - HeaderH + scroll;
            for (int i = 0; i < entries.Count; i++)
                if (!entries[i].Rect.IsEmpty && entries[i].Rect.Contains(p.X, cy)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (draggingBar)
            {
                var t = ThumbRect();
                float track = ViewH - t.Height;
                if (track > 0) { scroll = Math.Max(0, Math.Min(contentH - ViewH, barStartScroll + (e.Y - barStartY) * (contentH - ViewH) / track)); Invalidate(); }
                return;
            }
            if (e.X >= Width - 16 * s && contentH > ViewH) Cursor = Cursors.Default;
            if (downIdx >= 0 && e.Button == MouseButtons.Left && !draggingOut &&
                (Math.Abs(e.X - downPt.X) > 6 * s || Math.Abs(e.Y - downPt.Y) > 6 * s))
            {
                StartDrag(entries[downIdx]);
                return;
            }
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = (h >= 0 || openBtn.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (draggingBar) { draggingBar = false; Capture = false; return; }
            if (draggingOut || IsDisposed) return;
            downIdx = -1;
            if (e.Button == MouseButtons.Left && history.Count > 0 && backBtn.Contains(e.Location)) { GoBackLevel(); return; }
            if (e.Button == MouseButtons.Left && openBtn.Contains(e.Location))
            {
                Process.Start("explorer.exe", "\"" + folders[0] + "\"");
                Close(); return;
            }
            int i = HitTest(e.Location);
            if (i < 0) return;
            if (e.Button == MouseButtons.Left) Open(entries[i]);
            else if (e.Button == MouseButtons.Right)
            {
                var en = entries[i];
                var m = MenuStyle.New(light);
                m.Items.Add("Abrir", null, (o, a) => Open(en));
                m.Items.Add("Vista rápida (Espacio)", null, (o, a) => { string p = en.Path; bool lt = light; Close(); QuickLook.Toggle(p, lt); });
                if (en.Dir) m.Items.Add("Abrir con el Explorador", null, (o, a) => { Process.Start("explorer.exe", "\"" + en.Path + "\""); Close(); });
                m.Items.Add("Mostrar en el Explorador", null, (o, a) => { Process.Start("explorer.exe", "/select,\"" + en.Path + "\""); Close(); });
                m.Items.Add("Copiar ruta", null, (o, a) => { try { Clipboard.SetText(en.Path); } catch { } });
                m.Show(this, e.Location);
            }
        }

        void Open(Entry e)
        {
            if (e.Dir) { EnterFolder(e.Path); return; }
            try { Process.Start(new ProcessStartInfo(e.Path) { UseShellExecute = true }); } catch (Exception ex) { Program.Log(ex); }
            Close();
        }

        // ---- navegar dentro de la pila ----
        class Level { public string[] Folders; public bool ByDate; public string Title; }
        List<Level> history = new List<Level>();
        RectangleF backBtn;

        void EnterFolder(string folder)
        {
            history.Add(new Level { Folders = folders, ByDate = byDate, Title = stackTitle });
            ShowLevel(new[] { folder }, false, Path.GetFileName(folder.TrimEnd('\\')));
        }

        void GoBackLevel()
        {
            if (history.Count == 0) return;
            var l = history[history.Count - 1]; history.RemoveAt(history.Count - 1);
            ShowLevel(l.Folders, l.ByDate, l.Title);
        }

        void ShowLevel(string[] dirs, bool date, string title)
        {
            folders = dirs; byDate = date; stackTitle = title; Text = title;
            filter = ""; scroll = 0; hover = -1;
            entries = new List<Entry>();
            LoadEntries();
            Arrange(Width);
            Invalidate();
            StartLoader();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color fg = light ? Color.FromArgb(28, 28, 30) : Color.FromArgb(236, 236, 240);
            Color sub = light ? Color.FromArgb(110, 110, 118) : Color.FromArgb(150, 150, 160);

            using (var tf = new Font(UiFont.Semi, 15f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(fg))
            {
                float tx = Pad;
                if (history.Count > 0)
                {
                    backBtn = new RectangleF(Pad - 4 * s, 10 * s, 26 * s, 28 * s);
                    using (var pen = new Pen(fg, 2f * s)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                        float cx = backBtn.X + 12 * s, cy = backBtn.Y + 14 * s, d = 5 * s;
                        g.DrawLines(pen, new[] { new PointF(cx + d / 2, cy - d), new PointF(cx - d / 2, cy), new PointF(cx + d / 2, cy + d) }); }
                    tx += 24 * s;
                }
                var sfT = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                g.DrawString(stackTitle, tf, b, new RectangleF(tx, 13 * s, 150 * s, 24 * s), sfT);
            }
            using (var f = new Font(UiFont.Reg, 12f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                string hint = filter != "" ? "Buscar: " + filter : "Escribe para buscar";
                using (var b = new SolidBrush(filter != "" ? fg : sub)) g.DrawString(hint, f, b, Pad + (history.Count > 0 ? 190 : 110) * s, 16 * s);
                string ob = "Abrir en el Explorador";
                var sz = g.MeasureString(ob, f);
                openBtn = new RectangleF(Width - Pad - sz.Width - 16 * s, 11 * s, sz.Width + 16 * s, 26 * s);
                using (var p = Round(openBtn, 6 * s))
                using (var bb = new SolidBrush(light ? Color.FromArgb(225, 225, 230) : Color.FromArgb(55, 55, 62))) g.FillPath(bb, p);
                using (var b = new SolidBrush(fg)) g.DrawString(ob, f, b, openBtn.X + 8 * s, openBtn.Y + 5 * s);
            }
            using (var pen = new Pen(light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255), 1))
                g.DrawLine(pen, Pad, HeaderH - 2, Width - Pad, HeaderH - 2);

            g.SetClip(new RectangleF(0, HeaderH, Width, Height - HeaderH));
            g.TranslateTransform(0, HeaderH - scroll);
            string group = null;
            var nameFmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            using (var gf = new Font(UiFont.Semi, 12f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var nf = new Font(UiFont.Reg, 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fgB = new SolidBrush(fg)) using (var subB = new SolidBrush(sub))
            using (var hovB = new SolidBrush(light ? Color.FromArgb(28, 0, 0, 0) : Color.FromArgb(36, 255, 255, 255)))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e.Rect.IsEmpty) continue;
                    if (e.Group != group)
                    {
                        group = e.Group;
                        int count = VisibleEntries().Count(v => v.Group == group);
                        g.DrawString(group + "  (" + count + ")", gf, subB, Pad + 4 * s, e.Rect.Y - GroupH + 6 * s);
                    }
                    if (e.Rect.Bottom < scroll || e.Rect.Top > scroll + Height) continue;
                    if (i == hover) using (var p = Round(RectangleF.Inflate(e.Rect, -3 * s, -3 * s), 8 * s)) g.FillPath(hovB, p);
                    var ir = new RectangleF(e.Rect.X + (e.Rect.Width - IconSz) / 2, e.Rect.Y + 8 * s, IconSz, IconSz);
                    Bitmap ic; lock (IconLock) { Icons.TryGetValue(e.Path + "|" + SafeTime(e.Path), out ic); }
                    if (ic != null)
                    {
                        float k = Math.Min(IconSz / ic.Width, IconSz / ic.Height);
                        float w = ic.Width * k, h = ic.Height * k;
                        g.DrawImage(ic, ir.X + (IconSz - w) / 2, ir.Y + (IconSz - h), w, h);
                    }
                    else using (var p = Round(RectangleF.Inflate(ir, -6 * s, -6 * s), 6 * s)) g.FillPath(hovB, p);
                    g.DrawString(e.Name, nf, fgB, new RectangleF(e.Rect.X + 4 * s, ir.Bottom + 5 * s, e.Rect.Width - 8 * s, nf.Height * 2.1f), nameFmt);
                }
            }
            g.ResetTransform(); g.ResetClip();
            if (filter != "" && !entries.Any(x => !x.Rect.IsEmpty))
                using (var f = new Font(UiFont.Reg, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(sub)) g.DrawString("Nada coincide con \"" + filter + "\"", f, b, Pad, HeaderH + 16 * s);

            float view = Height - HeaderH;
            if (contentH > view)
            {
                float barH = Math.Max(30 * s, view * view / contentH);
                float barY = HeaderH + (view - barH) * (scroll / (contentH - view));
                using (var p = Round(new RectangleF(Width - 9 * s, barY + 3 * s, 6 * s, barH - 6 * s), 3 * s))
                using (var b = new SolidBrush(light ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(80, 255, 255, 255))) g.FillPath(b, p);
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

    // Mostrar / ocultar los iconos del escritorio (igual que clic derecho > Ver > Mostrar iconos del escritorio).
    static class DesktopIcons
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        static IntPtr DefView()
        {
            IntPtr progman = N.FindWindow("Progman", null);
            IntPtr dv = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) return dv;
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            {
                dv = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero) return dv;
            }
            return IntPtr.Zero;
        }

        public static bool Visible
        {
            get
            {
                IntPtr dv = DefView(); if (dv == IntPtr.Zero) return true;
                IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
                return lv == IntPtr.Zero || N.IsWindowVisible(lv);
            }
        }

        public static void Toggle()
        {
            IntPtr dv = DefView();
            if (dv != IntPtr.Zero) SendMessage(dv, 0x0111, new IntPtr(0x7402), IntPtr.Zero);
        }
    }
}
