using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace MacDock
{
    class NowPlayingInfo
    {
        public string Title = "", Artist = "", App = "", AppId = "";
        public bool Playing, HasSession;
        public Bitmap Art, AppIcon;
        public string Key { get { return Title + "|" + Artist + "|" + AppId + "|" + Playing + "|" + (Art != null); } }
    }

    // Que esta sonando (Spotify, YouTube Music en el navegador, Apple Music, VLC...).
    // Usa el modulo MacDockMedia.dll que build.ps1 mete dentro del exe (API oficial de Windows). Si no esta,
    // adivina por el titulo de la ventana como antes y usa las teclas multimedia.
    // Todas las consultas van en un hilo de fondo; la ventana pide Refresh y recibe el resultado en un aviso.
    static class NowPlaying
    {
        static Type media; static bool tried;
        static readonly object Lock = new object();
        static NowPlayingInfo last; static bool busy;
        static readonly System.Collections.Generic.List<Action<NowPlayingInfo>> waiting = new System.Collections.Generic.List<Action<NowPlayingInfo>>();

        public static NowPlayingInfo Last { get { lock (Lock) return last; } }

        static Type Media()
        {
            lock (Lock)
            {
                if (tried) return media;
                tried = true;
                try
                {
                    using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("MacDockMedia.dll"))
                    {
                        if (st == null) { Program.Log("Musica: esta version se compilo sin el modulo de Windows (revisa el aviso de build.ps1). Se usa el titulo de la ventana."); return null; }
                        var ms = new MemoryStream(); st.CopyTo(ms);
                        // se copia a un archivo y se carga desde ahi: las funciones de Windows (WinRT) no siempre cargan desde la memoria
                        string path = Path.Combine(Program.DataDir, "MacDockMedia.dll");
                        var bytes = ms.ToArray();
                        try { if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length) { Directory.CreateDirectory(Program.DataDir); File.WriteAllBytes(path, bytes); } }
                        catch { }   // si el archivo esta en uso por otra copia, sirve el que ya estaba
                        media = Assembly.LoadFrom(path).GetType("MacDockMedia.Media", true);
                        // prueba real: si Windows no deja usar la API, se nota aqui y se usa el metodo anterior
                        media.GetMethod("Now", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                    }
                }
                catch (Exception ex) { Program.Log("Musica: no se pudo usar el modulo de Windows, se usa el titulo de la ventana. Detalle: " + (ex.InnerException ?? ex)); media = null; }
                return media;
            }
        }

        static object Call(string name)
        {
            var t = Media();
            if (t == null) return null;
            return t.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        }

        // Pide el estado en segundo plano; done(info) llega en ese hilo (la ventana hace BeginInvoke).
        public static void Refresh(Action<NowPlayingInfo> done)
        {
            lock (Lock) { if (done != null) waiting.Add(done); if (busy) return; busy = true; }   // si ya hay una consulta, se espera esa
            ThreadPool.QueueUserWorkItem(_ =>
            {
                NowPlayingInfo info = null;
                try { info = Read(); } catch (Exception ex) { Program.Log(ex); }
                if (info == null) info = new NowPlayingInfo();
                NowPlayingInfo old;
                lock (Lock)
                {
                    old = last;
                    // se reutilizan: la portada si es la misma cancion, el icono si es la misma app
                    if (old != null)
                    {
                        if (old.Title == info.Title && old.AppId == info.AppId && info.Art == null) info.Art = old.Art;
                        else if (old.Art != null && old.Art != info.Art) old.Art.Dispose();
                        if (old.AppId == info.AppId && info.AppIcon == null) info.AppIcon = old.AppIcon;
                        else if (old.AppIcon != null && old.AppIcon != info.AppIcon) old.AppIcon.Dispose();
                    }
                    last = info; busy = false;
                }
                Action<NowPlayingInfo>[] cbs;
                lock (Lock) { cbs = waiting.ToArray(); waiting.Clear(); }
                foreach (var cb in cbs) try { cb(info); } catch { }
            });
        }

        static NowPlayingInfo Read()
        {
            if (Media() != null)
            {
                object[] r = null;
                try { r = Call("Now") as object[]; } catch (Exception ex) { Program.Log(ex); }
                var info = new NowPlayingInfo();
                if (r == null) return info;
                info.HasSession = true;
                info.Title = (string)r[0]; info.Artist = (string)r[1]; info.AppId = (string)r[2]; info.Playing = (bool)r[3];
                var art = r[4] as byte[];
                if (art != null && art.Length > 0 && (last == null || last.Title != info.Title || last.Art == null))
                    try { using (var ms = new MemoryStream(art)) using (var img = Image.FromStream(ms)) info.Art = new Bitmap(img, 96, 96); } catch { }
                info.App = AppName(info.AppId);
                if (last == null || last.AppId != info.AppId || last.AppIcon == null)
                    try { info.AppIcon = Shell.GetIcon(info.AppId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? info.AppId : Shell.AppsFolderPath(info.AppId), 32); } catch { }
                return info;
            }
            // sin el modulo: el metodo anterior (titulo de la ventana)
            string title, app; bool playing;
            MediaControl.GetNowPlaying(out title, out app, out playing);
            var fb = new NowPlayingInfo { Title = title, App = app, AppId = app, Playing = playing };
            fb.HasSession = title != "Reproductor multimedia" && title.IndexOf("Nueva pestaña", StringComparison.OrdinalIgnoreCase) < 0 && title.IndexOf("New Tab", StringComparison.OrdinalIgnoreCase) < 0;
            // Spotify pone "Artista - Cancion" en su ventana
            int dash = title.IndexOf(" - ");
            if (app == "Spotify" && dash > 0) { fb.Artist = title.Substring(0, dash).Trim(); fb.Title = title.Substring(dash + 3).Trim(); }
            // icono de la app que suena
            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName(app == "Edge" ? "msedge" : app);
                foreach (var pr in procs) using (pr)
                {
                    if (fb.AppIcon != null) continue;
                    string pth = N.ProcessPath((uint)pr.Id);
                    if (pth != "" && (last == null || last.AppIcon == null || last.AppId != app))
                    {
                        string aid = Shell.AppIdOf((uint)pr.Id);
                        fb.AppIcon = Shell.GetIcon(aid != "" ? Shell.AppsFolderPath(aid) : pth, 32);
                    }
                }
            }
            catch { }
            return fb;
        }

        static string AppName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string nice = AppIndex.NameForAppId(id);
            if (!string.IsNullOrEmpty(nice)) return nice;
            string n = id;
            int bang = n.IndexOf('!'); if (bang >= 0) n = n.Substring(bang + 1);
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = Path.GetFileNameWithoutExtension(n);
            switch (n.ToLowerInvariant())
            {
                case "chrome": return "Chrome";
                case "msedge": return "Microsoft Edge";
                case "brave": return "Brave";
                case "firefox": case "308046b0af4a39cb": return "Firefox";
                case "opera": return "Opera";
                case "spotify": return "Spotify";
            }
            return n.Length > 0 ? char.ToUpperInvariant(n[0]) + n.Substring(1) : n;
        }

        // Controles: a la app que esta sonando (o teclas multimedia si no hay modulo).
        public static void PlayPause(Action after) { Do("Toggle", MediaControl.PlayPause, after); }
        public static void Next(Action after) { Do("Next", MediaControl.NextTrack, after); }
        public static void Prev(Action after) { Do("Prev", MediaControl.PrevTrack, after); }

        static void Do(string name, Action fallback, Action after)
        {
            if (Media() == null) { fallback(); if (after != null) after(); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try { ok = (bool)Call(name); } catch (Exception ex) { Program.Log(ex); }
                if (!ok) fallback();
                Thread.Sleep(250);   // da tiempo a que la app cambie de cancion o de estado
                if (after != null) try { after(); } catch { }
            });
        }
    }
}
