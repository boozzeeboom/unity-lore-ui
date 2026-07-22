using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ProjectC.LoreUnity
{
    /// <summary>
    /// Manages Lore server lifecycle: installation, start, stop, health check.
    /// Server runs as a background process; killed when Unity exits.
    /// </summary>
    [InitializeOnLoad]
    public static class LoreServerManager
    {
        private static Process _serverProcess;
        private static HttpClient _httpClient;
        private static bool _isStarting;

        /// <summary>
        /// Fired when server status changes.
        /// </summary>
        public static event Action<bool> OnServerStatusChanged;

        public static bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

        static LoreServerManager()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            EditorApplication.quitting += OnEditorQuitting;

            // Auto-start on first run if configured
            if (!LoreSettings.FirstRunDone)
            {
                // Don't auto-start until first-run wizard completes
            }
            else if (LoreSettings.AutoStartServer)
            {
                EditorApplication.delayCall += () => _ = StartServerAsync();
            }
        }

        // ── Install ──

        /// <summary>
        /// Install directory for Lore binaries: %LOCALAPPDATA%/LoreForUnity/
        /// </summary>
        public static string InstallPath
        {
            get
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "LoreForUnity");
            }
        }

        /// <summary>
        /// Download latest Lore release from GitHub and extract into InstallPath.
        /// </summary>
        public static async Task<(bool Success, string Message)> DownloadAndInstallAsync()
        {
            try
            {
                var installDir = InstallPath;
                Directory.CreateDirectory(installDir);

                // GitHub API to get latest release download URL
                // This uses the releases API — replace URL with actual latest release
                const string releasesUrl =
                    "https://api.github.com/repos/EpicGames/lore/releases/latest";

                var request = new HttpRequestMessage(HttpMethod.Get, releasesUrl);
                request.Headers.Add("User-Agent", "unity-lore-ui");
                request.Headers.Add("Accept", "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();

                // Extract download URL for Windows zip
                var assetPattern = "\"browser_download_url\":\\s*\"([^\"]+-windows[^\"]*\\.zip)\"";
                var match = System.Text.RegularExpressions.Regex.Match(json, assetPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                string downloadUrl;
                if (match.Success)
                {
                    downloadUrl = match.Groups[1].Value;
                }
                else
                {
                    // Fallback: take first zip asset
                    var fallbackMatch = System.Text.RegularExpressions.Regex.Match(json,
                        "\"browser_download_url\":\\s*\"([^\"]*\\.zip)\"");
                    if (!fallbackMatch.Success)
                        return (false, "Could not find download URL in GitHub release response.");
                    downloadUrl = fallbackMatch.Groups[1].Value;
                }

                var zipPath = Path.Combine(installDir, "lore-release.zip");

                UnityEngine.Debug.Log($"[Lore] Downloading from {downloadUrl}...");

                var dlRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                dlRequest.Headers.Add("User-Agent", "unity-lore-ui");

                using var dlResponse = await _httpClient.SendAsync(dlRequest);
                dlResponse.EnsureSuccessStatusCode();

                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
                await dlResponse.Content.CopyToAsync(fs);
                await fs.FlushAsync();

                UnityEngine.Debug.Log($"[Lore] Extracting to {installDir}...");
                ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);

                File.Delete(zipPath);

                // Set paths
                var loreExe = Path.Combine(installDir, "lore.exe");
                var serverExe = Path.Combine(installDir, "loreserver.exe");

                if (File.Exists(loreExe))
                    LoreSettings.LoreExePath = loreExe;
                if (File.Exists(serverExe))
                    LoreSettings.ServerExePath = serverExe;

                UnityEngine.Debug.Log($"[Lore] Installation complete. lore.exe: {File.Exists(loreExe)}, loreserver.exe: {File.Exists(serverExe)}");
                return (true, installDir);
            }
            catch (Exception ex)
            {
                return (false, $"Installation failed: {ex.Message}");
            }
        }

        // ── Start / Stop ──

        /// <summary>
        /// Start loreserver.exe. Returns true if server started successfully.
        /// </summary>
        public static async Task<bool> StartServerAsync()
        {
            if (IsRunning) return true;
            if (_isStarting) return false;
            _isStarting = true;

            try
            {
                var serverPath = LoreCliService.ResolveServerPath();
                if (string.IsNullOrEmpty(serverPath) || !File.Exists(serverPath))
                {
                    UnityEngine.Debug.LogWarning("[Lore] Server executable not found. Run Lore → Install Server first.");
                    return false;
                }

                // Build config directory path
                var configDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LoreForUnity", "config");
                Directory.CreateDirectory(configDir);

                var startInfo = new ProcessStartInfo
                {
                    FileName = serverPath,
                    Arguments = $"--config \"{configDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _serverProcess = new Process { StartInfo = startInfo };
                _serverProcess.Start();

                // Wait for health check
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(1000);
                    if (await HealthCheckAsync())
                    {
                        _isStarting = false;
                        OnServerStatusChanged?.Invoke(true);
                        UnityEngine.Debug.Log("[Lore] Server started successfully.");
                        return true;
                    }
                }

                UnityEngine.Debug.LogWarning("[Lore] Server process started but health check failed.");
                _isStarting = false;
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Lore] Failed to start server: {ex.Message}");
                _isStarting = false;
                return false;
            }
        }

        /// <summary>
        /// Stop the server process.
        /// </summary>
        public static void StopServer()
        {
            if (_serverProcess == null || _serverProcess.HasExited) return;

            try
            {
                _serverProcess.Kill();
                _serverProcess.WaitForExit(5000);
                _serverProcess.Dispose();
                _serverProcess = null;
                OnServerStatusChanged?.Invoke(false);
                UnityEngine.Debug.Log("[Lore] Server stopped.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Lore] Error stopping server: {ex.Message}");
            }
        }

        // ── Health check ──

        /// <summary>
        /// Check if loreserver is responding via HTTP health endpoint.
        /// </summary>
        public static async Task<bool> HealthCheckAsync()
        {
            try
            {
                var url = LoreSettings.ServerUrl;
                if (string.IsNullOrEmpty(url)) return false;

                var response = await _httpClient.GetAsync($"{url.TrimEnd('/')}/health_check");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check server status synchronously (for UI toolbar indicator).
        /// Returns cached state if async would block.
        /// </summary>
        public static bool HealthCheckSync()
        {
            try
            {
                var url = LoreSettings.ServerUrl;
                if (string.IsNullOrEmpty(url)) return false;
                var task = _httpClient.GetAsync($"{url.TrimEnd('/')}/health_check");
                return task.Wait(2000) && task.Result.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // ── Lifecycle ──

        private static void OnEditorQuitting()
        {
            StopServer();
            _httpClient?.Dispose();
        }

        /// <summary>
        /// Return server version info. Currently reads from health endpoint or process.
        /// </summary>
        public static string GetServerInfo()
        {
            if (IsRunning) return $"Running at {LoreSettings.ServerUrl}";
            return "Stopped";
        }
    }
}
