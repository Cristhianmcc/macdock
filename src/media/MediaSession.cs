// Modulo opcional: "que esta sonando" con la API oficial de Windows (la misma del control de volumen de Windows 11).
// Da titulo, artista, portada y controles reales de Spotify, YouTube Music en el navegador, Apple Music, VLC...
// build.ps1 lo compila aparte contra los metadatos de Windows (WinMetadata) y lo mete dentro de MacDock.exe;
// si no se puede compilar en esa PC, MacDock usa el metodo anterior (titulo de la ventana y teclas multimedia).
// Todo se llama desde un hilo de fondo (las llamadas esperan a Windows).
//
// Nota: no se usa AsTask() (System.Runtime.WindowsRuntime): con los metadatos de System32 el compilador de .NET Framework
// no lo reconoce para IAsyncOperation<T>. Se espera "a mano" con Status / GetResults, que vienen en los mismos metadatos.
using System;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MacDockMedia
{
    public static class Media
    {
        static GlobalSystemMediaTransportControlsSessionManager mgr;

        // espera una operacion de Windows (maximo 5 s)
        static T Wait<T>(IAsyncOperation<T> op)
        {
            var start = DateTime.UtcNow;
            while (op.Status == AsyncStatus.Started)
            {
                if ((DateTime.UtcNow - start).TotalSeconds > 5) { try { op.Cancel(); } catch { } throw new TimeoutException("Windows no respondio"); }
                Thread.Sleep(5);
            }
            if (op.Status != AsyncStatus.Completed) throw op.ErrorCode ?? new InvalidOperationException(op.Status.ToString());
            return op.GetResults();
        }

        static GlobalSystemMediaTransportControlsSession Session()
        {
            if (mgr == null) mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            return mgr.GetCurrentSession();
        }

        // { titulo, artista, id de la app, reproduciendo (bool), portada (byte[] o null) } o null si no hay nada
        public static object[] Now()
        {
            var s = Session();
            if (s == null) return null;
            var props = Wait(s.TryGetMediaPropertiesAsync());
            var info = s.GetPlaybackInfo();
            byte[] art = null;
            try
            {
                if (props.Thumbnail != null)
                    using (var st = Wait(props.Thumbnail.OpenReadAsync()))
                    {
                        uint size = (uint)st.Size;
                        if (size > 0 && size < 20 * 1024 * 1024)
                            using (var reader = new DataReader(st.GetInputStreamAt(0)))
                            {
                                Wait(reader.LoadAsync(size));
                                art = new byte[size];
                                reader.ReadBytes(art);
                            }
                    }
            }
            catch { art = null; }
            bool playing = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return new object[] { props.Title ?? "", props.Artist ?? "", s.SourceAppUserModelId ?? "", playing, art };
        }

        public static bool Toggle() { var s = Session(); return s != null && Wait(s.TryTogglePlayPauseAsync()); }
        public static bool Next() { var s = Session(); return s != null && Wait(s.TrySkipNextAsync()); }
        public static bool Prev() { var s = Session(); return s != null && Wait(s.TrySkipPreviousAsync()); }
    }
}
