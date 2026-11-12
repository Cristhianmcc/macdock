using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace MacDock
{
    // Una app instalada. Path es lo que se abre: un acceso directo (.lnk/.url) o "shell:AppsFolder\<id>" (apps de la Tienda y del menu Inicio).
    class AppEntry
    {
        public string Name, Path;
        public Bitmap SmallIcon;   // 48 px, lo reutiliza Spotlight entre busquedas
    }

    // Lista de apps compartida por Spotlight y Launchpad.
    // Primero usa la carpeta "Aplicaciones" de Windows (la misma que muestra el menu Inicio, incluye las apps de la Tienda);
    // si no esta disponible, recorre las carpetas del menu Inicio como antes.
    static class AppIndex
    {
        static List<AppEntry> apps;
        static DateTime builtAt;
        static bool building;
        static readonly object Lock = new object();
        static readonly List<Action> waiting = new List<Action>();

        // Devuelve la lista actual (puede estar vacia la primera vez); null si nunca se ha cargado.
        public static List<AppEntry> Current { get { lock (Lock) return apps; } }

        // Carga o refresca la lista en segundo plano. onReady se llama (en otro hilo) cuando hay datos nuevos.
        public static void EnsureAsync(Action onReady)
        {
            lock (Lock)
            {
                bool fresh = apps != null && (DateTime.Now - builtAt).TotalMinutes < 10;
                if (fresh) { if (onReady != null) ThreadPool.QueueUserWorkItem(_ => onReady()); return; }
                if (onReady != null) waiting.Add(onReady);
                if (building) return;
                building = true;
            }
            var th = new Thread(() =>
            {
                List<AppEntry> list = null;
                try { list = FromAppsFolder(); } catch (Exception ex) { Program.Log(ex); }
                if (list == null || list.Count < 5) list = FromShortcuts(true);
                else
                {
                    // ademas: accesos directos sueltos del Escritorio y del dock que no estan en el menu Inicio
                    var names = new HashSet<string>(list.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
                    list.AddRange(FromShortcuts(false).Where(a => names.Add(a.Name)));
                }
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

                List<Action> callbacks;
                lock (Lock)
                {
                    // conserva los iconos pequenos ya cargados para no volver a pedirlos
                    if (apps != null)
                    {
                        var old = apps.Where(a => a.SmallIcon != null).GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().SmallIcon, StringComparer.OrdinalIgnoreCase);
                        foreach (var a in list) { Bitmap b; if (old.TryGetValue(a.Path, out b)) a.SmallIcon = b; }
                    }
                    apps = list; builtAt = DateTime.Now; building = false;
                    callbacks = waiting.ToList(); waiting.Clear();
                }
                foreach (var cb in callbacks) try { cb(); } catch (Exception ex) { Program.Log(ex); }
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }

        // Spotlight guarda los iconos pequenos de las apps que encuentra; si no lo usas en un rato, se sueltan.
        public static DateTime IconsUsedAt = DateTime.MinValue;
        public static void ReleaseIconsIfIdle(TimeSpan idle)
        {
            if (DateTime.Now - IconsUsedAt < idle || Spotlight.Current != null) return;
            lock (Lock)
            {
                if (apps == null) return;
                foreach (var e in apps) if (e.SmallIcon != null) { e.SmallIcon.Dispose(); e.SmallIcon = null; }
            }
        }

        public static void Launch(AppEntry a)
        {
            try
            {
                if (a.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) Process.Start("explorer.exe", "\"" + a.Path + "\"");
                else FileOps.Open(a.Path);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        static bool Skip(string name)
        {
            string low = name.ToLowerInvariant();
            return low.Contains("uninstall") || low.Contains("desinstal") || low.Contains("readme") || low.Contains("léame") ||
                   low.Contains("help") || low.Contains("ayuda") || low.Contains("documentation") || low.Contains("documentación") ||
                   low.Contains("release notes") || low.Contains("website") || low.Contains("sitio web");
        }

        // Carpeta virtual de apps de Windows (shell:AppsFolder): incluye programas de escritorio y apps de la Tienda.
        static List<AppEntry> FromAppsFolder()
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return null;
            dynamic sh = Activator.CreateInstance(t);
            try
            {
                dynamic folder = sh.NameSpace("shell:AppsFolder");
                if (folder == null) return null;
                var list = new List<AppEntry>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (dynamic item in folder.Items())
                {
                    string name = Convert.ToString(item.Name), id = Convert.ToString(item.Path);
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    if (id.Contains("://") || Skip(name)) continue;      // enlaces a paginas web
                    if (id.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".chm", StringComparison.OrdinalIgnoreCase) ||
                        id.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(name)) continue;
                    list.Add(new AppEntry { Name = name, Path = @"shell:AppsFolder\" + id });
                }
                return list;
            }
            finally { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh); } catch { } }
        }

        // Accesos directos del Escritorio y del dock y, si startMenu, tambien los del menu Inicio (respaldo).
        static List<AppEntry> FromShortcuts(bool startMenu)
        {
            var list = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new List<string> {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                System.IO.Path.Combine(Program.DataDir, "Apps") };
            if (startMenu) dirs.InsertRange(0, new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) });
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                var files = new List<string>();
                bool deep = d.IndexOf("Start Menu", StringComparison.OrdinalIgnoreCase) >= 0;   // del Escritorio y del dock solo el primer nivel
                SafeFind(d, files, deep ? 0 : 99);
                foreach (var f in files)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (Skip(name) || !seen.Add(name)) continue;
                    list.Add(new AppEntry { Name = name, Path = f });
                }
            }
            return list;
        }

        // recorre carpetas saltando las protegidas y los enlaces antiguos (como "Programas")
        static void SafeFind(string dir, List<string> outList, int depth)
        {
            if (depth > 6 && depth < 99) return;
            try
            {
                foreach (var f in Directory.GetFiles(dir)) { string e = System.IO.Path.GetExtension(f).ToLowerInvariant(); if (e == ".lnk" || e == ".url") outList.Add(f); }
                if (depth >= 99) return;
                foreach (var d in Directory.GetDirectories(dir))
                {
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    SafeFind(d, outList, depth + 1);
                }
            }
            catch { }
        }
    }
}
