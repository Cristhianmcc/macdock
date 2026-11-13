using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

[assembly: AssemblyTitle("MacDock")]
[assembly: AssemblyDescription("Entorno de escritorio ligero estilo macOS para Windows 10/11")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCompany("MacDock Open Source Project")]
[assembly: AssemblyProduct("MacDock")]
[assembly: AssemblyCopyright("Copyright © 2026 MacDock Contributors - MIT License")]
[assembly: AssemblyVersion("3.9.0.0")]
[assembly: AssemblyFileVersion("3.9.0.0")]

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
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT val, int size);
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
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static extern bool SpiRect(int a, int u, ref RECT p, int f);
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);

        public static Bitmap GetIcon(string path, int px, int flags = 4)
        {
            IShellItemImageFactory f = null; IntPtr hb = IntPtr.Zero;
            try
            {
                Guid g = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref g, out f) == 0 && f != null)
                {
                    var sz = new N.SIZE { cx = px, cy = px };
                    if (f.GetImage(sz, flags, out hb) != 0 || hb == IntPtr.Zero)
                    {
                        hb = IntPtr.Zero;
                        int altFlag = (flags == 4) ? 0 : 4;
                        f.GetImage(sz, altFlag, out hb);
                    }
                    if (hb != IntPtr.Zero) return FromHBitmap(hb);
                }
            }
            catch { }
            finally
            {
                if (hb != IntPtr.Zero) N.DeleteObject(hb);
                if (f != null) Marshal.ReleaseComObject(f);
            }

            // Fallback 1: SHGetFileInfo (icono del sistema para esa carpeta o extension)
            try
            {
                var shfi = new SHFILEINFO();
                bool isDir = System.IO.Directory.Exists(path);
                uint attr = isDir ? 0x10u : 0x80u;
                uint uFlags = 0x100u | 0x0u; // SHGFI_ICON | SHGFI_LARGEICON
                if (!isDir && !System.IO.File.Exists(path)) uFlags |= 0x10u; // SHGFI_USEFILEATTRIBUTES
                if (SHGetFileInfo(path, attr, ref shfi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), uFlags) != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        using (var ico = Icon.FromHandle(shfi.hIcon))
                            return new Bitmap(ico.ToBitmap());
                    }
                    finally { DestroyIcon(shfi.hIcon); }
                }
            }
            catch { }

            // Fallback 2: Dibujar icono vectorial estilo macOS (Carpeta Azul o Documento) para que nunca quede un cuadro gris
            return DrawMacFallbackIcon(path, px);
        }

        public static Bitmap DrawMacFallbackIcon(string path, int px)
        {
            try
            {
                int sz = Math.Max(48, Math.Min(128, px));
                var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
                bool isDir = false;
                try { isDir = System.IO.Directory.Exists(path); } catch { }
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    float u = sz / 64f;
                    if (isDir)
                    {
                        using (var pBack = RoundRect(new RectangleF(5 * u, 12 * u, 54 * u, 40 * u), 5 * u))
                        using (var pTab = RoundRect(new RectangleF(5 * u, 8 * u, 23 * u, 11 * u), 3.5f * u))
                        using (var bBack = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(0, 8 * u, sz, 44 * u), Color.FromArgb(74, 150, 226), Color.FromArgb(52, 122, 204), 90f))
                        {
                            g.FillPath(bBack, pBack);
                            g.FillPath(bBack, pTab);
                        }
                        using (var pFront = RoundRect(new RectangleF(5 * u, 19 * u, 54 * u, 36 * u), 5 * u))
                        using (var bFront = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(0, 19 * u, sz, 36 * u), Color.FromArgb(132, 196, 252), Color.FromArgb(86, 160, 240), 90f))
                            g.FillPath(bFront, pFront);
                    }
                    else
                    {
                        var docRect = new RectangleF(12 * u, 6 * u, 40 * u, 52 * u);
                        using (var pDoc = RoundRect(docRect, 5 * u))
                        using (var bDoc = new System.Drawing.Drawing2D.LinearGradientBrush(docRect, Color.FromArgb(250, 250, 254), Color.FromArgb(222, 226, 236), 90f))
                        using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0), Math.Max(1f, 1.1f * u)))
                        {
                            g.FillPath(bDoc, pDoc);
                            g.DrawPath(pen, pDoc);
                        }
                        string ext = "";
                        try { ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant(); } catch { }
                        if (ext.Length > 4) ext = ext.Substring(0, 4);
                        if (ext != "")
                        {
                            Color badgeCol = (ext == "ZIP" || ext == "RAR" || ext == "7Z") ? Color.FromArgb(255, 159, 10)
                                           : (ext == "MP4" || ext == "MOV" || ext == "MKV") ? Color.FromArgb(191, 90, 242)
                                           : (ext == "PDF" || ext == "DOC" || ext == "DOCX") ? Color.FromArgb(255, 69, 58)
                                           : Color.FromArgb(10, 132, 255);
                            var br = new RectangleF(16 * u, 34 * u, 32 * u, 14 * u);
                            using (var pb = RoundRect(br, 3.5f * u))
                            using (var bb = new SolidBrush(badgeCol)) g.FillPath(bb, pb);
                            using (var f = new Font(UiFont.Semi, 8.5f * u, FontStyle.Regular, GraphicsUnit.Pixel))
                            {
                                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                                g.DrawString(ext, f, Brushes.White, br, sf);
                            }
                        }
                    }
                }
                return bmp;
            }
            catch { return null; }
        }

        static System.Drawing.Drawing2D.GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref N.BITMAPINFOHEADER bmi, uint usage);

        // Convierte el HBITMAP del Shell conservando la transparencia.
        // Antes se leian los pixeles a mano segun el signo de biHeight, pero en las miniaturas de fotos Windows a veces
        // reporta ese signo al reves y las fotos salian de cabeza. Ahora se le pide a Windows (GetDIBits) la imagen
        // ya ordenada de arriba hacia abajo (biHeight negativo), sin adivinar.
        static Bitmap FromHBitmap(IntPtr hb)
        {
            N.DIBSECTION ds;
            if (N.GetObject(hb, Marshal.SizeOf(typeof(N.DIBSECTION)), out ds) == 0 || ds.dsBm.bmBitsPixel != 32)
                return Image.FromHbitmap(hb);
            int w = ds.dsBm.bmWidth, h = Math.Abs(ds.dsBm.bmHeight);
            if (w <= 0 || h <= 0) return Image.FromHbitmap(hb);
            var bits = new byte[w * h * 4];
            var bi = new N.BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf(typeof(N.BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0 };
            IntPtr dc = N.GetDC(IntPtr.Zero);
            int got;
            try { got = GetDIBits(dc, hb, 0, (uint)h, bits, ref bi, 0); }
            finally { N.ReleaseDC(IntPtr.Zero, dc); }
            if (got != h) return Image.FromHbitmap(hb);

            bool anyAlpha = false;
            for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;   // fotos sin canal de transparencia: opacas

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(bits, y * w * 4, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), w * 4);
            bmp.UnlockBits(bd);
            return bmp;
        }
    }

    // Perfil inteligente del equipo: detecta automaticamente si es PC de Escritorio o Laptop, o permite forzarlo.
    static class DeviceProfile
    {
        // 0 = Automatico, 1 = Forzar PC de Escritorio (Alto Rendimiento), 2 = Forzar Laptop (Bateria)
        public static int Mode = 0;
        public static bool GamingMode = true;

        public static bool IsDesktop
        {
            get
            {
                if (Mode == 1) return true;
                if (Mode == 2) return false;
                try
                {
                    var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                    return ps.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery || ps.BatteryLifePercent < 0f || ps.BatteryLifePercent > 1f;
                }
                catch { return true; }
            }
        }
    }

    // Detecta juegos, renders, videos y presentaciones a pantalla completa (exclusiva o "sin bordes" / Borderless Windowed en Windows 11).
    static class FullscreenApp
    {
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out N.RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref N.POINT p);
        static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "#32768", "MultitaskingViewFrame", "ForegroundStaging", "CabinetWClass",
            "ScreenClippingWindow", "Xaml_WindowedPopupClass", "ToolTips_class32", "SysShadow" };
        static readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public static bool IsIgnoredOverlayProcess(uint pid)
        {
            string p = N.ProcessPath(pid);
            if (string.IsNullOrEmpty(p)) return false;
            string fn = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            return fn == "screenclippinghost" || fn == "snippingtool" || fn == "lightshot" || fn == "sharex" ||
                   fn == "flameshot" || fn == "greenshot" || fn == "picpick" || fn == "searchhost" ||
                   fn == "shellexperiencehost" || fn == "startmenuexperiencehost" || fn == "textinputhost" || fn == "lockapp";
        }

        public static bool Active(IntPtr me, System.Windows.Forms.Screen targetScreen = null)
        {
            try
            {
                IntPtr fg = N.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == me || N.IsIconic(fg)) return false;
                string cls = N.ClassOf(fg);
                if (Array.IndexOf(ShellClasses, cls) >= 0) return false;
                uint pid; N.GetWindowThreadProcessId(fg, out pid);
                if (pid == myPid || IsIgnoredOverlayProcess(pid)) return false;
                var scr = System.Windows.Forms.Screen.FromHandle(fg);
                var want = targetScreen ?? System.Windows.Forms.Screen.PrimaryScreen;
                if (scr.DeviceName != want.DeviceName) return false;

                var b = scr.Bounds;
                var wa = scr.WorkingArea;

                N.RECT wr;
                if (!N.GetWindowRect(fg, out wr)) return false;
                int ww = wr.right - wr.left, wh = wr.bottom - wr.top;

                // Nunca tratar overlays multi-monitor (como herramientas de captura de pantalla que abarcan varios monitores) como juego a pantalla completa
                if (ww > b.Width + 32 || wh > b.Height + 32 || wr.left < b.Left - 24 || wr.right > b.Right + 24)
                    return false;

                int st;
                if (SHQueryUserNotificationState(out st) == 0 && (st == 3 || st == 4) && scr.Primary) return true;   // D3D exclusivo o modo presentacion

                // Una ventana maximizada no es pantalla completa aunque no tenga barra de titulo clasica
                // (WhatsApp, Discord, Spotify...): con la barra de Windows oculta cubre todo el monitor y se confundia con un juego,
                // y entonces se escondian el dock, la barra superior y los botones de Mac.
                if (N.IsZoomed(fg)) return false;

                // 1) "Pantalla completa sin bordes" clasica: el area cliente cubre exactamente este monitor (con tolerancia de 2px de Windows 11).
                N.RECT c;
                if (GetClientRect(fg, out c))
                {
                    var p = new N.POINT(); ClientToScreen(fg, ref p);
                    int cw = c.right - c.left, ch = c.bottom - c.top;
                    if (cw <= b.Width + 16 && ch <= b.Height + 16 &&
                        p.x >= b.Left - 8 && p.x <= b.Left + 2 && p.y >= b.Top - 8 && p.y <= b.Top + 2 &&
                        p.x + cw >= b.Right - 2 && p.y + ch >= b.Bottom - 2)
                        return true;
                }

                int style = N.GetWindowLong(fg, -16);
                bool hasCaption = (style & 0x00C00000) == 0x00C00000; // WS_CAPTION
                // 2) Ventana sin bordes (sin WS_CAPTION, ej. juegos en "Ventana sin bordes" en Windows 11) que ocupa este monitor:
                if (!hasCaption)
                {
                    if (ww >= b.Width - 4 && wh >= b.Height - 4) return true;
                    if (ww >= wa.Width - 4 && wh >= wa.Height - 4 && !N.IsZoomed(fg)) return true;
                }
                // 3) Si SHQueryUserNotificationState reporta QUNS_BUSY (2) y la ventana cubre exactamente este monitor:
                if (st == 2 && wr.left >= b.Left - 8 && wr.left <= b.Left + 2 && wr.top <= b.Top + 2 && wr.right >= b.Right - 2 && wr.bottom >= b.Bottom - 2)
                    return true;
                return false;
            }
            catch { return false; }
        }
    }
}
