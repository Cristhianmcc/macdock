using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MacDock
{
    // Interruptores reales del Centro de Control: Wi-Fi, Bluetooth, Compartir en proximidad y No molestar.
    // Wi-Fi y Bluetooth usan la API oficial de Windows (Windows.Devices.Radios), la misma que usa el panel de Windows.
    // Se carga por reflexion para seguir compilando con el csc de .NET Framework que trae Windows, sin dependencias.
    // Todo esto puede tardar un poco: llamarlo desde un hilo de fondo, nunca desde el hilo de la ventana.
    static class SysToggles
    {
        public const int WiFi = 1, Bluetooth = 3;          // Windows.Devices.Radios.RadioKind
        const int StateOn = 1, StateOff = 2;               // Windows.Devices.Radios.RadioState

        static readonly object Lock = new object();
        static bool initDone, initOk;
        static Type radioT, accessT, stateT;
        static MethodInfo asTaskOp;

        static bool Init()
        {
            lock (Lock)
            {
                if (initDone) return initOk;
                initDone = true;
                try
                {
                    radioT = Type.GetType("Windows.Devices.Radios.Radio, Windows, ContentType=WindowsRuntime", true);
                    accessT = Type.GetType("Windows.Devices.Radios.RadioAccessStatus, Windows, ContentType=WindowsRuntime", true);
                    stateT = Type.GetType("Windows.Devices.Radios.RadioState, Windows, ContentType=WindowsRuntime", true);
                    var rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                    asTaskOp = rt.GetType("System.WindowsRuntimeSystemExtensions").GetMethods()
                        .First(m => m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1
                                 && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
                    Await(radioT.GetMethod("RequestAccessAsync").Invoke(null, null), accessT);
                    initOk = true;
                }
                catch (Exception ex) { Program.Log(ex); initOk = false; }
                return initOk;
            }
        }

        static object Await(object op, Type resultType)
        {
            var t = (Task)asTaskOp.MakeGenericMethod(resultType).Invoke(null, new[] { op });
            if (!t.Wait(5000)) return null;
            return t.GetType().GetProperty("Result").GetValue(t, null);
        }

        static object FindRadio(int kind)
        {
            var listT = typeof(IReadOnlyList<>).MakeGenericType(radioT);
            var radios = Await(radioT.GetMethod("GetRadiosAsync").Invoke(null, null), listT) as IEnumerable;
            if (radios == null) return null;
            var kindProp = radioT.GetProperty("Kind");
            foreach (var r in radios) if (Convert.ToInt32(kindProp.GetValue(r, null)) == kind) return r;
            return null;
        }

        // -1 = este equipo no lo tiene (o Windows no deja leerlo), 0 = apagado, 1 = encendido
        public static int GetRadio(int kind)
        {
            try
            {
                if (!Init()) return -1;
                var r = FindRadio(kind);
                if (r == null) return -1;
                return Convert.ToInt32(radioT.GetProperty("State").GetValue(r, null)) == StateOn ? 1 : 0;
            }
            catch (Exception ex) { Program.Log(ex); return -1; }
        }

        public static bool SetRadio(int kind, bool on)
        {
            try
            {
                if (!Init()) return false;
                var r = FindRadio(kind);
                if (r == null) return false;
                var op = radioT.GetMethod("SetStateAsync").Invoke(r, new[] { Enum.ToObject(stateT, on ? StateOn : StateOff) });
                var res = Await(op, accessT);
                return res != null && Convert.ToInt32(res) == 1;   // RadioAccessStatus.Allowed
            }
            catch (Exception ex) { Program.Log(ex); return false; }
        }

        // ---- Nombre de la red Wi-Fi y aparatos Bluetooth conectados ----
        // Con la API de Windows.Networking: NO necesita el permiso de ubicacion que exige netsh en Windows 11 24H2.
        static Type WinRt(string name)
        {
            return Type.GetType(name + ", Windows, ContentType=WindowsRuntime", true);
        }

        public static string GetWifiName()
        {
            try
            {
                var niT = WinRt("Windows.Networking.Connectivity.NetworkInformation");
                var cpT = WinRt("Windows.Networking.Connectivity.ConnectionProfile");
                var profiles = new List<object>();
                var inet = niT.GetMethod("GetInternetConnectionProfile").Invoke(null, null);
                if (inet != null) profiles.Add(inet);
                var all = niT.GetMethod("GetConnectionProfiles").Invoke(null, null) as IEnumerable;
                if (all != null) foreach (var p in all) profiles.Add(p);
                foreach (var p in profiles)
                {
                    if (!(bool)cpT.GetProperty("IsWlanConnectionProfile").GetValue(p, null)) continue;
                    // solo la red a la que de verdad esta conectado (nivel de conectividad > ninguno)
                    if (Convert.ToInt32(cpT.GetMethod("GetNetworkConnectivityLevel").Invoke(p, null)) == 0) continue;
                    string ssid = null;
                    try
                    {
                        var det = cpT.GetProperty("WlanConnectionProfileDetails").GetValue(p, null);
                        if (det != null) ssid = (string)det.GetType().GetMethod("GetConnectedSsid").Invoke(det, null);
                    }
                    catch { }
                    if (string.IsNullOrEmpty(ssid)) ssid = (string)cpT.GetProperty("ProfileName").GetValue(p, null);
                    if (!string.IsNullOrEmpty(ssid)) return ssid;
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            return null;
        }

        // nombres de los aparatos Bluetooth conectados ahora (audifonos, mouse, teclado...). Vacio si no hay.
        public static List<string> GetBluetoothConnected()
        {
            var names = new List<string>();
            try
            {
                if (!Init()) return names;
                var diT = WinRt("Windows.Devices.Enumeration.DeviceInformation");
                var colT = WinRt("Windows.Devices.Enumeration.DeviceInformationCollection");
                var find = diT.GetMethod("FindAllAsync", new[] { typeof(string) });
                foreach (var devName in new[] { "Windows.Devices.Bluetooth.BluetoothDevice", "Windows.Devices.Bluetooth.BluetoothLEDevice" })
                {
                    var devT = WinRt(devName);
                    var stT = WinRt("Windows.Devices.Bluetooth.BluetoothConnectionStatus");
                    var sel = (string)devT.GetMethod("GetDeviceSelectorFromConnectionStatus").Invoke(null, new[] { Enum.ToObject(stT, 1) });   // 1 = Connected
                    var res = Await(find.Invoke(null, new object[] { sel }), colT) as IEnumerable;
                    if (res == null) continue;
                    foreach (var d in res)
                    {
                        var n = (string)diT.GetProperty("Name").GetValue(d, null);
                        if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
                    }
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            return names;
        }

        // ---- Compartir en proximidad (Configuracion > Sistema > Compartir en proximidad) ----
        const string CdpKey = @"Software\Microsoft\Windows\CurrentVersion\CDP";

        public static bool GetNearShare()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(CdpKey))
                    return k != null && Convert.ToInt32(k.GetValue("NearShareChannelUserAuthzPolicy", 0)) > 0;
            }
            catch { return false; }
        }

        // encendido = "Solo mis dispositivos" (lo mas seguro); apagado = desactivado
        public static void SetNearShare(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(CdpKey))
            {
                k.SetValue("NearShareChannelUserAuthzPolicy", on ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("CdpSessionUserAuthzPolicy", on ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        // ---- No molestar / Enfoque ----
        // Es el estado que Windows publica para "No molestar" (WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED).
        // 0 = apagado, 1 = solo prioridad (lo que activa el boton de No molestar).
        [DllImport("ntdll.dll")] static extern int NtQueryWnfStateData(ref ulong name, IntPtr typeId, IntPtr scope, out uint changeStamp, byte[] buf, ref uint size);
        [DllImport("ntdll.dll")] static extern int NtUpdateWnfStateData(ref ulong name, byte[] buf, int len, IntPtr typeId, IntPtr scope, uint matchingStamp, int checkStamp);
        const ulong QuietHours = 0x0D83063EA3BF1C75UL;

        public static bool GetDnd()
        {
            try
            {
                ulong n = QuietHours; uint stamp; var buf = new byte[4]; uint size = 4;
                return NtQueryWnfStateData(ref n, IntPtr.Zero, IntPtr.Zero, out stamp, buf, ref size) == 0 && size >= 1 && buf[0] != 0;
            }
            catch { return false; }
        }

        public static bool SetDnd(bool on)
        {
            try
            {
                ulong n = QuietHours; var buf = new byte[] { (byte)(on ? 1 : 0), 0, 0, 0 };
                return NtUpdateWnfStateData(ref n, buf, 4, IntPtr.Zero, IntPtr.Zero, 0, 0) == 0;
            }
            catch (Exception ex) { Program.Log(ex); return false; }
        }
    }
}
