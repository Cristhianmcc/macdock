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
        Dictionary<int, string> pidPathCache = new Dictionary<int, string>();

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
            var nextCache = new Dictionary<int, string>();
            foreach (var p in all)
            {
                int pid = p.Id;
                string path = "";
                if (pid > 4)
                {
                    if (!pidPathCache.TryGetValue(pid, out path))
                    {
                        try { path = N.ProcessPath((uint)pid); } catch { path = ""; }
                    }
                    nextCache[pid] = path;
                }
                pathOf[pid] = path;
                if (audible.Contains((uint)pid) && path != "") audibleExes.Add(path);
                p.Dispose();
            }
            pidPathCache = nextCache;

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

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
            [PreserveSig] int GetChannelCount(out uint pnChannelCount);
            [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
            [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        public static float GetMasterVolume(out bool muted)
        {
            muted = false;
            IMMDeviceEnumerator en = null; IMMDevice dev = null; IAudioEndpointVolume ep = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return 0.5f;
                Guid iid = typeof(IAudioEndpointVolume).GUID; object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return 0.5f;
                ep = (IAudioEndpointVolume)o;
                float lvl = 0.5f;
                ep.GetMasterVolumeLevelScalar(out lvl);
                ep.GetMute(out muted);
                return Math.Max(0f, Math.Min(1f, lvl));
            }
            catch { return 0.5f; }
            finally
            {
                if (ep != null) Marshal.ReleaseComObject(ep);
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        public static void SetMasterVolume(float level, bool? mute = null)
        {
            IMMDeviceEnumerator en = null; IMMDevice dev = null; IAudioEndpointVolume ep = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return;
                Guid iid = typeof(IAudioEndpointVolume).GUID; object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return;
                ep = (IAudioEndpointVolume)o;
                Guid ctx = Guid.Empty;
                if (level >= 0f) ep.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, level)), ref ctx);
                if (mute.HasValue) ep.SetMute(mute.Value, ref ctx);
            }
            catch { }
            finally
            {
                if (ep != null) Marshal.ReleaseComObject(ep);
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }
    }

    // Control de brillo de pantalla en tiempo real:
    // 1) En Laptops: usa el driver nativo \\.\LCD (DeviceIoControl).
    // 2) En PC de Escritorio: usa DDC/CI (dxva2.dll) para cambiar el brillo real del monitor externo,
    //    y si el monitor tiene DDC/CI bloqueado, usa la tabla de iluminacion de hardware de la GPU (SetDeviceGammaRamp) con 0% de uso de CPU.
    static class DisplayBrightness
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr dev, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int ret, IntPtr ovl);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(N.POINT pt, uint flags);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint num);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint num, [Out] PHYSICAL_MONITOR[] mons);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool DestroyPhysicalMonitors(uint num, [In] PHYSICAL_MONITOR[] mons);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetMonitorBrightness(IntPtr hPhysicalMonitor, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool SetMonitorBrightness(IntPtr hPhysicalMonitor, uint brightness);
        [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct RAMP
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
        }

        static float lastVal = 1.0f;

        public static float Get()
        {
            // 1) Intentar panel interno de Laptop (\\.\LCD)
            IntPtr h = CreateFile(@"\\.\LCD", 0x80000000 | 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h != IntPtr.Zero && h != new IntPtr(-1))
            {
                try
                {
                    var buf = new byte[3]; int ret;
                    if (DeviceIoControl(h, 0x230498, null, 0, buf, 3, out ret, IntPtr.Zero) && ret >= 3)
                    {
                        byte b = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline ? buf[2] : buf[1];
                        lastVal = Math.Max(0.05f, Math.Min(1f, b / 100f));
                        return lastVal;
                    }
                }
                catch { }
                finally { N.CloseHandle(h); }
            }

            // 2) Intentar monitor de escritorio via DDC/CI
            try
            {
                var cp = Cursor.Position;
                IntPtr hMon = MonitorFromPoint(new N.POINT { x = cp.X, y = cp.Y }, 2);
                uint count;
                if (hMon != IntPtr.Zero && GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) && count > 0)
                {
                    var mons = new PHYSICAL_MONITOR[count];
                    if (GetPhysicalMonitorsFromHMONITOR(hMon, count, mons))
                    {
                        try
                        {
                            uint min, cur, max;
                            if (GetMonitorBrightness(mons[0].hPhysicalMonitor, out min, out cur, out max) && max > min)
                            {
                                lastVal = Math.Max(0.05f, Math.Min(1f, (float)(cur - min) / (max - min)));
                                return lastVal;
                            }
                        }
                        finally { DestroyPhysicalMonitors(count, mons); }
                    }
                }
            }
            catch { }

            return lastVal;
        }

        public static bool Set(float val)
        {
            lastVal = Math.Max(0.10f, Math.Min(1f, val));
            byte b = (byte)Math.Round(lastVal * 100f);

            // 1) Laptop (\\.\LCD)
            IntPtr h = CreateFile(@"\\.\LCD", 0x80000000 | 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h != IntPtr.Zero && h != new IntPtr(-1))
            {
                try
                {
                    var buf = new byte[] { 3, b, b }; int ret;
                    if (DeviceIoControl(h, 0x23049C, buf, 3, null, 0, out ret, IntPtr.Zero)) return true;
                }
                catch { }
                finally { N.CloseHandle(h); }
            }

            // 2) Monitor de escritorio via DDC/CI
            bool ddcOk = false;
            try
            {
                var cp = Cursor.Position;
                IntPtr hMon = MonitorFromPoint(new N.POINT { x = cp.X, y = cp.Y }, 2);
                uint count;
                if (hMon != IntPtr.Zero && GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) && count > 0)
                {
                    var mons = new PHYSICAL_MONITOR[count];
                    if (GetPhysicalMonitorsFromHMONITOR(hMon, count, mons))
                    {
                        try
                        {
                            for (int i = 0; i < count; i++)
                            {
                                uint min = 0, cur = 50, max = 100;
                                GetMonitorBrightness(mons[i].hPhysicalMonitor, out min, out cur, out max);
                                if (max <= min) { min = 0; max = 100; }
                                uint target = (uint)Math.Round(min + (max - min) * lastVal);
                                if (SetMonitorBrightness(mons[i].hPhysicalMonitor, target)) ddcOk = true;
                            }
                        }
                        finally { DestroyPhysicalMonitors(count, mons); }
                    }
                }
            }
            catch { }
            if (ddcOk) return true;

            // 3) Fallback universal para monitores de escritorio sin DDC/CI: rampa de brillo de hardware GPU (0% CPU)
            try
            {
                var ramp = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
                double factor = 0.45 + 0.55 * lastVal; // rango seguro de lectura [45%..100%]
                for (int i = 0; i < 256; i++)
                {
                    int v = (int)Math.Min(65535, Math.Max(0, Math.Round(i * 256.0 * factor)));
                    ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = (ushort)v;
                }
                IntPtr hdc = N.GetDC(IntPtr.Zero);
                try { return SetDeviceGammaRamp(hdc, ref ramp); }
                finally { N.ReleaseDC(IntPtr.Zero, hdc); }
            }
            catch { return false; }
        }
    }

    // Telemetria instantanea (CPU, RAM, Red Ethernet/Wi-Fi y Plan de Energia) sin consumo en segundo plano:
    // solo se consulta bajo demanda al abrir el Centro de Control.
    static class SystemStats
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr RootPowerKey, out IntPtr ActivePolicyGuid);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr RootPowerKey, ref Guid SchemeGuid);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr hMem);

        static ulong prevIdle, prevKernel, prevUser;
        static int lastCpu = 8;

        public static void SampleCpuAndRam(out int cpuPct, out int ramPct, out float ramUsedGb, out float ramTotalGb)
        {
            cpuPct = lastCpu; ramPct = 0; ramUsedGb = 0f; ramTotalGb = 16f;
            try
            {
                ulong idle, kernel, user;
                if (GetSystemTimes(out idle, out kernel, out user))
                {
                    ulong dIdle = idle - prevIdle, dKernel = kernel - prevKernel, dUser = user - prevUser;
                    ulong total = dKernel + dUser;
                    if (prevKernel != 0 && total > 0)
                        lastCpu = (int)Math.Max(1, Math.Min(100, Math.Round((1.0 - (double)dIdle / total) * 100.0)));
                    prevIdle = idle; prevKernel = kernel; prevUser = user;
                    cpuPct = lastCpu;
                }
            }
            catch { }
            try
            {
                var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (GlobalMemoryStatusEx(ref ms) && ms.ullTotalPhys > 0)
                {
                    ramPct = (int)ms.dwMemoryLoad;
                    ramTotalGb = (float)(ms.ullTotalPhys / (1024.0 * 1024.0 * 1024.0));
                    ramUsedGb = (float)((ms.ullTotalPhys - ms.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0));
                }
            }
            catch { }
        }

        static readonly Guid GuidHighPerf = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        static readonly Guid GuidUltimate = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");
        static readonly Guid GuidBalanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        static readonly Guid GuidPowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        static Guid savedTurboGuid = GuidHighPerf;

        public static bool IsHighPerformancePlan()
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out ptr) == 0 && ptr != IntPtr.Zero)
                {
                    Guid g = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
                    if (g != GuidBalanced && g != GuidPowerSaver)
                    {
                        savedTurboGuid = g;
                        return true;
                    }
                    return false;
                }
            }
            catch { }
            finally { if (ptr != IntPtr.Zero) LocalFree(ptr); }
            return false;
        }

        public static bool ToggleHighPerformancePlan()
        {
            try
            {
                if (IsHighPerformancePlan())
                {
                    Guid b = GuidBalanced;
                    PowerSetActiveScheme(IntPtr.Zero, ref b);
                    return false;
                }
                else
                {
                    Guid t = savedTurboGuid;
                    if (PowerSetActiveScheme(IntPtr.Zero, ref t) == 0) return true;
                    Guid h = GuidHighPerf;
                    PowerSetActiveScheme(IntPtr.Zero, ref h);
                    return true;
                }
            }
            catch { return false; }
        }

        // Detecta si la red activa es por cable Ethernet (comun en PC de escritorio) o Wi-Fi
        public static void GetNetworkInfo(out bool connected, out bool isEthernet, out string label)
        {
            connected = false; isEthernet = false; label = "Desconectado";
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
                    connected = true;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                    {
                        isEthernet = false;
                        label = "Wi-Fi conectado";
                        return;
                    }
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ||
                        ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet ||
                        ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT)
                    {
                        isEthernet = true;
                        label = "Cable LAN activo";
                    }
                }
            }
            catch { }
        }
    }

    // Control Multimedia ("Ahora suena") para el Centro de Control (0% CPU en segundo plano).
    static class MediaControl
    {
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public static void PlayPause() { SendMediaKey(0xB3); }
        public static void NextTrack() { SendMediaKey(0xB0); }
        public static void PrevTrack() { SendMediaKey(0xB1); }

        static void SendMediaKey(byte vk)
        {
            try { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); } catch { }
        }

        public static void GetNowPlaying(out string title, out string app, out bool playing)
        {
            title = "Reproductor multimedia";
            app = "Control de música y video";
            playing = false;
            try
            {
                var activePids = AudioSessions.ActivePids();
                playing = activePids.Count > 0;
                string[] mediaProcs = { "Spotify", "AppleMusic", "Music.UI", "vlc", "foobar2000", "AIMP", "wmplayer", "chrome", "msedge", "brave", "firefox", "opera" };
                foreach (var pid in activePids)
                {
                    try
                    {
                        var p = Process.GetProcessById((int)pid);
                        string t = p.MainWindowTitle;
                        if (!string.IsNullOrEmpty(t))
                        {
                            FormatTrack(t, p.ProcessName, out title, out app);
                            return;
                        }
                    }
                    catch { }
                }
                foreach (var mp in mediaProcs)
                {
                    foreach (var p in Process.GetProcessesByName(mp))
                    {
                        try
                        {
                            string t = p.MainWindowTitle;
                            if (!string.IsNullOrEmpty(t))
                            {
                                FormatTrack(t, p.ProcessName, out title, out app);
                                return;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        static void FormatTrack(string rawTitle, string procName, out string title, out string app)
        {
            title = rawTitle.Trim();
            string pn = procName.ToLowerInvariant();
            if (pn.Contains("spotify")) app = "Spotify";
            else if (pn.Contains("music")) app = "Música";
            else if (pn.Contains("vlc")) app = "VLC";
            else if (pn.Contains("chrome")) app = "Chrome";
            else if (pn.Contains("msedge")) app = "Edge";
            else if (pn.Contains("brave")) app = "Brave";
            else if (pn.Contains("firefox")) app = "Firefox";
            else app = procName;

            string[] suffixes = { " - Google Chrome", " - Microsoft\u200B Edge", " - Microsoft Edge", " - Brave", " - YouTube", " — Mozilla Firefox", " - VLC media player" };
            foreach (var sf in suffixes)
                if (title.EndsWith(sf, StringComparison.OrdinalIgnoreCase))
                    title = title.Substring(0, title.Length - sf.Length).Trim();
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
