using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MacDock
{
    // Launchpad estilo Mac: todas tus apps en una cuadricula a pantalla completa, con paginas y buscador.
    // Se abre con el icono del dock o con Alt + Shift + Espacio. Escribe para buscar, Enter abre, Esc cierra.
    // Solo usa memoria mientras esta abierto: al cerrarse libera el fondo y los iconos.
    class Launchpad : Form
    {
        public static Launchpad Current;

        public static void Toggle(float s)
        {
            if (Current != null && !Current.IsDisposed) { Current.FadeClose(); return; }
            Current = new Launchpad(s);
            Current.FormClosed += (o, e) => { Current = null; GC.Collect(); };
            Current.Show();
            Current.Activate();
            N.SetForegroundWindow(Current.Handle);
        }

        readonly float s;
        readonly Rectangle scr;
        Bitmap bg;                                         // captura de la pantalla desenfocada y oscurecida
        List<AppEntry> allApps = new List<AppEntry>(), view = new List<AppEntry>();
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);   // por ruta de la app
        readonly object iconLock = new object();
        string query = "";
        int page, sel = -1, cols, rows;
        float pos;                                         // pagina que se ve (con decimales mientras se anima)
        float appear;                                      // 0 -> 1 al abrir, 1 -> 0 al cerrar
        bool closing, iconWorkerRunning;
        volatile bool closed;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 15 };
        Point downPt; bool down, dragged; float dragPx;
        DateTime wheelAt = DateTime.MinValue; int wheelAcc;

        int PerPage { get { return cols * rows; } }
        int Pages { get { return Math.Max(1, (view.Count + PerPage - 1) / PerPage); } }

        Launchpad(float scale)
        {
            s = scale;
            scr = Screen.PrimaryScreen.Bounds;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual; Bounds = scr; DoubleBuffered = true; BackColor = Color.Black;
            Opacity = 0;
            bg = CaptureBlurred();
            ComputeLayout();
            var cur = AppIndex.Current;
            if (cur != null) SetApps(cur);
            anim.Tick += (o, e) => Step();
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }   // sin boton en Alt+Tab
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            anim.Start();
            // si la lista tiene mas de 10 minutos se refresca en segundo plano (apps recien instaladas)
            AppIndex.EnsureAsync(() => { try { BeginInvoke(new Action(() => { var list = AppIndex.Current; if (list != null && !IsDisposed && !closed) SetApps(list); })); } catch { } });
        }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); FadeClose(); }

        // ---------- fondo ----------
        Bitmap CaptureBlurred()
        {
            var full = new Bitmap(scr.Width, scr.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using (var shot = new Bitmap(scr.Width, scr.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(scr.Location, Point.Empty, scr.Size);
                    // desenfoque barato: reducir mucho y volver a agrandar con interpolacion suave
                    int tw = Math.Max(8, scr.Width / 28), th = Math.Max(8, scr.Height / 28);
                    using (var tiny = new Bitmap(tw, th, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(tiny)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(shot, 0, 0, tw, th); }
                        using (var g = Graphics.FromImage(full))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            using (var ia = new ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(tiny, new Rectangle(0, 0, scr.Width, scr.Height), 0, 0, tw, th, GraphicsUnit.Pixel, ia); }
                            using (var dark = new SolidBrush(Color.FromArgb(105, 10, 10, 14))) g.FillRectangle(dark, 0, 0, scr.Width, scr.Height);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                using (var g = Graphics.FromImage(full)) g.Clear(Color.FromArgb(40, 40, 46));
            }
            return full;
        }

        // ---------- distribucion ----------
        RectangleF grid;
        float cellW, cellH, iconPx;

        void ComputeLayout()
        {
            float top = 118 * s, bottom = 96 * s, side = scr.Width * 0.09f;
            grid = new RectangleF(side, top, scr.Width - side * 2, scr.Height - top - bottom);
            cols = Math.Max(4, Math.Min(7, (int)(grid.Width / (150 * s))));
            rows = Math.Max(3, Math.Min(5, (int)(grid.Height / (140 * s))));
            cellW = grid.Width / cols; cellH = grid.Height / rows;
            iconPx = (float)Math.Floor(Math.Min(Math.Min(cellW * 0.6f, cellH * 0.62f), 112 * s));
        }

        // Lista nueva de apps: se conserva la busqueda, la pagina y la seleccion.
        void SetApps(List<AppEntry> list)
        {
            string keep = sel >= 0 && sel < view.Count ? view[sel].Path : null;
            int keepPage = page;
            allApps = list.ToList();
            ApplyFilter();
            if (keep != null) sel = view.FindIndex(a => string.Equals(a.Path, keep, StringComparison.OrdinalIgnoreCase));
            page = Math.Min(keepPage, Pages - 1); pos = page;
            StartIconWorker();
            Invalidate();
        }

        void ApplyFilter()
        {
            string q = query.Trim();
            if (q == "") view = allApps;
            else view = allApps.Select(a => new { a, sc = Spotlight.Score(a.Name, q) }).Where(x => x.sc > 0)
                               .OrderByDescending(x => x.sc).ThenBy(x => x.a.Name.Length).Select(x => x.a).ToList();
            sel = q == "" ? -1 : (view.Count > 0 ? 0 : -1);
            page = 0; pos = 0;
            StartIconWorker();
            Invalidate();
        }

        RectangleF CellRect(int idx)
        {
            int p = idx / PerPage, k = idx % PerPage;
            float x = grid.X + (p - pos) * scr.Width + (k % cols) * cellW + dragPx;
            float y = grid.Y + (k / cols) * cellH;
            return new RectangleF(x, y, cellW, cellH);
        }

        RectangleF IconRect(int idx)
        {
            var c = CellRect(idx);
            return new RectangleF(c.X + (c.Width - iconPx) / 2f, c.Y + (c.Height - iconPx - 26 * s) / 2f, iconPx, iconPx);
        }

        int HitIcon(Point p)
        {
            int first = page * PerPage;
            for (int i = first; i < Math.Min(view.Count, first + PerPage); i++)
            {
                var r = IconRect(i); r.Inflate(10 * s, 8 * s); r.Height += 22 * s;
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        // ---------- iconos en segundo plano (primero los de la pagina visible) ----------
        void StartIconWorker()
        {
            lock (iconLock) { if (iconWorkerRunning) return; iconWorkerRunning = true; }
            int px = (int)iconPx;
            var th = new Thread(() =>
            {
                int sinceRefresh = 0;
                while (true)
                {
                    AppEntry next = null;
                    try { Invoke(new Action(() => next = NextIconToLoad())); } catch { break; }
                    if (next == null || closed) break;
                    var bmp = Shell.GetIcon(next.Path, px, 4) ?? Shell.GetIcon(next.Path, px, 0);
                    bool keep = false;
                    lock (iconLock) { if (!closed && !icons.ContainsKey(next.Path)) { icons[next.Path] = bmp; keep = true; } }
                    if (!keep && bmp != null) bmp.Dispose();
                    if (++sinceRefresh >= 6) { sinceRefresh = 0; try { BeginInvoke(new Action(Invalidate)); } catch { break; } }
                }
                lock (iconLock) iconWorkerRunning = false;
                try { BeginInvoke(new Action(Invalidate)); } catch { }
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }

        AppEntry NextIconToLoad()
        {
            if (closed) return null;
            lock (iconLock)
            {
                int first = page * PerPage;
                for (int i = first; i < Math.Min(view.Count, first + PerPage); i++) if (!icons.ContainsKey(view[i].Path)) return view[i];
                foreach (var a in view) if (!icons.ContainsKey(a.Path)) return a;
            }
            return null;
        }

        // ---------- animacion ----------
        void Step()
        {
            bool busy = false;
            float targetAppear = closing ? 0f : 1f;
            if (Math.Abs(appear - targetAppear) > 0.001f)
            {
                appear += (targetAppear - appear) * (closing ? 0.45f : 0.3f);
                if (Math.Abs(appear - targetAppear) < 0.02f) appear = targetAppear;
                Opacity = appear;
                busy = true;
            }
            if (closing && appear <= 0f) { anim.Stop(); Close(); return; }
            if (!down && Math.Abs(pos - page) > 0.0005f)
            {
                pos += (page - pos) * 0.24f;
                if (Math.Abs(pos - page) < 0.002f) pos = page;
                busy = true;
            }
            if (busy) Invalidate(); else anim.Stop();
        }

        void Kick() { if (!anim.Enabled) anim.Start(); }

        void GoPage(int p)
        {
            p = Math.Max(0, Math.Min(Pages - 1, p));
            if (p == page) return;
            page = p; Kick(); StartIconWorker();
        }

        public void FadeClose()
        {
            if (closing || IsDisposed) return;
            closing = true; Kick();
        }

        void Launch(int idx)
        {
            if (idx < 0 || idx >= view.Count) return;
            var a = view[idx];
            AppIndex.Launch(a);
            FadeClose();
        }

        // ---------- teclado ----------
        protected override bool IsInputKey(Keys k)
        {
            var key = k & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    if (query != "") { query = ""; ApplyFilter(); } else FadeClose();
                    break;
                case Keys.Enter: Launch(sel >= 0 ? sel : (query != "" ? 0 : -1)); break;
                case Keys.Left: if (e.Control) GoPage(page - 1); else MoveSel(-1); break;
                case Keys.Right: if (e.Control) GoPage(page + 1); else MoveSel(1); break;
                case Keys.Up: MoveSel(-cols); break;
                case Keys.Down: MoveSel(cols); break;
                case Keys.PageUp: GoPage(page - 1); break;
                case Keys.PageDown: GoPage(page + 1); break;
                case Keys.Home: if (query == "") GoPage(0); break;
                case Keys.End: if (query == "") GoPage(Pages - 1); break;
                case Keys.Back:
                    if (query.Length > 0) { query = e.Control ? "" : query.Substring(0, query.Length - 1); ApplyFilter(); }
                    break;
                case Keys.V:
                    if (e.Control) try { if (Clipboard.ContainsText()) { query += Clipboard.GetText().Replace("\r", " ").Replace("\n", " "); ApplyFilter(); } } catch { }
                    break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (e.KeyChar < ' ' || char.IsControl(e.KeyChar)) return;
            if (query.Length >= 60) return;
            query += e.KeyChar; ApplyFilter(); e.Handled = true;
        }

        void MoveSel(int d)
        {
            if (view.Count == 0) return;
            if (sel < 0) sel = Math.Min(view.Count - 1, page * PerPage);
            else sel = Math.Max(0, Math.Min(view.Count - 1, sel + d));
            GoPage(sel / PerPage);
            Invalidate();
        }

        // ---------- mouse: clic, arrastrar para cambiar de pagina, rueda ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            down = true; dragged = false; downPt = e.Location; dragPx = 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!down) return;
            float dx = e.X - downPt.X;
            if (!dragged && Math.Abs(dx) > 12 * s) dragged = true;
            if (dragged)
            {
                // resistencia al llegar a la primera o la ultima pagina
                if ((page == 0 && dx > 0) || (page == Pages - 1 && dx < 0)) dx *= 0.3f;
                dragPx = dx; Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { FadeClose(); return; }
            if (!down) return;
            down = false;
            if (dragged)
            {
                float dx = dragPx;
                // dejar la pagina donde quedo visualmente y animar desde ahi
                pos = page - dragPx / scr.Width; dragPx = 0;
                if (dx < -scr.Width * 0.12f) GoPage(page + 1);
                else if (dx > scr.Width * 0.12f) GoPage(page - 1);
                Kick();
                return;
            }
            int dot = HitDot(e.Location);
            if (dot >= 0) { GoPage(dot); return; }
            int i = HitIcon(e.Location);
            if (i >= 0) Launch(i); else FadeClose();   // clic en un espacio vacio cierra, como en Mac
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            WheelPage(-e.Delta);
        }

        void WheelPage(int delta)
        {
            // los trackpads mandan muchos pasos pequenos: se acumulan y se cambia una pagina a la vez
            if ((DateTime.Now - wheelAt).TotalMilliseconds < 320) { wheelAcc = 0; return; }
            wheelAcc += delta;
            if (Math.Abs(wheelAcc) >= 100)
            {
                GoPage(page + Math.Sign(wheelAcc));
                wheelAcc = 0; wheelAt = DateTime.Now;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020E)   // rueda horizontal (deslizar en el trackpad)
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                WheelPage(delta);
                m.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref m);
        }

        RectangleF DotRect(int p)
        {
            float d = 8 * s, gap = 10 * s;
            float total = Pages * d + (Pages - 1) * gap;
            float x = (scr.Width - total) / 2f + p * (d + gap);
            return new RectangleF(x, scr.Height - 58 * s, d, d);
        }

        int HitDot(Point p)
        {
            if (Pages <= 1) return -1;
            for (int i = 0; i < Pages; i++) { var r = DotRect(i); r.Inflate(6 * s, 8 * s); if (r.Contains(p)) return i; }
            return -1;
        }

        // ---------- dibujo ----------
        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            if (bg != null) g.DrawImageUnscaled(bg, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            DrawSearch(g);

            // al abrir, los iconos llegan desde un poco mas grandes (como en Mac)
            float zoom = 1f + 0.08f * (1f - appear);
            var state = g.Save();
            if (zoom != 1f)
            {
                g.TranslateTransform(scr.Width / 2f, scr.Height / 2f);
                g.ScaleTransform(zoom, zoom);
                g.TranslateTransform(-scr.Width / 2f, -scr.Height / 2f);
            }

            float visible = pos - dragPx / scr.Width;
            int pFrom = Math.Max(0, (int)Math.Floor(visible)), pTo = Math.Min(Pages - 1, (int)Math.Ceiling(visible));
            using (var font = new Font(UiFont.Reg, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            using (var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                for (int p = pFrom; p <= pTo; p++)
                    for (int i = p * PerPage; i < Math.Min(view.Count, (p + 1) * PerPage); i++)
                    {
                        var r = IconRect(i);
                        if (r.Right < -cellW || r.Left > scr.Width + cellW) continue;
                        if (i == sel)
                        {
                            var hr = RectangleF.Inflate(r, 12 * s, 10 * s); hr.Height += 24 * s;
                            using (var path = Round(hr, 16 * s)) using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillPath(b, path);
                        }
                        Bitmap ic; lock (iconLock) icons.TryGetValue(view[i].Path, out ic);
                        if (ic != null) g.DrawImage(ic, r);
                        else DrawPlaceholder(g, r, view[i].Name);
                        var lr = new RectangleF(r.X - (cellW - iconPx) / 2f + 4 * s, r.Bottom + 8 * s, cellW - 8 * s, 18 * s);
                        g.DrawString(view[i].Name, font, shadow, new RectangleF(lr.X, lr.Y + 1 * s, lr.Width, lr.Height), sf);
                        g.DrawString(view[i].Name, font, white, lr, sf);
                    }
            }
            g.Restore(state);

            if (view.Count == 0)
                using (var f = new Font(UiFont.Reg, 20f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(allApps.Count == 0 ? "Cargando apps..." : "Sin resultados", f, b, new RectangleF(0, scr.Height * 0.42f, scr.Width, 40 * s), sf);

            if (Pages > 1)
                for (int p = 0; p < Pages; p++)
                    using (var b = new SolidBrush(p == page ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(90, 255, 255, 255)))
                        g.FillEllipse(b, DotRect(p));
        }

        void DrawSearch(Graphics g)
        {
            float w = 260 * s, h = 32 * s;
            var r = new RectangleF((scr.Width - w) / 2f, 44 * s, w, h);
            using (var p = Round(r, h / 2f))
            {
                using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255), Math.Max(1f, s * 0.8f))) g.DrawPath(pen, p);
            }
            using (var f = new Font(UiFont.Reg, 15f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                string text = query == "" ? "Buscar" : query;
                var size = g.MeasureString(text, f);
                float textX = query == "" ? r.X + (r.Width - size.Width) / 2f + 10 * s : r.X + 34 * s;
                // lupa
                float cx = query == "" ? textX - 14 * s : r.X + 18 * s, cy = r.Y + h / 2f - 1 * s, rad = 5.5f * s;
                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.7f * s))
                {
                    g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                    g.DrawLine(pen, cx + rad * 0.7f, cy + rad * 0.7f, cx + rad * 1.5f, cy + rad * 1.5f);
                }
                using (var b = new SolidBrush(query == "" ? Color.FromArgb(170, 255, 255, 255) : Color.White))
                {
                    var tr = new RectangleF(textX, r.Y + (h - size.Height) / 2f, r.Right - textX - 12 * s, size.Height);
                    g.DrawString(text, f, b, tr, new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                    if (query != "")
                    {
                        float caret = Math.Min(textX + size.Width - 3 * s, r.Right - 14 * s);
                        using (var pen = new Pen(Color.White, 1.5f * s)) g.DrawLine(pen, caret, r.Y + 8 * s, caret, r.Bottom - 8 * s);
                    }
                }
            }
        }

        void DrawPlaceholder(Graphics g, RectangleF r, string name)
        {
            using (var p = Round(r, r.Width * 0.225f))
            using (var b = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) g.FillPath(b, p);
            using (var f = new Font(UiFont.Semi, r.Width * 0.42f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", f, Brushes.White, r, sf);
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
            closed = true;
            anim.Stop(); anim.Dispose();
            if (bg != null) { bg.Dispose(); bg = null; }
            lock (iconLock) { foreach (var b in icons.Values) if (b != null) b.Dispose(); icons.Clear(); }
            base.OnFormClosed(e);
        }
    }
}
