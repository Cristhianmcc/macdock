using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MacDock
{
    static class N
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize; public IntPtr hWnd; public int uCallbackMessage; public int uEdge; public RECT rc; public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint b0, b1, b2; public IntPtr dshSection; public uint dsOffset; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int c, out DIBSECTION pv);

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, IntPtr e);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);

        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);

        [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(int msg, ref APPBARDATA data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, int flags);
        [DllImport("shell32.dll")] public static extern int SHGetKnownFolderPath(ref Guid id, int flags, IntPtr token, out IntPtr path);
        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")] public static extern bool SpiStr(int a, int u, string p, int f);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, int flags, int timeout, out IntPtr res);
        public static void BroadcastSetting(string what) { IntPtr r; SendMessageTimeout(new IntPtr(0xffff), 0x1A, IntPtr.Zero, what, 2, 2000, out r); }

        public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
        public static string TitleOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }

        public static string ProcessPath(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return "";
            try { var sb = new StringBuilder(1024); int n = 1024; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : ""; }
            finally { CloseHandle(h); }
        }

        public static void PressKeys(params byte[] vks)
        {
            foreach (var k in vks) keybd_event(k, 0, 0, IntPtr.Zero);
            for (int i = vks.Length - 1; i >= 0; i--) keybd_event(vks[i], 0, 2, IntPtr.Zero);
        }

        public static string Downloads()
        {
            Guid g = new Guid("374DE290-123F-4565-9164-39C4925E467B"); IntPtr p;
            if (SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out p) != 0) return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
            try { return Marshal.PtrToStringUni(p); } finally { CoTaskMemFree(p); }
        }

        public static bool RecycleBinFull()
        {
            var info = new SHQUERYRBINFO(); info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
            return SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems > 0;
        }
    }

    // Frecuencia de la pantalla (Hz): 60 Hz con bateria, maxima enchufado.
    static class Display
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr lparam);

        static DEVMODE New() { var d = new DEVMODE(); d.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); return d; }

        public static int Current(string dev)
        {
            var d = New();
            return EnumDisplaySettings(dev, -1, ref d) ? d.dmDisplayFrequency : 0;
        }

        // Frecuencias disponibles con la resolucion actual
        public static System.Collections.Generic.List<int> Rates(string dev)
        {
            var list = new System.Collections.Generic.List<int>();
            var cur = New();
            if (!EnumDisplaySettings(dev, -1, ref cur)) return list;
            for (int i = 0; ; i++)
            {
                var d = New();
                if (!EnumDisplaySettings(dev, i, ref d)) break;
                if (d.dmPelsWidth == cur.dmPelsWidth && d.dmPelsHeight == cur.dmPelsHeight && d.dmBitsPerPel == cur.dmBitsPerPel
                    && d.dmDisplayFrequency > 1 && !list.Contains(d.dmDisplayFrequency))
                    list.Add(d.dmDisplayFrequency);
            }
            list.Sort();
            return list;
        }

        public static bool Set(string dev, int hz)
        {
            var d = New();
            if (!EnumDisplaySettings(dev, -1, ref d)) return false;
            if (d.dmDisplayFrequency == hz) return true;
            d.dmDisplayFrequency = hz;
            d.dmFields = 0x400000;
            return ChangeDisplaySettingsEx(dev, ref d, IntPtr.Zero, 0, IntPtr.Zero) == 0;
        }
    }

    static class UiFont
    {
        static string Pick(string want, string fallback) { try { using (var f = new FontFamily(want)) return want; } catch { return fallback; } }
        public static readonly string Reg = Pick("Inter", "Segoe UI");
        public static readonly string Semi = Pick("Inter SemiBold", "Segoe UI Semibold");
    }

    static class Shell
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(N.SIZE size, int flags, out IntPtr phbm); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class CShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder f, int cch, IntPtr pfd, int flags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string s);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string s);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string s);
            void GetHotkey(out short k);
            void SetHotkey(short k);
            void GetShowCmd(out int c);
            void SetShowCmd(int c);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch, out int i);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string s, int i);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string s, int r);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string s);
        }

        public static void ReadLnk(string lnk, out string target, out string args)
        {
            target = ""; args = "";
            try
            {
                var link = (IShellLinkW)new CShellLink();
                ((IPersistFile)link).Load(lnk, 0);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0); target = Environment.ExpandEnvironmentVariables(sb.ToString());
                sb.Length = 0; link.GetArguments(sb, sb.Capacity); args = sb.ToString();
                Marshal.ReleaseComObject(link);
            }
            catch { }
        }

        public static void CreateLnk(string lnk, string target)
        {
            var link = (IShellLinkW)new CShellLink();
            link.SetPath(target);
            link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target));
            ((IPersistFile)link).Save(lnk, true);
            Marshal.ReleaseComObject(link);
        }

        public static void CreateLnkArgs(string lnk, string target, string args)
        {
            var link = (IShellLinkW)new CShellLink();
            link.SetPath(target);
            link.SetArguments(args);
            link.SetIconLocation(target, 0);
            link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target));
            ((IPersistFile)link).Save(lnk, true);
            Marshal.ReleaseComObject(link);
        }

        public static Bitmap GetIcon(string path, int px, int flags = 4)
        {
            IShellItemImageFactory f = null; IntPtr hb = IntPtr.Zero;
            try
            {
                Guid g = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref g, out f) != 0 || f == null) return null;
                var sz = new N.SIZE(); sz.cx = px; sz.cy = px;
                if (f.GetImage(sz, flags, out hb) != 0 || hb == IntPtr.Zero) return null;
                return FromHBitmap(hb);
            }
            catch { return null; }
            finally
            {
                if (hb != IntPtr.Zero) N.DeleteObject(hb);
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }

        static Bitmap FromHBitmap(IntPtr hb)
        {
            N.DIBSECTION ds;
            if (N.GetObject(hb, Marshal.SizeOf(typeof(N.DIBSECTION)), out ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
                return Image.FromHbitmap(hb);
            int w = ds.dsBm.bmWidth, h = Math.Abs(ds.dsBm.bmHeight);
            bool bottomUp = ds.dsBmih.biHeight > 0;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            var row = new byte[w * 4]; bool anyAlpha = false;
            for (int y = 0; y < h; y++)
            {
                IntPtr src = new IntPtr(ds.dsBm.bmBits.ToInt64() + (long)(bottomUp ? (h - 1 - y) : y) * ds.dsBm.bmWidthBytes);
                Marshal.Copy(src, row, 0, row.Length);
                if (!anyAlpha) for (int i = 3; i < row.Length; i += 4) if (row[i] != 0) { anyAlpha = true; break; }
                Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row.Length);
            }
            if (!anyAlpha)
            {
                for (int y = 0; y < h; y++)
                {
                    IntPtr dst = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                    Marshal.Copy(dst, row, 0, row.Length);
                    for (int i = 3; i < row.Length; i += 4) row[i] = 255;
                    Marshal.Copy(row, 0, dst, row.Length);
                }
            }
            bmp.UnlockBits(bd);
            return bmp;
        }
    }

    // Detecta juegos, videos y presentaciones a pantalla completa (exclusiva o "sin bordes") en la pantalla principal.
    static class FullscreenApp
    {
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out N.RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref N.POINT p);
        static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "#32768", "MultitaskingViewFrame", "ForegroundStaging" };
        static readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public static bool Active(IntPtr me)
        {
            try
            {
                IntPtr fg = N.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == me || N.IsIconic(fg)) return false;
                if (Array.IndexOf(ShellClasses, N.ClassOf(fg)) >= 0) return false;
                uint pid; N.GetWindowThreadProcessId(fg, out pid);
                if (pid == myPid) return false;
                var scr = System.Windows.Forms.Screen.FromHandle(fg);
                if (!scr.Primary) return false;
                int st;
                if (SHQueryUserNotificationState(out st) == 0 && (st == 3 || st == 4)) return true;   // D3D exclusivo o modo presentacion
                // "Pantalla completa sin bordes": el area de la ventana (sin titulo) cubre toda la pantalla.
                // Una ventana maximizada normal no la cubre (deja la barra superior y tiene titulo).
                N.RECT c; if (!GetClientRect(fg, out c)) return false;
                var p = new N.POINT(); ClientToScreen(fg, ref p);
                var b = scr.Bounds;
                return p.x <= b.Left && p.y <= b.Top && p.x + (c.right - c.left) >= b.Right && p.y + (c.bottom - c.top) >= b.Bottom;
            }
            catch { return false; }
        }
    }
}
