using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Legend
{
    public sealed class UpdateInfo
    {
        public string Version { get; set; }
        public string Zip { get; set; }
        public string Sha256 { get; set; }
        public string[] Notes { get; set; } = Array.Empty<string>();
        public string Page { get; set; }
    }

    public static class Updater
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public static string CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        // update.json lives next to Legend.exe: { "manifest": "https://host/manifest.json" }
        public static string ManifestUrl()
        {
            try
            {
                string cfg = Path.Combine(AppContext.BaseDirectory, "update.json");
                if (!File.Exists(cfg)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
                return doc.RootElement.TryGetProperty("manifest", out var m) ? m.GetString() : null;
            }
            catch { return null; }
        }

        // Returns null when up to date, the install is unmanaged, or the host is unreachable.
        public static async Task<UpdateInfo> CheckAsync()
        {
            string url = ManifestUrl();
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                string raw = await Http.GetStringAsync(url).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<UpdateInfo>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (info == null || string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.Zip)) return null;

                if (!Version.TryParse(info.Version, out var remote) || !Version.TryParse(CurrentVersion, out var local)) return null;
                return remote > local ? info : null;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "legend-update.log"), url + "\r\n" + ex); } catch { }
                return null;
            }
        }

        public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress)
        {
            string dir = Path.Combine(Path.GetTempPath(), "legend-update");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            string zipPath = Path.Combine(dir, "update.zip");
            using (var response = await Http.GetAsync(info.Zip, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var target = File.Create(zipPath);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            if (!string.IsNullOrWhiteSpace(info.Sha256))
            {
                using var sha = SHA256.Create();
                await using var stream = File.OpenRead(zipPath);
                string hash = Convert.ToHexString(await sha.ComputeHashAsync(stream).ConfigureAwait(false));
                if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update archive checksum mismatch.");
            }

            string staging = Path.Combine(dir, "staging");
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);
            return staging;
        }

        // Hands the swap to a detached PowerShell that outlives this process.
        public static void Apply(string stagingDir)
        {
            string dir = Path.Combine(Path.GetTempPath(), "legend-update");
            string script = Path.Combine(dir, "apply.ps1");
            string appDir = AppContext.BaseDirectory.TrimEnd('\\');
            string exe = Path.Combine(appDir, "Legend.exe");

            File.WriteAllText(script, string.Join("\r\n", new[]
            {
                "$ErrorActionPreference = 'SilentlyContinue'",
                "Wait-Process -Id " + Environment.ProcessId + " -Timeout 120",
                "Start-Sleep -Milliseconds 800",
                "Copy-Item -Path '" + stagingDir.Replace("'", "''") + "\\*' -Destination '" + appDir.Replace("'", "''") + "' -Recurse -Force",
                "Start-Process -FilePath '" + exe.Replace("'", "''") + "'",
                "Start-Sleep -Seconds 2",
                "Remove-Item -Path '" + dir.Replace("'", "''") + "' -Recurse -Force"
            }));

            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
    }
}
