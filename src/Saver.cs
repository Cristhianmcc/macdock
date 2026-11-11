using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // "App Nap" estilo Mac: con bateria, las apps que no estas usando pasan a modo eficiencia
    // (EcoQoS de Windows 11). La app en primer plano y las que estan sonando nunca se frenan.
    class AppNap
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

        [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr h, int cls, ref PROCESS_POWER_THROTTLING_STATE info, int size);

        static readonly HashSet<string> NeverNap = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "explorer.exe", "macdock.exe", "spotify.exe", "obs64.exe", "obs32.exe", "zoom.exe", "teams.exe", "ms-teams.exe",
            "discord.exe", "vlc.exe", "wmplayer.exe", "music.ui.exe", "audacity.exe", "steam.exe", "steamwebhelper.exe",
            "windowsterminal.exe", "openconsole.exe", "conhost.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
            "docker desktop.exe", "com.docker.backend.exe", "vmmem.exe", "surfshark.exe", "taskmgr.exe", "dwm.exe"
        };

        // pid -> exe que dejamos en modo eficiencia
        Dictionary<int, string> napped = new Dictionary<int, string>();

        static bool SetEco(int pid, bool on)
        {
            IntPtr h = N.OpenProcess(0x0200 | 0x1000, false, (uint)pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                var s = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1, StateMask = on ? 1u : 0u };
                return SetProcessInformation(h, 4, ref s, Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
            }
            finally { N.CloseHandle(h); }
        }

        // appExes: exes de las apps con ventana (las que muestra el dock). fgExe: la app que estas usando.
        public void Update(ICollection<string> appExes, string fgExe)
        {
            var audible = AudioSessions.ActivePids();
            var audibleExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var all = Process.GetProcesses();
            var pathOf = new Dictionary<int, string>();
            foreach (var p in all)
            {
                string path = "";
                try { path = N.ProcessPath((uint)p.Id); } catch { }
                pathOf[p.Id] = path;
                if (audible.Contains((uint)p.Id) && path != "") audibleExes.Add(path);
                p.Dispose();
            }

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var exe in appExes)
            {
                if (string.IsNullOrEmpty(exe)) continue;
                if (NeverNap.Contains(Path.GetFileName(exe))) continue;
                if (string.Equals(exe, fgExe, StringComparison.OrdinalIgnoreCase)) continue;
                if (audibleExes.Contains(exe)) continue;
                targets.Add(exe);
            }

            var keep = new Dictionary<int, string>();
            foreach (var kv in pathOf)
            {
                if (kv.Value == "" || !targets.Contains(kv.Value)) continue;
                if (!napped.ContainsKey(kv.Key)) SetEco(kv.Key, true);
                keep[kv.Key] = kv.Value;
            }
            foreach (var kv in napped)
                if (!keep.ContainsKey(kv.Key) && pathOf.ContainsKey(kv.Key)) SetEco(kv.Key, false);
            napped = keep;
        }

        public void WakeAll()
        {
            foreach (var pid in napped.Keys) { try { SetEco(pid, false); } catch { } }
            napped.Clear();
        }

        public int Count { get { return napped.Count; } }
    }

    // Que procesos estan reproduciendo sonido ahora mismo (Core Audio).
    static class AudioSessions
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice dev);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr g, int f, out IntPtr c);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr g, int f, out IntPtr v);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int n);
            [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr s);
            [PreserveSig] int SetDisplayName(IntPtr s, IntPtr g);
            [PreserveSig] int GetIconPath(out IntPtr s);
            [PreserveSig] int SetIconPath(IntPtr s, IntPtr g);
            [PreserveSig] int GetGroupingParam(out Guid g);
            [PreserveSig] int SetGroupingParam(IntPtr g, IntPtr c);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
            [PreserveSig] int GetSessionIdentifier(out IntPtr s);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr s);
            [PreserveSig] int GetProcessId(out uint pid);
        }

        public static HashSet<uint> ActivePids()
        {
            var set = new HashSet<uint>();
            IMMDeviceEnumerator en = null; IMMDevice dev = null; IAudioSessionManager2 mgr = null; IAudioSessionEnumerator se = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return set;
                Guid iid = typeof(IAudioSessionManager2).GUID; object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return set;
                mgr = (IAudioSessionManager2)o;
                if (mgr.GetSessionEnumerator(out se) != 0 || se == null) return set;
                int n; se.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 s;
                    if (se.GetSession(i, out s) != 0 || s == null) continue;
                    int st; uint pid;
                    if (s.GetState(out st) == 0 && st == 1 && s.GetProcessId(out pid) == 0 && pid != 0) set.Add(pid);
                    Marshal.ReleaseComObject(s);
                }
            }
            catch { }
            finally
            {
                if (se != null) Marshal.ReleaseComObject(se);
                if (mgr != null) Marshal.ReleaseComObject(mgr);
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
            return set;
        }
    }

    // Esquinas activas: arriba-izquierda = ver todas las ventanas (como Mission Control),
    // abajo-izquierda = mostrar el escritorio. Hay que dejar el mouse quieto un instante en la esquina.
    class HotCorners
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        DateTime enteredAt = DateTime.MaxValue; int corner = 0; bool fired = false;

        // con la barra superior activa, el logo esta arriba-izquierda: la esquina de ventanas pasa a arriba-derecha
        public void Tick(Screen scr, bool menuBarOn)
        {
            var c = Cursor.Position; var b = scr.Bounds;
            int now = 0;
            if (!menuBarOn && c.X <= b.Left + 1 && c.Y <= b.Top + 1) now = 1;
            else if (menuBarOn && c.X >= b.Right - 2 && c.Y <= b.Top + 1) now = 1;
            else if (c.X <= b.Left + 1 && c.Y >= b.Bottom - 2) now = 2;
            bool buttons = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (now == 0 || buttons) { corner = 0; fired = false; enteredAt = DateTime.MaxValue; return; }
            if (now != corner) { corner = now; fired = false; enteredAt = DateTime.Now; return; }
            if (fired || (DateTime.Now - enteredAt).TotalMilliseconds < 350) return;
            fired = true;
            if (corner == 1) N.PressKeys(0x5B, 0x09);      // Win+Tab: vista de todas las ventanas
            else N.PressKeys(0x5B, 0x44);                  // Win+D: escritorio
        }
    }
}
