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

namespace MacDock
{
    // ======================================================================================
    //  Monitor de Actividad (estilo macOS), enfocado en la memoria RAM.
    //  - Agrupa los procesos por app (Chrome = una fila aunque tenga 40 procesos) y suma a cada app
    //    los navegadores internos (WebView2) que usan WhatsApp, Claude, Teams, etc.
    //  - Mide la "memoria privada en uso" de cada proceso (la misma columna "Memoria" del Administrador de tareas),
    //    leyendo todos los procesos de una sola vez con NtQuerySystemInformation: sin abrir cada proceso ni pedir administrador.
    //  - Solo trabaja mientras la ventana esta abierta y visible (se actualiza cada 2 segundos).
    // ======================================================================================

    class ProcInfo
    {
        public int Pid, ParentPid, Session, Threads;
        public string Image = "", Path = "";
        public long PrivWs, Ws, CpuTime, Created;
        public double Cpu;   // 0..1 del total del equipo
    }

    class AppMem
    {
        public string Key, Name, IconPath;
        public bool IsSystem;
        public List<ProcInfo> Procs = new List<ProcInfo>();
        public long Mem { get { long t = 0; foreach (var p in Procs) t += p.PrivWs; return t; } }
        public double Cpu { get { double t = 0; foreach (var p in Procs) t += p.Cpu; return t; } }
    }

    class MemSnapshot
    {
        public List<AppMem> Apps = new List<AppMem>();
        public long Total, Available, Cached, Compressed, CommitUsed, CommitLimit, AppsMem;
        public int ProcessCount;
        public float UsedFraction { get { return Total <= 0 ? 0 : (float)(Total - Available) / Total; } }
    }

    static class MemReader
    {
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int needed);
        [StructLayout(LayoutKind.Sequential)]
        struct PERFORMANCE_INFORMATION
        {
            public int cb; public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public int HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo")] static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pi, int cb);

        static readonly Dictionary<string, long> lastCpu = new Dictionary<string, long>();   // "pid|creado" -> tiempo de CPU
        static DateTime lastAt = DateTime.MinValue;
        static readonly Dictionary<string, string> pathCache = new Dictionary<string, string>();

        // Programas "ayudantes" que trabajan para otra app: su memoria se suma a la app que los abrio.
        static readonly HashSet<string> Helpers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "msedgewebview2.exe", "crashpad_handler.exe", "conhost.exe" };

        static readonly HashSet<string> SystemImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "services.exe", "lsass.exe", "svchost.exe", "fontdrvhost.exe",
            "winlogon.exe", "dwm.exe", "LsaIso.exe", "spoolsv.exe", "SearchIndexer.exe", "MsMpEng.exe", "NisSrv.exe", "audiodg.exe",
            "WmiPrvSE.exe", "dllhost.exe", "sihost.exe", "taskhostw.exe", "ctfmon.exe", "RuntimeBroker.exe", "SecurityHealthService.exe",
            "SearchHost.exe", "StartMenuExperienceHost.exe", "ShellExperienceHost.exe", "TextInputHost.exe", "Widgets.exe", "WidgetService.exe",
            "dasHost.exe", "SgrmBroker.exe", "MoUsoCoreWorker.exe", "TiWorker.exe", "TrustedInstaller.exe", "smartscreen.exe" };

        public static MemSnapshot Read()
        {
            var snap = new MemSnapshot();
            PERFORMANCE_INFORMATION pi;
            if (GetPerformanceInfo(out pi, Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION))))
            {
                long page = (long)pi.PageSize.ToUInt64();
                snap.Total = (long)pi.PhysicalTotal.ToUInt64() * page;
                snap.Available = (long)pi.PhysicalAvailable.ToUInt64() * page;
                snap.Cached = (long)pi.SystemCache.ToUInt64() * page;
                snap.CommitUsed = (long)pi.CommitTotal.ToUInt64() * page;
                snap.CommitLimit = (long)pi.CommitLimit.ToUInt64() * page;
            }

            var procs = ReadProcesses();
            snap.ProcessCount = procs.Count;
            var now = DateTime.UtcNow;
            double elapsed = lastAt == DateTime.MinValue ? 0 : (now - lastAt).TotalMilliseconds * 10000.0 * Environment.ProcessorCount;   // en unidades de 100 ns
            var seen = new HashSet<string>();
            foreach (var p in procs)
            {
                string k = p.Pid + "|" + p.Created; seen.Add(k);
                long prev;
                if (elapsed > 0 && lastCpu.TryGetValue(k, out prev) && p.CpuTime >= prev) p.Cpu = Math.Min(1.0, (p.CpuTime - prev) / elapsed);
                lastCpu[k] = p.CpuTime;
            }
            foreach (var k in lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) lastCpu.Remove(k);
            lastAt = now;

            var byPid = new Dictionary<int, ProcInfo>();
            foreach (var p in procs) byPid[p.Pid] = p;
            var groups = new Dictionary<string, AppMem>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in procs)
            {
                if (p.Pid == 0) continue;
                if (p.Image.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase)) { snap.Compressed = p.Ws; continue; }

                // los ayudantes se cuentan en la app que los abrio (subiendo por los "padres")
                var owner = p; int guard = 0;
                while (Helpers.Contains(owner.Image) && guard++ < 10)
                {
                    ProcInfo parent;
                    if (!byPid.TryGetValue(owner.ParentPid, out parent) || parent.Created > owner.Created) break;   // el padre ya no existe (pid reciclado)
                    owner = parent;
                }

                string key, name, icon; bool sys = false;
                if (SystemImages.Contains(owner.Image) || owner.Session == 0 || owner.Path == "")
                {
                    key = "sys"; name = "Windows (sistema y servicios)"; icon = null; sys = true;
                }
                else
                {
                    key = owner.Path; icon = owner.Path; name = null;
                    string appId = Shell.AppIdOf((uint)owner.Pid);
                    if (appId != "") { icon = Shell.AppsFolderPath(appId); name = AppIndex.NameForAppId(appId); }
                    if (Helpers.Contains(owner.Image) && owner == p) name = "Navegador interno de apps (WebView2)";
                }
                AppMem g;
                if (!groups.TryGetValue(key, out g))
                {
                    g = new AppMem { Key = key, IsSystem = sys, IconPath = icon, Name = name ?? NiceName(owner.Path, owner.Image) };
                    groups[key] = g;
                }
                g.Procs.Add(p);
            }
            foreach (var g in groups.Values) g.Procs.Sort((a, b) => b.PrivWs.CompareTo(a.PrivWs));
            snap.Apps = groups.Values.OrderByDescending(g => g.Mem).ToList();
            snap.AppsMem = snap.Apps.Where(a => !a.IsSystem).Sum(a => a.Mem);
            return snap;
        }

        static readonly Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string NiceName(string path, string image)
        {
            if (path == "") return image;
            string n;
            if (nameCache.TryGetValue(path, out n)) return n;
            n = System.IO.Path.GetFileNameWithoutExtension(path);
            try { var vi = FileVersionInfo.GetVersionInfo(path); if (!string.IsNullOrWhiteSpace(vi.FileDescription)) n = vi.FileDescription.Trim(); } catch { }
            if (nameCache.Count > 500) nameCache.Clear();
            nameCache[path] = n;
            return n;
        }

        // Lee todos los procesos de una vez (SystemProcessInformation). Solo para 64 bits; si no, usa la clase Process.
        static List<ProcInfo> ReadProcesses()
        {
            var list = new List<ProcInfo>();
            if (IntPtr.Size != 8) return Fallback();
            int size = 1 << 20; IntPtr buf = IntPtr.Zero;
            try
            {
                for (int tries = 0; tries < 6; tries++)
                {
                    buf = Marshal.AllocHGlobal(size);
                    int needed, st = NtQuerySystemInformation(5, buf, size, out needed);
                    if (st == 0) break;
                    Marshal.FreeHGlobal(buf); buf = IntPtr.Zero;
                    if (st != unchecked((int)0xC0000004)) return Fallback();   // STATUS_INFO_LENGTH_MISMATCH: buffer chico
                    size = Math.Max(size * 2, needed + (1 << 16));
                }
                if (buf == IntPtr.Zero) return Fallback();
                long off = 0;
                var live = new HashSet<string>();
                while (true)
                {
                    IntPtr e = new IntPtr(buf.ToInt64() + off);
                    var p = new ProcInfo();
                    p.Threads = Marshal.ReadInt32(e, 0x04);
                    p.PrivWs = Marshal.ReadInt64(e, 0x08);
                    p.Created = Marshal.ReadInt64(e, 0x20);
                    p.CpuTime = Marshal.ReadInt64(e, 0x28) + Marshal.ReadInt64(e, 0x30);
                    int nameLen = Marshal.ReadInt16(e, 0x38) & 0xFFFF; IntPtr namePtr = Marshal.ReadIntPtr(e, 0x40);
                    p.Image = nameLen > 0 && namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr, nameLen / 2) : (p.Pid == 0 ? "Idle" : "System");
                    p.Pid = (int)Marshal.ReadIntPtr(e, 0x50).ToInt64();
                    p.ParentPid = (int)Marshal.ReadIntPtr(e, 0x58).ToInt64();
                    p.Session = Marshal.ReadInt32(e, 0x64);
                    p.Ws = Marshal.ReadIntPtr(e, 0x90).ToInt64();
                    if (p.Pid == 4) p.Image = "System";
                    // ruta del programa (para el icono y el nombre); se guarda por proceso para no pedirla cada vez
                    string pk = p.Pid + "|" + p.Created; live.Add(pk);
                    string path;
                    if (!pathCache.TryGetValue(pk, out path)) { path = p.Pid > 4 ? N.ProcessPath((uint)p.Pid) : ""; pathCache[pk] = path; }
                    p.Path = path;
                    list.Add(p);
                    int next = Marshal.ReadInt32(e, 0);
                    if (next == 0) break;
                    off += next;
                }
                foreach (var k in pathCache.Keys.Where(k => !live.Contains(k)).ToList()) pathCache.Remove(k);
            }
            catch (Exception ex) { Program.Log(ex); return Fallback(); }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
            return list;
        }

        static List<ProcInfo> Fallback()
        {
            var list = new List<ProcInfo>();
            foreach (var pr in Process.GetProcesses())
                try
                {
                    var p = new ProcInfo { Pid = pr.Id, Image = pr.ProcessName + ".exe", PrivWs = pr.WorkingSet64, Ws = pr.WorkingSet64, Session = pr.SessionId };
                    p.Path = N.ProcessPath((uint)pr.Id);
                    list.Add(p);
                }
                catch { }
                finally { pr.Dispose(); }
            return list;
        }

        public static string Size(long bytes)
        {
            double gb = bytes / 1073741824.0;
            if (gb >= 1) return gb.ToString(gb >= 10 ? "0" : "0.0") + " GB";
            return Math.Max(0, bytes / 1048576.0).ToString("0") + " MB";
        }
    }

    // ======================================================================================
    //  Ventana
    // ======================================================================================
    class ActivityMonitor : Form
    {
        static ActivityMonitor current;

        public static void Open(bool light, float s)
        {
            if (current != null && !current.IsDisposed)
            {
                if (current.WindowState == FormWindowState.Minimized) current.WindowState = FormWindowState.Normal;
                current.Activate(); return;
            }
            current = new ActivityMonitor(light, s);
            current.FormClosed += (o, e) => { current = null; GC.Collect(); };
            current.Show();
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        readonly bool light; readonly float s;
        MemSnapshot snap;
        readonly List<float> history = new List<float>();     // presion de memoria de los ultimos minutos
        readonly Timer timer = new Timer { Interval = 2000 };
        readonly TextBox search;
        readonly HashSet<string> expanded = new HashSet<string>();
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        string selKey; int scroll;
        readonly List<KeyValuePair<RectangleF, string>> rowHits = new List<KeyValuePair<RectangleF, string>>();
        RectangleF btnClose, btnKill;

        ActivityMonitor(bool lightTheme, float scale)
        {
            light = lightTheme; s = scale;
            Text = "Monitor de Actividad"; StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true; KeyPreview = true;
            ClientSize = new Size((int)(860 * s), (int)(640 * s)); MinimumSize = new Size((int)(640 * s), (int)(480 * s));
            BackColor = light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(30, 30, 33);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            search = new TextBox
            {
                BorderStyle = BorderStyle.None, Font = new Font(UiFont.Reg, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel),
                BackColor = light ? Color.White : Color.FromArgb(52, 52, 56), ForeColor = light ? Color.FromArgb(30, 30, 30) : Color.FromArgb(235, 235, 240)
            };
            search.TextChanged += (o, e) => { scroll = 0; Invalidate(); };
            Controls.Add(search);
            timer.Tick += (o, e) => Refresh2();
            MouseWheel += (o, e) => { scroll = Math.Max(0, scroll - Math.Sign(e.Delta) * (int)(34 * s) * 3); Invalidate(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);   // barra de titulo oscura
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Refresh2(); timer.Start(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (WindowState == FormWindowState.Minimized) timer.Stop(); else if (IsHandleCreated && !timer.Enabled) { timer.Start(); Refresh2(); } Invalidate(); }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); timer.Dispose();
            foreach (var b in icons.Values) if (b != null) b.Dispose();
            icons.Clear();
            base.OnFormClosed(e);
        }

        void Refresh2()
        {
            try
            {
                snap = MemReader.Read();
                history.Add(snap.UsedFraction);
                if (history.Count > 90) history.RemoveAt(0);
                foreach (var a in snap.Apps.Take(60))
                    if (a.IconPath != null && !icons.ContainsKey(a.IconPath)) icons[a.IconPath] = Shell.GetIcon(a.IconPath, (int)(22 * s));
            }
            catch (Exception ex) { Program.Log(ex); }
            Invalidate();
        }

        // ---------- colores ----------
        Color Fg { get { return light ? Color.FromArgb(28, 28, 30) : Color.FromArgb(240, 240, 245); } }
        Color Dim { get { return light ? Color.FromArgb(120, 120, 128) : Color.FromArgb(150, 150, 160); } }
        Color Card { get { return light ? Color.White : Color.FromArgb(42, 42, 46); } }
        Color Line { get { return light ? Color.FromArgb(228, 228, 232) : Color.FromArgb(58, 58, 64); } }
        static readonly Color CApps = Color.FromArgb(10, 132, 255), CSys = Color.FromArgb(255, 159, 10), CComp = Color.FromArgb(191, 90, 242), CCache = Color.FromArgb(142, 142, 147);
        static Color Level(float f) { return f < 0.75f ? Color.FromArgb(52, 199, 89) : f < 0.88f ? Color.FromArgb(255, 204, 0) : Color.FromArgb(255, 69, 58); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float pad = 16 * s, W = ClientSize.Width, H = ClientSize.Height;
            rowHits.Clear();
            using (var fTitle = new Font(UiFont.Semi, 17f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var f = new Font(UiFont.Reg, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fs = new Font(UiFont.Reg, 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fb = new Font(UiFont.Semi, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                // encabezado y buscador
                Txt(g, "Memoria", fTitle, Fg, pad, pad - 2 * s);
                var sr = new RectangleF(W - pad - 220 * s, pad - 4 * s, 220 * s, 28 * s);
                using (var p = Round(sr, 8 * s)) using (var b = new SolidBrush(search.BackColor)) g.FillPath(b, p);
                using (var pen = new Pen(Dim, 1.5f * s))   // lupa
                {
                    float mx = sr.X + 13 * s, my = sr.Y + 13 * s, mr = 4.5f * s;
                    g.DrawEllipse(pen, mx - mr, my - mr, mr * 2, mr * 2);
                    g.DrawLine(pen, mx + mr * 0.7f, my + mr * 0.7f, mx + mr * 1.6f, my + mr * 1.6f);
                }
                search.SetBounds((int)(sr.X + 26 * s), (int)(sr.Y + 6 * s), (int)(sr.Width - 34 * s), (int)(18 * s));
                if (search.Text == "" && !search.Focused) Txt(g, "Buscar app", f, Dim, sr.X + 26 * s, sr.Y + 5 * s);

                if (snap == null) { Txt(g, "Leyendo memoria...", f, Dim, pad, 80 * s); return; }

                // ---------- tarjeta de resumen ----------
                float cy = pad + 36 * s, ch = 168 * s;
                var card = new RectangleF(pad, cy, W - pad * 2, ch);
                using (var p = Round(card, 12 * s)) { using (var b = new SolidBrush(Card)) g.FillPath(b, p); using (var pen = new Pen(Line)) g.DrawPath(pen, p); }

                long used = snap.Total - snap.Available;
                long sys = Math.Max(0, used - snap.AppsMem - snap.Compressed);
                float frac = snap.UsedFraction;
                // barra apilada: apps / Windows / comprimida / libre
                var bar = new RectangleF(card.X + pad, card.Y + pad + 22 * s, card.Width * 0.58f - pad, 16 * s);
                Txt(g, MemReader.Size(used) + " en uso de " + MemReader.Size(snap.Total), fb, Fg, bar.X, card.Y + pad - 2 * s);
                using (var p = Round(bar, 5 * s)) using (var b = new SolidBrush(light ? Color.FromArgb(232, 232, 236) : Color.FromArgb(60, 60, 66))) g.FillPath(b, p);
                var state = g.Save();
                using (var clip = Round(bar, 5 * s)) g.SetClip(clip);
                float x = bar.X;
                foreach (var seg in new[] { new KeyValuePair<long, Color>(snap.AppsMem, CApps), new KeyValuePair<long, Color>(sys, CSys), new KeyValuePair<long, Color>(snap.Compressed, CComp) })
                {
                    float w = snap.Total > 0 ? bar.Width * seg.Key / (float)snap.Total : 0;
                    using (var b = new SolidBrush(seg.Value)) g.FillRectangle(b, x, bar.Y, w, bar.Height);
                    x += w;
                }
                g.Restore(state);
                // leyenda
                float ly = bar.Bottom + 12 * s, lx = bar.X;
                lx = Legend(g, fs, CApps, "Tus apps", MemReader.Size(snap.AppsMem), lx, ly);
                lx = Legend(g, fs, CSys, "Windows y caché", MemReader.Size(sys), lx, ly);
                lx = Legend(g, fs, CComp, "Comprimida", MemReader.Size(snap.Compressed), lx, ly);
                Legend(g, fs, light ? Color.FromArgb(205, 205, 210) : Color.FromArgb(80, 80, 88), "Libre", MemReader.Size(snap.Available), lx, ly);
                // frase sencilla
                string verdict = frac < 0.75f ? "Todo bien: queda memoria libre de sobra."
                               : frac < 0.88f ? "Va justa: cerrar alguna app que no uses ayudaría."
                               : "Muy llena: Windows está usando el disco como memoria y todo se siente lento.";
                var top = snap.Apps.FirstOrDefault(a => !a.IsSystem);
                // "Windows y cache" de arriba incluye lo que Windows guarda en memoria para abrir todo mas rapido (y suelta cuando hace falta);
                // en la lista, "Windows (sistema y servicios)" son solo sus programas.
                string tip = top == null ? "" : top.Name + " es la que más usa (" + MemReader.Size(top.Mem) + (top.Procs.Count > 1 ? ", en " + top.Procs.Count + " procesos" : "") + ")." + (IsBrowser(top) ? " Cada pestaña abierta suma." : "");
                using (var b = new SolidBrush(Level(frac))) g.FillEllipse(b, bar.X, ly + 30 * s, 9 * s, 9 * s);
                Txt(g, verdict, f, Fg, bar.X + 15 * s, ly + 25 * s);
                Txt(g, tip, fs, Dim, bar.X + 15 * s, ly + 45 * s, bar.Width + 60 * s);
                Txt(g, "\"Windows y caché\" incluye lo que Windows guarda para ir más rápido; lo suelta solo si una app lo necesita.", fs, Dim, bar.X + 15 * s, ly + 63 * s, bar.Width + 60 * s);

                // grafica de presion de memoria
                var gr = new RectangleF(card.X + card.Width * 0.62f, card.Y + pad + 18 * s, card.Width * 0.38f - pad, ch - pad * 2 - 18 * s);
                Txt(g, "Presión de memoria", fs, Dim, gr.X, card.Y + pad - 2 * s);
                using (var p = Round(gr, 6 * s)) using (var b = new SolidBrush(light ? Color.FromArgb(244, 244, 247) : Color.FromArgb(34, 34, 38))) g.FillPath(b, p);
                if (history.Count > 1)
                {
                    float step = gr.Width / 89f;
                    for (int i = 0; i < history.Count; i++)
                    {
                        float hx = gr.Right - (history.Count - 1 - i) * step - step, hh = gr.Height * history[i];
                        using (var b = new SolidBrush(Level(history[i]))) g.FillRectangle(b, hx, gr.Bottom - hh, Math.Max(1f, step - 0.5f), hh);
                    }
                }

                // ---------- lista de apps ----------
                float ty = card.Bottom + 14 * s;
                float colMem = W - pad - 110 * s, colProc = colMem - 90 * s, colCpu = colProc - 80 * s;
                Txt(g, "App", fs, Dim, pad + 40 * s, ty);
                TxtR(g, "CPU", fs, Dim, colCpu + 60 * s, ty);
                TxtR(g, "Procesos", fs, Dim, colProc + 70 * s, ty);
                TxtR(g, "Memoria", fs, Dim, colMem + 100 * s, ty);
                float listTop = ty + 22 * s, listBottom = H - 56 * s;
                using (var pen = new Pen(Line)) g.DrawLine(pen, pad, listTop - 3 * s, W - pad, listTop - 3 * s);
                g.SetClip(new RectangleF(0, listTop, W, listBottom - listTop));
                string q = search.Text.Trim();
                long maxMem = Math.Max(1, snap.Apps.Count > 0 ? snap.Apps[0].Mem : 1);
                float y = listTop - scroll, rowH = 34 * s, subH = 25 * s;
                foreach (var a in snap.Apps)
                {
                    if (q != "" && a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0 && !a.Procs.Any(p => p.Image.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    var row = new RectangleF(pad, y, W - pad * 2, rowH);
                    if (row.Bottom > listTop && row.Top < listBottom)
                    {
                        if (a.Key == selKey) using (var p = Round(row, 7 * s)) using (var b = new SolidBrush(Color.FromArgb(light ? 40 : 70, 10, 132, 255))) g.FillPath(b, p);
                        // barrita proporcional al uso, detras del texto
                        float bw = 104 * s * a.Mem / maxMem;   // solo dentro de la columna de Memoria
                        using (var b = new SolidBrush(Color.FromArgb(light ? 26 : 36, a.IsSystem ? CSys : CApps))) g.FillRectangle(b, colMem + 100 * s - bw, y + 6 * s, bw, rowH - 12 * s);
                        Txt(g, expanded.Contains(a.Key) ? "▾" : "▸", fs, Dim, pad + 6 * s, y + 9 * s);
                        Bitmap ic = null; if (a.IconPath != null) icons.TryGetValue(a.IconPath, out ic);
                        var ir = new RectangleF(pad + 20 * s, y + 6 * s, 22 * s, 22 * s);
                        if (ic != null) g.DrawImage(ic, ir);
                        else using (var p = Round(ir, 5 * s)) using (var b = new SolidBrush(a.IsSystem ? CSys : CCache)) g.FillPath(b, p);
                        Txt(g, a.Name, f, Fg, pad + 50 * s, y + 8 * s, colCpu - pad - 60 * s);
                        TxtR(g, (a.Cpu * 100).ToString("0.0") + " %", fs, Dim, colCpu + 60 * s, y + 10 * s);
                        TxtR(g, a.Procs.Count.ToString(), fs, Dim, colProc + 70 * s, y + 10 * s);
                        TxtR(g, MemReader.Size(a.Mem), fb, Fg, colMem + 100 * s, y + 8 * s);
                    }
                    rowHits.Add(new KeyValuePair<RectangleF, string>(row, a.Key));
                    y += rowH;
                    if (expanded.Contains(a.Key))
                        foreach (var p in a.Procs.Take(80))
                        {
                            if (y + subH > listTop && y < listBottom)
                            {
                                Txt(g, p.Image + "   ·   PID " + p.Pid, fs, Dim, pad + 50 * s, y + 5 * s, colCpu - pad - 60 * s);
                                TxtR(g, (p.Cpu * 100).ToString("0.0") + " %", fs, Dim, colCpu + 60 * s, y + 5 * s);
                                TxtR(g, MemReader.Size(p.PrivWs), fs, Dim, colMem + 100 * s, y + 5 * s);
                            }
                            y += subH;
                        }
                }
                int maxScroll = (int)Math.Max(0, (y + scroll) - listBottom + 8 * s);
                if (scroll > maxScroll) { scroll = maxScroll; }
                g.ResetClip();

                // ---------- pie: botones ----------
                using (var pen = new Pen(Line)) g.DrawLine(pen, 0, listBottom + 4 * s, W, listBottom + 4 * s);
                var sel = snap.Apps.FirstOrDefault(a => a.Key == selKey);
                bool canAct = sel != null && !sel.IsSystem;
                btnKill = new RectangleF(W - pad - 130 * s, listBottom + 14 * s, 130 * s, 28 * s);
                btnClose = new RectangleF(btnKill.X - 10 * s - 110 * s, btnKill.Y, 110 * s, 28 * s);
                Button(g, btnClose, "Cerrar app", fb, canAct, false);
                Button(g, btnKill, "Forzar cierre", fb, canAct, true);
                Txt(g, canAct ? "Elegida: " + sel.Name : (sel != null ? "Los procesos de Windows no se cierran desde aquí." : "Haz clic en una app para ver sus procesos o cerrarla."), fs, Dim, pad, listBottom + 20 * s, btnClose.X - pad * 2);
            }
        }

        static bool IsBrowser(AppMem a)
        {
            string k = (a.Key ?? "").ToLowerInvariant();
            return k.EndsWith("chrome.exe") || k.EndsWith("msedge.exe") || k.EndsWith("brave.exe") || k.EndsWith("firefox.exe") || k.EndsWith("opera.exe") || k.EndsWith("vivaldi.exe");
        }

        float Legend(Graphics g, Font f, Color c, string label, string val, float x, float y)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x, y + 4 * s, 8 * s, 8 * s);
            string t = label + "  " + val;
            Txt(g, t, f, Fg, x + 12 * s, y);
            return x + 12 * s + g.MeasureString(t, f).Width + 14 * s;
        }

        void Button(Graphics g, RectangleF r, string t, Font f, bool on, bool danger)
        {
            Color bg = !on ? (light ? Color.FromArgb(236, 236, 240) : Color.FromArgb(48, 48, 52)) : danger ? Color.FromArgb(255, 69, 58) : (light ? Color.White : Color.FromArgb(64, 64, 70));
            Color fg = !on ? Dim : danger ? Color.White : Fg;
            using (var p = Round(r, 7 * s)) { using (var b = new SolidBrush(bg)) g.FillPath(b, p); using (var pen = new Pen(Line)) g.DrawPath(pen, p); }
            using (var b = new SolidBrush(fg)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(t, f, b, r, sf);
        }

        void Txt(Graphics g, string t, Font f, Color c, float x, float y, float maxW = 0)
        {
            using (var b = new SolidBrush(c))
            {
                if (maxW <= 0) g.DrawString(t, f, b, x, y);
                else using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }) g.DrawString(t, f, b, new RectangleF(x, y, maxW, f.Height * 1.4f), sf);
            }
        }

        void TxtR(Graphics g, string t, Font f, Color c, float right, float y)
        {
            using (var b = new SolidBrush(c)) using (var sf = new StringFormat { Alignment = StringAlignment.Far }) g.DrawString(t, f, b, new RectangleF(right - 200 * s, y, 200 * s, f.Height * 1.4f), sf);
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Max(0.1f, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        // ---------- clics ----------
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (snap == null) return;
            var sel = snap.Apps.FirstOrDefault(a => a.Key == selKey);
            if (sel != null && !sel.IsSystem)
            {
                if (btnClose.Contains(e.Location)) { CloseApp(sel, false); return; }
                if (btnKill.Contains(e.Location)) { CloseApp(sel, true); return; }
            }
            foreach (var h in rowHits)
                if (h.Key.Contains(e.Location))
                {
                    if (selKey == h.Value || e.X < h.Key.X + 20 * s) { if (!expanded.Remove(h.Value)) expanded.Add(h.Value); }
                    selKey = h.Value; Invalidate(); return;
                }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            foreach (var h in rowHits) if (h.Key.Contains(e.Location)) { if (!expanded.Remove(h.Value)) expanded.Add(h.Value); Invalidate(); return; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { if (search.Text != "") search.Text = ""; else Close(); }
            else if (e.KeyCode == Keys.F && e.Control) { search.Focus(); e.SuppressKeyPress = true; }
        }

        // Cerrar = pedir a sus ventanas que se cierren (como el boton X, la app puede preguntar si guardar).
        // Forzar = terminar sus procesos al instante (se pierde lo no guardado): siempre pide confirmacion.
        void CloseApp(AppMem a, bool force)
        {
            if (force)
            {
                var r = MessageBox.Show(this, "¿Forzar el cierre de " + a.Name + "?\n\nSe cerrará de inmediato y se perderá lo que no hayas guardado.", "Monitor de Actividad", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
                foreach (var p in a.Procs)
                    try { using (var pr = Process.GetProcessById(p.Pid)) pr.Kill(); } catch { }
            }
            else
            {
                var pids = new HashSet<int>(a.Procs.Select(p => p.Pid));
                N.EnumWindows((h, l) =>
                {
                    uint pid; N.GetWindowThreadProcessId(h, out pid);
                    if (pids.Contains((int)pid) && N.IsWindowVisible(h)) N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            }
            var once = new Timer { Interval = 800 };
            once.Tick += (o, e) => { once.Stop(); once.Dispose(); if (!IsDisposed) Refresh2(); };
            once.Start();
        }
    }
}
