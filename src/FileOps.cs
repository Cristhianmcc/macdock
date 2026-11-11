using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Operaciones de archivos con el motor de Windows (dialogos de progreso, deshacer con Ctrl+Z en el Explorador, papelera).
    static class FileOps
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd; public int wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public short fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHELLEXECUTEINFO
        {
            public int cbSize; public uint fMask; public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpDirectory;
            public int nShow; public IntPtr hInstApp, lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpClass;
            public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

        const int FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3, FO_RENAME = 4;
        const short FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMATION = 0x10, FOF_RENAMEONCOLLISION = 0x8, FOF_NOCONFIRMMKDIR = 0x200;

        static string Multi(IEnumerable<string> paths) { return string.Join("\0", paths.ToArray()) + "\0\0"; }

        static bool Run(IntPtr owner, int func, IEnumerable<string> from, string to, short flags)
        {
            var op = new SHFILEOPSTRUCT { hwnd = owner, wFunc = func, pFrom = Multi(from), pTo = to == null ? null : to + "\0\0", fFlags = flags };
            int r = SHFileOperation(ref op);
            return r == 0 && !op.fAnyOperationsAborted;
        }

        // a la papelera (se puede recuperar)
        public static bool Recycle(IntPtr owner, IEnumerable<string> paths)
        {
            return Run(owner, FO_DELETE, paths, null, (short)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION));
        }

        // mover si es el mismo disco, copiar si es otro (igual que el Explorador); Ctrl fuerza copiar, Shift fuerza mover
        public static bool Drop(IntPtr owner, IEnumerable<string> paths, string destFolder, Keys mods)
        {
            var list = paths.Where(p => !string.Equals(Path.GetDirectoryName(p.TrimEnd('\\')), destFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                                     && !string.Equals(p.TrimEnd('\\'), destFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
            if (list.Count == 0) return false;
            bool copy;
            if ((mods & Keys.Control) != 0) copy = true;
            else if ((mods & Keys.Shift) != 0) copy = false;
            else copy = !SameDrive(list[0], destFolder);
            return Run(owner, copy ? FO_COPY : FO_MOVE, list, destFolder, (short)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR));
        }

        public static bool Copy(IntPtr owner, IEnumerable<string> paths, string destFolder)
        {
            bool same = paths.Any(p => string.Equals(Path.GetDirectoryName(p.TrimEnd('\\')), destFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            return Run(owner, FO_COPY, paths, destFolder, (short)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | (same ? FOF_RENAMEONCOLLISION : 0)));
        }

        public static bool Move(IntPtr owner, IEnumerable<string> paths, string destFolder)
        {
            return Run(owner, FO_MOVE, paths, destFolder, (short)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR));
        }

        public static bool Rename(IntPtr owner, string path, string newName)
        {
            string dest = Path.Combine(Path.GetDirectoryName(path.TrimEnd('\\')), newName);
            return Run(owner, FO_RENAME, new[] { path }, dest, FOF_ALLOWUNDO);
        }

        static bool SameDrive(string a, string b)
        {
            try { return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase); } catch { return true; }
        }

        public static string NewFolder(string parent)
        {
            string name = "Carpeta nueva"; string p = Path.Combine(parent, name); int i = 2;
            while (Directory.Exists(p) || File.Exists(p)) { p = Path.Combine(parent, name + " (" + i + ")"); i++; }
            Directory.CreateDirectory(p);
            return p;
        }

        public static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Program.Log(ex); }
        }

        public static void OpenWith(string path)
        {
            try { Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path); } catch (Exception ex) { Program.Log(ex); }
        }

        public static void Properties(string path)
        {
            var info = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)), fMask = 0xC, lpVerb = "properties", lpFile = path, nShow = 5 };
            ShellExecuteEx(ref info);
        }

        public static void ShowInExplorer(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        // portapapeles compatible con el Explorador (copiar / cortar archivos)
        public static void ToClipboard(IEnumerable<string> paths, bool cut)
        {
            var sc = new StringCollection(); foreach (var p in paths) sc.Add(p);
            var data = new DataObject();
            data.SetFileDropList(sc);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            Clipboard.SetDataObject(data, true);
        }

        public static void Paste(IntPtr owner, string destFolder)
        {
            if (!Clipboard.ContainsFileDropList()) return;
            var files = Clipboard.GetFileDropList().Cast<string>().ToList();
            bool cut = false;
            try
            {
                var ms = Clipboard.GetData("Preferred DropEffect") as MemoryStream;
                if (ms != null) { var b = new byte[4]; ms.Read(b, 0, 4); cut = (BitConverter.ToInt32(b, 0) & 2) != 0; }
            }
            catch { }
            if (cut) { if (Move(owner, files, destFolder)) Clipboard.Clear(); }
            else Copy(owner, files, destFolder);
        }

        public static string HumanSize(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1024.0 / 1024).ToString("0.#") + " MB";
            return (b / 1024.0 / 1024 / 1024).ToString("0.##") + " GB";
        }
    }
}
