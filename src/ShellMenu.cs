using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Lee el menu de clic derecho REAL de Windows para el fondo del escritorio (Ver, Ordenar por, Nuevo, Pegar,
    // Git, NVIDIA, etc.) y lo copia dentro de un menu con estilo Mac. Al elegir una opcion, la ejecuta Windows.
    class ShellMenu : IDisposable
    {
        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eaten, out IntPtr pidl, IntPtr attrs);
            [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr e);
            [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int CompareIDs(IntPtr l, IntPtr p1, IntPtr p2);
            [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu2
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
            [PreserveSig] int GetCommandString(UIntPtr cmd, uint type, IntPtr res, IntPtr name, uint cch);
            [PreserveSig] int HandleMenuMsg(uint msg, IntPtr w, IntPtr l);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CMINVOKECOMMANDINFO { public int cbSize, fMask; public IntPtr hwnd, lpVerb, lpParameters, lpDirectory; public int nShow, dwHotKey; public IntPtr hIcon; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MENUITEMINFO { public int cbSize; public uint fMask, fType, fState, wID; public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData; public uint cch; public IntPtr hbmpItem; }

        [DllImport("shell32.dll")] static extern int SHGetDesktopFolder([MarshalAs(UnmanagedType.Interface)] out IShellFolder f);
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr h);
        [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMenuItemInfo(IntPtr h, uint item, bool byPos, ref MENUITEMINFO mii);

        const uint FIRST = 1, LAST = 0x7FFF;
        IContextMenu2 cm; IShellFolder desk; IntPtr hmenu; IntPtr owner;

        // Agrega las opciones de Windows al final del menu "m". Devuelve false si no se pudo leer.
        public bool AppendTo(ToolStripItemCollection items, IntPtr ownerHwnd)
        {
            owner = ownerHwnd;
            try
            {
                if (SHGetDesktopFolder(out desk) != 0 || desk == null) return false;
                Guid iid = typeof(IContextMenu2).GUID; object o;
                if (desk.CreateViewObject(owner, ref iid, out o) != 0 || o == null) return false;
                cm = (IContextMenu2)o;
                hmenu = CreatePopupMenu();
                if (cm.QueryContextMenu(hmenu, 0, FIRST, LAST, 0) < 0) return false;
                Copy(hmenu, items);
                return true;
            }
            catch (Exception ex) { Program.Log(ex); return false; }
        }

        void Copy(IntPtr menu, ToolStripItemCollection items)
        {
            int n = GetMenuItemCount(menu);
            bool lastSep = items.Count == 0 || items[items.Count - 1] is ToolStripSeparator;
            for (int i = 0; i < n; i++)
            {
                var mii = new MENUITEMINFO { cbSize = Marshal.SizeOf(typeof(MENUITEMINFO)), fMask = 0x1 | 0x2 | 0x4 | 0x100 | 0x40 };
                if (!GetMenuItemInfo(menu, (uint)i, true, ref mii)) continue;
                if ((mii.fType & 0x800) != 0) { if (!lastSep) { items.Add(new ToolStripSeparator()); lastSep = true; } continue; }   // separador
                string text = Text(menu, i, mii.cch);
                if (string.IsNullOrEmpty(text)) continue;                                                                   // dibujadas a mano: se omiten
                var item = new ToolStripMenuItem(text.Replace("&", "").Split('\t')[0])
                {
                    Enabled = (mii.fState & 0x3) == 0,
                    Checked = (mii.fState & 0x8) != 0
                };
                if (mii.hSubMenu != IntPtr.Zero)
                {
                    try { cm.HandleMenuMsg(0x117, mii.hSubMenu, new IntPtr(i)); } catch { }   // WM_INITMENUPOPUP: rellena submenus como "Nuevo"
                    Copy(mii.hSubMenu, item.DropDownItems);
                    if (item.DropDownItems.Count == 0) continue;
                }
                else
                {
                    uint id = mii.wID;
                    item.Click += (s, e) => Invoke(id);
                }
                items.Add(item); lastSep = false;
            }
            while (items.Count > 0 && items[items.Count - 1] is ToolStripSeparator) items.RemoveAt(items.Count - 1);
        }

        static string Text(IntPtr menu, int pos, uint cch)
        {
            if (cch == 0) return null;
            IntPtr buf = Marshal.AllocHGlobal((int)(cch + 1) * 2);
            try
            {
                var mii = new MENUITEMINFO { cbSize = Marshal.SizeOf(typeof(MENUITEMINFO)), fMask = 0x40, dwTypeData = buf, cch = cch + 1 };
                if (!GetMenuItemInfo(menu, (uint)pos, true, ref mii)) return null;
                return Marshal.PtrToStringUni(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        void Invoke(uint id)
        {
            try
            {
                var info = new CMINVOKECOMMANDINFO { cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFO)), hwnd = owner, lpVerb = new IntPtr(id - FIRST), nShow = 1 };
                cm.InvokeCommand(ref info);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public void Dispose()
        {
            try { if (hmenu != IntPtr.Zero) DestroyMenu(hmenu); } catch { }
            try { if (cm != null) Marshal.ReleaseComObject(cm); if (desk != null) Marshal.ReleaseComObject(desk); } catch { }
            hmenu = IntPtr.Zero; cm = null; desk = null;
        }
    }
}
