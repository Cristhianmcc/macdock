using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MacDock
{
    // Apps en segundo plano (iconos de la bandeja) para dibujarlas en la barra superior, como los "menu extras" de Mac.
    // Windows no deja dibujar ni "presionar" los iconos reales de otra app, asi que la lista sale de lo que Windows recuerda
    // de cada icono (HKCU\Control Panel\NotifyIconSettings: programa, nombre e imagen) y solo se muestran las apps abiertas.
    // Una app con icono en la bandeja.
    class TrayApp
    {
        public string Name, Exe, Special; public Bitmap Icon; public List<int> Pids = new List<int>();
        public bool IsTemplate;   // icono con fondo transparente: se dibuja de un solo color, como en Mac

        // Mac dibuja los iconos de la barra de un solo color usando solo su forma (canal alfa). Los iconos sin transparencia
        // (cuadros llenos) se quedan a color, porque de un color serian un cuadro solido.
        public void PrepareGlyph(int px)
        {
            if (Icon == null) return;
            var small = new Bitmap(px, px, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(Icon, 0, 0, px, px);
            }
            int opaque = 0, total = px * px;
            for (int y = 0; y < px; y++) for (int x = 0; x < px; x++) if (small.GetPixel(x, y).A > 200) opaque++;
            IsTemplate = opaque < total * 0.62f;
            if (IsTemplate)
                for (int y = 0; y < px; y++)
                    for (int x = 0; x < px; x++) { var c = small.GetPixel(x, y); small.SetPixel(x, y, Color.FromArgb(c.A, 255, 255, 255)); }
            Icon.Dispose(); Icon = small;
        }

        // Dibuja el glifo: los de un color se tinen con el color del texto de la barra.
        public void Draw(Graphics g, RectangleF r, Color fg)
        {
            if (Icon == null) return;
            if (!IsTemplate) { g.DrawImage(Icon, r); return; }
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix(new[] {
                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, fg.A / 255f, 0 }, new float[] { fg.R / 255f, fg.G / 255f, fg.B / 255f, 0, 1 } });
                ia.SetColorMatrix(cm);
                g.DrawImage(Icon, Rectangle.Round(r), 0, 0, Icon.Width, Icon.Height, GraphicsUnit.Pixel, ia);
            }
        }

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
        public void ForceQuit()
        {
            foreach (var pid in Pids) try { using (var p = Process.GetProcessById(pid)) p.Kill(); } catch { }
        }

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
