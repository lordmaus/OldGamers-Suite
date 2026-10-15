using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TimerStudio
{
    [SupportedOSPlatform("windows")]
    public static class AlertSoundPlayer
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Beep(uint dwFreq, uint dwDuration);

        /// <summary>Play using built-in sound enum.</summary>
        public static void Play(AlertSound sound)
        {
            if (sound == AlertSound.Silent) return;
            Task.Run(() => PlaySync(sound))
                .ContinueWith(t => AppLog.Error("AlertSoundPlayer.Play", t.Exception!.GetBaseException()),
                    TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>Play a custom .wav or .mp3 file; falls back to built-in sound on error.</summary>
        /// <param name="volume">0.0–1.0. Applies to MediaPlayer playback only.</param>
        public static void PlayCustom(string filePath, AlertSound fallback = AlertSound.Beep,
            double volume = 1.0)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Play(fallback);
                return;
            }
            // MediaPlayer must run on an STA thread with its own Dispatcher pump.
            // The thread exits naturally when MediaEnded (or MediaFailed) calls
            // InvokeShutdown() — no outer Task.Run / Join needed.
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    var player = new MediaPlayer();
                    player.Volume = Math.Max(0.0, Math.Min(1.0, volume));

                    player.MediaOpened += (s, e) => player.Play();

                    player.MediaEnded += (s, e) =>
                    {
                        player.Close();
                        dispatcher.InvokeShutdown();
                    };

                    player.MediaFailed += (s, e) =>
                    {
                        Debug.WriteLine("[TimerStudio] MediaPlayer failed: " + e.ErrorException?.Message);
                        dispatcher.InvokeShutdown();
                        PlaySync(fallback);
                    };

                    player.Open(new Uri(filePath, UriKind.Absolute));

                    // Pumps the dispatcher until InvokeShutdown() is called above.
                    System.Windows.Threading.Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[TimerStudio] Custom sound error: " + ex.Message);
                    PlaySync(fallback);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            // No Join — thread is background and will exit via InvokeShutdown().
        }

        /// <summary>Play using model — picks custom file or built-in sound.</summary>
        public static void PlayForModel(TimerModel model)
        {
            double vol = Math.Max(0.0, Math.Min(1.0, model.AlertVolume));
            if (!string.IsNullOrEmpty(model.CustomSoundPath) &&
                File.Exists(model.CustomSoundPath))
                PlayCustom(model.CustomSoundPath, model.AlertSound, vol);
            else
                Play(model.AlertSound);  // built-in kernel32.Beep uses system volume
        }

        private static void PlaySync(AlertSound sound)
        {
            try
            {
                switch (sound)
                {
                    case AlertSound.Beep:
                        Beep(880, 120);
                        Beep(880, 120);
                        Beep(880, 220);
                        break;
                    case AlertSound.Chime:
                        Beep(1047, 200);
                        Beep(1319, 200);
                        Beep(1568, 420);
                        break;
                    case AlertSound.Alarm:
                        for (int i = 0; i < 6; i++) { Beep(i % 2 == 0 ? 700u : 900u, 120); }
                        break;
                    case AlertSound.Bell:
                        Beep(1175, 300);
                        Beep(1480, 260);
                        Beep(1760, 520);
                        break;
                    case AlertSound.Ping:
                        Beep(1500, 180);
                        Beep(1800, 120);
                        break;
                }
            }
            catch (Exception ex) { Debug.WriteLine("[TimerStudio] Sound error: " + ex.Message); }
        }
    }
}
