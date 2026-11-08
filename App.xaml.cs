using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Legend
{
    public partial class App : Application
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [DllImport("user32.dll")]
        static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        const int SW_RESTORE = 9;

        public const string AppUserModelId = "Legend.Legend.1";

        static readonly Mutex Single = new Mutex(true, "Legend.Exec.SingleInstance");

        static string LogPath => Path.Combine(Path.GetTempPath(), "legend-update.log");

        static void Log(string text)
        {
            try { File.AppendAllText(LogPath, "\r\n" + DateTime.Now.ToString("HH:mm:ss") + " " + text); } catch { }
        }

        public App()
        {
            DispatcherUnhandledException += (s, e) => Log("crash: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("domain crash: " + e.ExceptionObject);
        }

        static void FocusRunning()
        {
            foreach (var p in Process.GetProcessesByName("Legend"))
            {
                if (p.Id == Environment.ProcessId) continue;
                IntPtr h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                ShowWindowAsync(h, SW_RESTORE);
                SetForegroundWindow(h);
                return;
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            if (!Single.WaitOne(0, false))
            {
                FocusRunning();
                Shutdown();
                return;
            }

            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            base.OnStartup(e);

            try
            {
                var info = await Updater.CheckAsync();
                if (info != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { new UpdaterWindow(info).Show(); }
                        catch (Exception ex) { Log("window: " + ex); }
                    }));
                }
            }
            catch (Exception ex) { Log("startup: " + ex); }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { Single.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
