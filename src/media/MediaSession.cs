// Modulo opcional: "que esta sonando" con la API oficial de Windows (la misma del control de volumen de Windows 11).
// Da titulo, artista, portada y controles reales de Spotify, YouTube Music en el navegador, Apple Music, VLC...
// build.ps1 lo compila aparte contra los metadatos de Windows (WinMetadata) y lo mete dentro de MacDock.exe;
// si no se puede compilar en esa PC, MacDock usa el metodo anterior (titulo de la ventana y teclas multimedia).
// Todo se llama desde un hilo de fondo (las llamadas esperan a Windows).
using System;
using System.IO;
using Windows.Media.Control;

namespace MacDockMedia
{
    public static class Media
    {
        static GlobalSystemMediaTransportControlsSessionManager mgr;

        static GlobalSystemMediaTransportControlsSession Session()
        {
            if (mgr == null) mgr = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().Result;
            return mgr.GetCurrentSession();
        }

        // { titulo, artista, id de la app, reproduciendo (bool), portada (byte[] o null) } o null si no hay nada
        public static object[] Now()
        {
            var s = Session();
            if (s == null) return null;
            var props = s.TryGetMediaPropertiesAsync().AsTask().Result;
            var info = s.GetPlaybackInfo();
            byte[] art = null;
            try
            {
                if (props.Thumbnail != null)
                    using (var st = props.Thumbnail.OpenReadAsync().AsTask().Result)
                    using (var ns = st.AsStreamForRead())
                    using (var ms = new MemoryStream()) { ns.CopyTo(ms); art = ms.ToArray(); }
            }
            catch { art = null; }
            bool playing = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return new object[] { props.Title ?? "", props.Artist ?? "", s.SourceAppUserModelId ?? "", playing, art };
        }

        public static bool Toggle() { var s = Session(); return s != null && s.TryTogglePlayPauseAsync().AsTask().Result; }
        public static bool Next() { var s = Session(); return s != null && s.TrySkipNextAsync().AsTask().Result; }
        public static bool Prev() { var s = Session(); return s != null && s.TrySkipPreviousAsync().AsTask().Result; }
    }
}
