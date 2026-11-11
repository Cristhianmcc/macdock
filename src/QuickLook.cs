using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;

namespace MacDock
{
    // Quick Look estilo Mac: vista previa grande de un archivo. Espacio o Esc la cierran.
    // Usa los "visores" que Windows ya trae (PDF, Word, Excel...), imagenes y texto; si no hay visor, muestra una miniatura grande.
    class QuickLook : Form
    {
        public static QuickLook Current;

        public static void Toggle(string path, bool light)
        {
            if (Current != null && !Current.IsDisposed)
            {
                bool same = string.Equals(Current.file, path, StringComparison.OrdinalIgnoreCase);
                Current.Close();
                if (same || string.IsNullOrEmpty(path)) return;
            }
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) return;
            Current = new QuickLook(path, light);
            Current.FormClosed += (o, e) => { Current = null; };
            Current.Show();
            Current.Activate();
        }

        public static bool IsOpenAndFocused()
        {
            if (Current == null || Current.IsDisposed) return false;
            IntPtr fg = N.GetForegroundWindow();
            return fg == Current.Handle || Current.ContainsFocus || IsChildOf(fg, Current.Handle);
        }

        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        static bool IsChildOf(IntPtr h, IntPtr parent) { while (h != IntPtr.Zero) { if (h == parent) return true; h = GetParent(h); } return false; }

        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff" };
        static readonly string[] TextExt = { ".txt", ".md", ".json", ".js", ".ts", ".tsx", ".jsx", ".py", ".ps1", ".cs", ".css", ".scss", ".html", ".htm", ".xml",
            ".yml", ".yaml", ".sql", ".csv", ".tsv", ".log", ".env", ".ini", ".cfg", ".conf", ".cmd", ".bat", ".sh", ".java", ".kt", ".dart", ".php", ".go", ".rs", ".c", ".cpp", ".h", ".vue", ".svelte", ".gitignore", ".toml", ".properties" };

        string file; bool light;
        Panel host;
        IPreviewHandler handler;
        Color bg, fg, sub;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        QuickLook(string path, bool lightTheme)
        {
            file = path; light = lightTheme;
            bg = light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(30, 30, 34);
            fg = light ? Color.FromArgb(28, 28, 30) : Color.FromArgb(236, 236, 240);
            sub = light ? Color.FromArgb(110, 110, 118) : Color.FromArgb(150, 150, 160);
            Text = Path.GetFileName(path.TrimEnd('\\'));
            BackColor = bg; ForeColor = fg;
            KeyPreview = true; TopMost = true; ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; Padding = new Padding(2);
            StartPosition = FormStartPosition.Manual;
            var wa = Screen.PrimaryScreen.WorkingArea;
            int w = (int)(wa.Width * 0.62), h = (int)(wa.Height * 0.78);
            Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
            Font = new Font(UiFont.Reg, 9.75f);

            // barra superior: nombre y "Abrir con ..."
            var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = light ? Color.FromArgb(236, 236, 240) : Color.FromArgb(40, 40, 46) };
            float sc = DeviceDpi / 96f;
            var title = new Label { Text = Text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = fg, Font = new Font(UiFont.Semi, 10.5f) };
            int hov = -1;
            Func<PointF> lp = () => new PointF(16 * sc, title.Height / 2f - TrafficLights.D(sc) / 2);
            title.Paint += (o, e) => { var p = lp(); TrafficLights.Draw(e.Graphics, p.X, p.Y, sc, true, hov >= 0, light); };
            title.MouseMove += (o, e) => { var p = lp(); int hh = e.X < p.X + TrafficLights.Width(sc) + 6 * sc ? 1 : -1; if (hh != hov) { hov = hh; title.Invalidate(); } };
            title.MouseLeave += (o, e) => { hov = -1; title.Invalidate(); };
            title.MouseDown += (o, e) => { var p = lp(); if (e.Button == MouseButtons.Left && TrafficLights.Hit(e.Location, p.X, p.Y, sc) < 0) Borderless.BeginDrag(this); };
            title.MouseUp += (o, e) =>
            {
                var p = lp(); int i = TrafficLights.Hit(e.Location, p.X, p.Y, sc);
                if (i == 0) Close(); else if (i == 1) WindowState = FormWindowState.Minimized; else if (i == 2) Borderless.ToggleMax(this);
            };
            var open = new Button { Text = "Abrir con " + AppName(path), Dock = DockStyle.Right, Width = 220, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(10, 122, 255), Font = new Font(UiFont.Semi, 9.5f), TabStop = false };
            open.FlatAppearance.BorderSize = 0;
            open.Click += (o, e) => { FileOps.Open(file); Close(); };
            var pad = new Panel { Dock = DockStyle.Right, Width = 10, BackColor = top.BackColor };
            top.Controls.Add(title); top.Controls.Add(open); top.Controls.Add(pad);
            host = new Panel { Dock = DockStyle.Fill, BackColor = bg };
            Controls.Add(host); Controls.Add(top);
        }

        static string AppName(string path)
        {
            if (Directory.Exists(path)) return "el Explorador";
            try
            {
                var sb = new StringBuilder(512); uint n = 512;
                if (AssocQueryString(0x40, 4, Path.GetExtension(path), null, sb, ref n) == 0 && sb.Length > 0) return sb.ToString();   // ASSOCSTR_FRIENDLYAPPNAME
            }
            catch { }
            return "la app predeterminada";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Fill(); } catch (Exception ex) { Program.Log(ex); ShowThumb(); }
            Activate();
        }

        void Fill()
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (Directory.Exists(file)) { ShowFolder(); return; }
            if (ImageExt.Contains(ext)) { ShowImage(); return; }
            if (TextExt.Contains(ext) || new FileInfo(file).Length < 2 && ext == "") { ShowText(); return; }
            if (TryPreviewHandler(ext)) return;
            if (LooksLikeText()) { ShowText(); return; }
            ShowThumb();
        }

        void ShowImage()
        {
            Image img;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream()) { fs.CopyTo(ms); ms.Position = 0; img = Image.FromStream(new MemoryStream(ms.ToArray())); }
            var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = img, BackColor = bg };
            host.Controls.Add(pb);
            FormClosed += (o, e) => { pb.Image = null; img.Dispose(); };
            Text += "   " + img.Width + " x " + img.Height;
        }

        void ShowText()
        {
            string txt;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, true))
            {
                var buf = new char[512 * 1024]; int n = sr.ReadBlock(buf, 0, buf.Length);
                txt = new string(buf, 0, n);
                if (!sr.EndOfStream) txt += "\r\n\r\n... (archivo largo: se muestran los primeros 512 KB)";
            }
            var tb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = bg, ForeColor = fg,
                                       Font = new Font("Consolas", 10.5f), WordWrap = false, DetectUrls = false, TabStop = false };
            tb.Text = txt.Replace("\r\n", "\n").Replace("\n", "\r\n");
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 4, 4), BackColor = bg };
            wrap.Controls.Add(tb); host.Controls.Add(wrap);
        }

        bool LooksLikeText()
        {
            try
            {
                var b = new byte[4096]; int n;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) n = fs.Read(b, 0, b.Length);
                if (n == 0) return true;
                int bad = 0; for (int i = 0; i < n; i++) if (b[i] == 0) bad++;
                return bad == 0;
            }
            catch { return false; }
        }

        void ShowFolder()
        {
            int files = 0, dirs = 0;
            try { foreach (var p in Directory.EnumerateFileSystemEntries(file)) { if (Directory.Exists(p)) dirs++; else files++; if (files + dirs > 5000) break; } } catch { }
            ShowThumb(dirs + " carpetas, " + files + " archivos");
        }

        void ShowThumb(string extra = null)
        {
            var bmp = Shell.GetIcon(file, 256, 0) ?? Shell.GetIcon(file, 256, 4);
            string info = extra;
            if (info == null)
            {
                try { var fi = new FileInfo(file); info = FileOps.HumanSize(fi.Length) + "   ·   modificado " + fi.LastWriteTime.ToString("d MMM yyyy, HH:mm"); } catch { info = ""; }
            }
            var p = new Panel { Dock = DockStyle.Fill, BackColor = bg };
            p.Paint += (o, e) =>
            {
                var g = e.Graphics; g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                int sz = Math.Min(256, Math.Min(p.Width, p.Height - 120));
                if (bmp != null && sz > 0) g.DrawImage(bmp, (p.Width - sz) / 2, (p.Height - sz) / 2 - 50, sz, sz);
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                using (var f1 = new Font(UiFont.Semi, 14f)) using (var b1 = new SolidBrush(fg))
                    g.DrawString(Path.GetFileName(file.TrimEnd('\\')), f1, b1, new RectangleF(0, (p.Height + sz) / 2 - 30, p.Width, 30), sf);
                using (var f2 = new Font(UiFont.Reg, 10f)) using (var b2 = new SolidBrush(sub))
                    g.DrawString(info, f2, b2, new RectangleF(0, (p.Height + sz) / 2 + 4, p.Width, 24), sf);
            };
            p.Resize += (o, e) => p.Invalidate();
            host.Controls.Add(p);
            FormClosed += (o, e) => { if (bmp != null) bmp.Dispose(); };
        }

        // ---- visores de Windows (preview handlers): PDF, Word, Excel, PowerPoint, correo, fuentes... ----
        [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPreviewHandler
        {
            void SetWindow(IntPtr hwnd, ref N.RECT rect);
            void SetRect(ref N.RECT rect);
            void DoPreview();
            void Unload();
            void SetFocus();
            void QueryFocus(out IntPtr phwnd);
            [PreserveSig] uint TranslateAccelerator(IntPtr pmsg);
        }
        [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithFile { void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }
        [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithStream { void Initialize(IStream stream, uint mode); }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int AssocQueryString(int flags, int str, string assoc, string extra, StringBuilder outBuf, ref uint outLen);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attr, bool create, IntPtr template, out IStream stream);

        bool TryPreviewHandler(string ext)
        {
            if (ext == "") return false;
            var sb = new StringBuilder(64); uint n = 64;
            if (AssocQueryString(0x4, 16, ext, "{8895b1c6-b41f-4c1c-a562-0d564250836f}", sb, ref n) != 0) return false;   // ASSOCSTR_SHELLEXTENSION
            Guid clsid; if (!Guid.TryParse(sb.ToString(), out clsid)) return false;
            var t = Type.GetTypeFromCLSID(clsid);
            object o = Activator.CreateInstance(t);
            var withFile = o as IInitializeWithFile;
            var withStream = o as IInitializeWithStream;
            if (withFile != null) withFile.Initialize(file, 0);
            else if (withStream != null)
            {
                IStream st;
                if (SHCreateStreamOnFileEx(file, 0x40, 0, false, IntPtr.Zero, out st) != 0) { Marshal.ReleaseComObject(o); return false; }   // STGM_SHARE_DENY_NONE
                withStream.Initialize(st, 0);
            }
            else { Marshal.ReleaseComObject(o); return false; }
            handler = (IPreviewHandler)o;
            var r = HostRect();
            handler.SetWindow(host.Handle, ref r);
            handler.DoPreview();
            host.Resize += (s, e) => { if (handler != null) { var rr = HostRect(); try { handler.SetRect(ref rr); } catch { } } };
            return true;
        }

        N.RECT HostRect() { var r = new N.RECT(); r.right = host.ClientSize.Width; r.bottom = host.ClientSize.Height; return r; }

        protected override void WndProc(ref Message m)
        {
            if (Borderless.HitTest(this, ref m, (int)(6 * DeviceDpi / 96f))) return;
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style |= 0x20000 | 0x10000; cp.ClassStyle |= 0x20000; return cp; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; Close(); return; }
            if (e.KeyCode == Keys.Enter) { FileOps.Open(file); Close(); return; }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (handler != null) { try { handler.Unload(); } catch { } try { Marshal.ReleaseComObject(handler); } catch { } handler = null; }
            base.OnFormClosed(e);
            GC.Collect();
        }
    }
}
