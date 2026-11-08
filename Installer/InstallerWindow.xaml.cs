using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace LegendSetup
{
    public partial class InstallerWindow : Window
    {
        const string AppName = "Legend";
        const string Version = "1.0.0";

        readonly string installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        readonly string appDir;
        readonly string appExe;
        readonly bool uninstall;
        readonly bool noLaunch;

        public InstallerWindow()
        {
            InitializeComponent();
            appDir = Path.Combine(installRoot, "app");
            appExe = Path.Combine(appDir, AppName + ".exe");
            uninstall = Environment.GetCommandLineArgs().Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
            noLaunch = Environment.GetCommandLineArgs().Any(a => a.Equals("--no-launch", StringComparison.OrdinalIgnoreCase));

            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            if (uninstall)
            {
                Title = "Legend Uninstall";
                Subtitle.Text = "Removing from this user";
                Footer.Text = "Shortcuts and installed files are removed.";
            }

            Loaded += async (s, e) => await RunAsync();
        }

        void Report(double value, string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Report(value, text)));
                return;
            }
            Progress.Value = value;
            Status.Text = text;
        }

        async Task RunAsync()
        {
            try
            {
                if (uninstall) await Task.Run(Uninstall);
                else await Task.Run(Install);
            }
            catch (Exception ex)
            {
                Status.Text = "Installation failed.";
                Footer.Text = ex.Message;
                MessageBox.Show(this, ex.Message, "Legend Setup", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        void Install()
        {
            Report(5, "Checking previous installation...");
            if (Directory.Exists(appDir))
            {
                try { Directory.Delete(appDir, true); }
                catch (Exception ex)
                {
                    throw new IOException("Could not replace the old installation. Close Legend and run the setup again.", ex);
                }
            }
            Directory.CreateDirectory(appDir);

            Report(15, "Extracting application...");
            ExtractPayload(p => Report(15 + p * 65, "Extracting application..."));

            Report(82, "Creating shortcuts...");
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
            CreateShortcut(desktop);
            CreateShortcut(startMenu);

            Report(90, "Registering uninstaller...");
            string setupCopy = Path.Combine(installRoot, "LegendSetup.exe");
            try
            {
                string self = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(self) && !string.Equals(self, setupCopy, StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, setupCopy, true);
            }
            catch (Exception) { }

            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", AppName);
                key.SetValue("DisplayIcon", appExe);
                key.SetValue("InstallLocation", installRoot);
                key.SetValue("UninstallString", "\"" + setupCopy + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            Report(100, "Installed. Starting Legend...");

            if (!File.Exists(appExe))
                throw new FileNotFoundException("The application was not extracted correctly. Rebuild the setup with build-installer.ps1.");

            if (!noLaunch)
                Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true, WorkingDirectory = appDir });

            Dispatcher.BeginInvoke(new Action(Close));        }

        void ExtractPayload(Action<double> onProgress)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
            if (stream == null)
                throw new InvalidOperationException("This setup has no payload. Run build-installer.ps1 to produce a complete installer.");

            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            string prefix = appDir + Path.DirectorySeparatorChar;
            int total = Math.Max(1, zip.Entries.Count);
            int index = 0;

            foreach (var entry in zip.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(appDir, entry.FullName));
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }

                index++;
                if (index % 8 == 0) onProgress((double)index / total);
            }
            onProgress(1);
        }

        void CreateShortcut(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Windows Script Host is unavailable; cannot create shortcuts.");

            dynamic shell = Activator.CreateInstance(shellType);
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = appExe;
            link.WorkingDirectory = appDir;
            link.IconLocation = appExe + ",0";
            link.Description = "Legend — Lua executor for Roblox";
            link.Save();
            try { ShortcutAppId.Apply(path); } catch (Exception) { }
        }

        void Uninstall()
        {
            Report(10, "Removing shortcuts...");
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"));

            Report(40, "Removing registry entries...");
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName, false); }
            catch (Exception) { }

            if (Directory.Exists(installRoot))
            {
                foreach (var process in Process.GetProcessesByName(AppName))
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try { process.Kill(true); } catch (Exception) { }
                }
            }

            Report(100, "Removed.");
            string script = "/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"" + installRoot + "\"";
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", script)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception) { }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                Footer.Text = "Legend has been removed from this user.";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                timer.Tick += (s, e) => { timer.Stop(); Close(); };
                timer.Start();
            }));
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}
