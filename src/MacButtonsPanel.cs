using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Que apps llevan los botones de Mac (y el borde redondeado). Dos formas:
    //   "todas"    -> todas las ventanas, menos las que apagues
    //   "elegidas" -> ninguna, solo las que prendas
    // Se guarda en %LOCALAPPDATA%\MacDock\botones-mac.txt
    static class MacButtonRules
    {
        public class App { public string Key, Name = "", Path = ""; public bool On; }

        public static bool OnlyChosen;        // false = todas menos las apagadas
        public static bool MacBorder = true;  // esquinas redondeadas y borde gris como en Mac
        public static readonly Dictionary<string, App> Apps = new Dictionary<string, App>(StringComparer.OrdinalIgnoreCase);
        public static event Action Changed;
        static bool loaded;
        static readonly Dictionary<uint, string> pidKey = new Dictionary<uint, string>();
        static readonly Dictionary<uint, string> pidPath = new Dictionary<uint, string>();

        static string FilePath { get { return System.IO.Path.Combine(Program.DataDir, "botones-mac.txt"); } }

        public static void Load()
        {
            if (loaded) return; loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('='); if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1);
                    if (k == "modo") OnlyChosen = v.Trim() == "elegidas";
                    else if (k == "borde") MacBorder = v.Trim() == "1";
                    else if (k == "app")
                    {
                        var p = v.Split('|');
                        if (p.Length >= 2 && p[0] != "") Apps[p[0]] = new App { Key = p[0], On = p[1] == "1", Path = p.Length > 2 ? p[2] : "", Name = p.Length > 3 ? p[3] : "" };
                    }
                }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                var lines = new List<string> { "modo=" + (OnlyChosen ? "elegidas" : "todas"), "borde=" + (MacBorder ? 1 : 0) };
                foreach (var a in Apps.Values) lines.Add("app=" + a.Key + "|" + (a.On ? 1 : 0) + "|" + a.Path.Replace("|", "") + "|" + a.Name.Replace("|", ""));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception ex) { Program.Log(ex); }
            lock (pidKey) { pidKey.Clear(); pidPath.Clear(); }
            var c = Changed; if (c != null) try { c(); } catch (Exception ex) { Program.Log(ex); }
        }

        public static bool IsOn(string key)
        {
            App a; if (key != "" && Apps.TryGetValue(key, out a)) return a.On;
            return !OnlyChosen;
        }

        public static void Set(string key, bool on, string path, string name)
        {
            App a;
            if (!Apps.TryGetValue(key, out a)) { a = new App { Key = key }; Apps[key] = a; }
            a.On = on; if (path != "") a.Path = path; if (name != "") a.Name = name;
            Save();
        }

        // la app (exe) de una ventana, p. ej. "chrome", "code", "steamwebhelper"
        public static string KeyOf(IntPtr h, out string path)
        {
            uint pid; N.GetWindowThreadProcessId(h, out pid);
            lock (pidKey)
            {
                string k;
                if (pidKey.TryGetValue(pid, out k)) { path = pidPath[pid]; return k; }
                path = "";
                try { path = N.ProcessPath(pid); } catch { }
                k = path == "" ? "" : System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                pidKey[pid] = k; pidPath[pid] = path;
                return k;
            }
        }

        public static bool Allowed(IntPtr h) { Load(); string p; return IsOn(KeyOf(h, out p)); }

        static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome", "Google Chrome" }, { "brave", "Brave" }, { "msedge", "Microsoft Edge" }, { "firefox", "Firefox" }, { "opera", "Opera" },
            { "code", "Visual Studio Code" }, { "devenv", "Visual Studio" }, { "steamwebhelper", "Steam" }, { "steam", "Steam" },
            { "explorer", "Explorador de archivos" }, { "applicationframehost", "Apps de Windows (Configuración, Calculadora…)" },
            { "discord", "Discord" }, { "spotify", "Spotify" }, { "whatsapp", "WhatsApp" }, { "winword", "Word" }, { "excel", "Excel" },
            { "powerpnt", "PowerPoint" }, { "notepad", "Bloc de notas" }, { "windowsterminal", "Terminal" }, { "claude", "Claude" },
        };

        public static string NameOf(string key, string path)
        {
            string n;
            if (Known.TryGetValue(key, out n)) return n;
            try { if (path != "") { var d = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrEmpty(d) && d.Trim() != "") return d.Trim(); } } catch { }
            return key.Length > 0 ? char.ToUpper(key[0]) + key.Substring(1) : key;
        }
    }

    // Ventana "Botones de Mac": elegir en que apps salen los botones rojo/amarillo/verde y el borde de Mac.
    class MacButtonsWindow : MacWindow
    {
        class Row { public string Key, Name, Path; public bool Open; public Bitmap Icon; }

        static MacButtonsWindow current;
        static readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Func<bool> isEnabled; readonly Action<bool> setEnabled;
        readonly Timer tick = new Timer { Interval = 2000 };
        List<Row> rows = new List<Row>();
        RectangleF listArea; float listH;

        public static void Open(Func<bool> enabled, Action<bool> setOn)
        {
            try
            {
                MacButtonRules.Load();
                if (current != null && !current.IsDisposed) { current.Activate(); return; }
                current = new MacButtonsWindow(enabled, setOn);
                current.FormClosed += (o, e) => { current = null; };
                current.Show();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        MacButtonsWindow(Func<bool> enabled, Action<bool> setOn) : base("Botones de Mac", 560, 620)
        {
            isEnabled = enabled; setEnabled = setOn;
            Collect();
            tick.Tick += (o, e) => { Collect(); Invalidate(); };   // las apps que vayas abriendo aparecen solas
            tick.Start();
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { tick.Stop(); tick.Dispose(); base.OnFormClosed(e); }

        // apps abiertas ahora + las que ya tienen un ajuste guardado
        void Collect()
        {
            var map = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            var wins = new List<IntPtr>();
            try { N.EnumWindows((h, l) => { wins.Add(h); return true; }, IntPtr.Zero); } catch { }
            foreach (var h in wins)
            {
                try
                {
                    if (!MacButtons.Eligible(h)) continue;
                    string path, key = MacButtonRules.KeyOf(h, out path);
                    if (key == "" || map.ContainsKey(key)) continue;
                    map[key] = new Row { Key = key, Path = path, Open = true, Name = MacButtonRules.NameOf(key, path) };
                }
                catch { }
            }
            foreach (var a in MacButtonRules.Apps.Values)
                if (!map.ContainsKey(a.Key)) map[a.Key] = new Row { Key = a.Key, Path = a.Path, Name = a.Name != "" ? a.Name : MacButtonRules.NameOf(a.Key, a.Path) };
            var list = map.Values.OrderByDescending(r => r.Open).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var r in list) r.Icon = IconFor(r.Key, r.Path);
            rows = list;
        }

        static Bitmap IconFor(string key, string path)
        {
            Bitmap b;
            if (icons.TryGetValue(key, out b)) return b;
            b = null;
            try { if (path != "" && File.Exists(path)) using (var ic = Icon.ExtractAssociatedIcon(path)) if (ic != null) b = ic.ToBitmap(); } catch { }
            icons[key] = b;
            return b;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!listArea.Contains(e.Location)) return;
            int max = (int)Math.Max(0, listH - listArea.Height);
            scroll = Math.Max(0, Math.Min(max, scroll - Math.Sign(e.Delta) * (int)(40 * s)));
            Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            float pad = 16 * s, ip = 16 * s, rowH = 40 * s;
            var r = new RectangleF(pad, TitleH + 14 * s, ClientSize.Width - pad * 2, ClientSize.Height - TitleH - 14 * s - pad);
            bool on = isEnabled();

            // ajustes generales
            var top = new RectangleF(r.X, r.Y, r.Width, 190 * s);
            Card(g, top);
            float y = top.Y + 6 * s;
            var row1 = new RectangleF(top.X + ip, y, top.Width - ip * 2, rowH);
            using (var f = F(13f, true)) Txt(g, "Botones de Mac en las ventanas", f, Fg, row1);
            Switch(g, new RectangleF(row1.Right - 40 * s, row1.Y + (rowH - 22 * s) / 2, 40 * s, 22 * s), on, () => setEnabled(!on));
            y += rowH;
            using (var pen = new Pen(CardBorder)) g.DrawLine(pen, top.X + ip, y, top.Right - ip, y);
            var row2 = new RectangleF(top.X + ip, y, top.Width - ip * 2, rowH);
            using (var f = F(13f)) Txt(g, "Bordes redondeados estilo Mac", f, on ? Fg : Sub, row2);
            Switch(g, new RectangleF(row2.Right - 40 * s, row2.Y + (rowH - 22 * s) / 2, 40 * s, 22 * s), MacButtonRules.MacBorder,
                () => { MacButtonRules.MacBorder = !MacButtonRules.MacBorder; MacButtonRules.Save(); });
            y += rowH + 8 * s;
            using (var f = F(12f)) Txt(g, "¿En qué apps?", f, Sub, new RectangleF(top.X + ip, y, top.Width - ip * 2, 18 * s));
            y += 22 * s;
            Segments(g, new RectangleF(top.X + ip, y, top.Width - ip * 2, 30 * s), new[] { "En todas, menos las que apague", "Solo en las que yo elija" },
                MacButtonRules.OnlyChosen ? 1 : 0, i => { MacButtonRules.OnlyChosen = i == 1; MacButtonRules.Save(); });
            y += 36 * s;
            using (var f = F(11.5f)) Txt(g, MacButtonRules.OnlyChosen ? "Las apps nuevas salen sin botones hasta que las prendas." : "Las apps nuevas salen con botones; apaga las que no quieras.",
                f, Sub, new RectangleF(top.X + ip, y, top.Width - ip * 2, 18 * s));

            // lista de apps
            var lc = new RectangleF(r.X, top.Bottom + 12 * s, r.Width, r.Bottom - top.Bottom - 12 * s - 34 * s);
            Card(g, lc);
            using (var f = F(14f, true)) Txt(g, "Apps", f, Fg, new RectangleF(lc.X + ip, lc.Y + 10 * s, 200 * s, 22 * s));
            listArea = new RectangleF(lc.X + 4 * s, lc.Y + 38 * s, lc.Width - 8 * s, lc.Height - 42 * s);
            listH = rows.Count * rowH;
            int max = (int)Math.Max(0, listH - listArea.Height); if (scroll > max) scroll = max;
            if (rows.Count == 0) using (var f = F(12.5f)) Txt(g, "Abre una app (Chrome, Steam, VS Code…) y aparecerá aquí.", f, Sub, listArea, StringAlignment.Center);
            var st = g.Save(); g.SetClip(listArea);
            float ly = listArea.Y - scroll;
            using (var f = F(13f)) using (var fs = F(11.5f))
                foreach (var it in rows)
                {
                    var row = it;
                    var rr = new RectangleF(listArea.X + 12 * s, ly, listArea.Width - 24 * s, rowH); ly += rowH;
                    if (rr.Bottom < listArea.Y || rr.Y > listArea.Bottom) continue;
                    var ic = new RectangleF(rr.X, rr.Y + (rowH - 24 * s) / 2, 24 * s, 24 * s);
                    if (row.Icon != null) g.DrawImage(row.Icon, ic);
                    else using (var p = Round(ic, 6 * s)) using (var b = new SolidBrush(Soft)) g.FillPath(b, p);
                    bool allowed = MacButtonRules.IsOn(row.Key);
                    var tr = new RectangleF(ic.Right + 12 * s, rr.Y, rr.Width - 24 * s - 70 * s, rowH);
                    float nw = Math.Min(tr.Width, g.MeasureString(row.Name, f).Width + 2 * s);
                    Txt(g, row.Name, f, on ? Fg : Sub, new RectangleF(tr.X, tr.Y, nw, tr.Height));
                    if (row.Open) Txt(g, "abierta", fs, Sub, new RectangleF(tr.X + nw + 6 * s, tr.Y, 80 * s, tr.Height));
                    var sw = new RectangleF(rr.Right - 40 * s, rr.Y + (rowH - 22 * s) / 2, 40 * s, 22 * s);
                    if (sw.Y >= listArea.Y && sw.Bottom <= listArea.Bottom)
                        Switch(g, sw, allowed, () => MacButtonRules.Set(row.Key, !allowed, row.Path ?? "", row.Name ?? ""));
                    if (ly < listArea.Bottom) using (var pen = new Pen(CardBorder)) g.DrawLine(pen, tr.X, rr.Bottom, rr.Right, rr.Bottom);
                }
            g.Restore(st);

            using (var f = F(11.5f))
                Txt(g, "No salen en apps abiertas como administrador ni en juegos o videos a pantalla completa.", f, Sub, new RectangleF(r.X + 4 * s, r.Bottom - 28 * s, r.Width - 8 * s, 28 * s), StringAlignment.Center);
        }
    }
}
