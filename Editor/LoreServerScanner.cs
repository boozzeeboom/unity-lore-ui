using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProjectC.LoreUnity
{
    /// <summary>
    /// Scans local machine for Lore servers:
    /// - running loreserver processes
    /// - HTTP health check on common ports
    /// - config files in standard locations
    /// - repository remote URLs
    /// </summary>
    public static class LoreServerScanner
    {
        /// <summary>
        /// Represents a discovered Lore server candidate.
        /// </summary>
        public class ServerCandidate
        {
            /// <summary>Display name, e.g. "Local (running)" or "Config at C:\Users\..."</summary>
            public string DisplayName;
            /// <summary>HTTP URL like http://127.0.0.1:41339</summary>
            public string Url;
            /// <summary>true if health check returned 200</summary>
            public bool IsAlive;
            /// <summary>How this was discovered</summary>
            public DiscoverySource Source;
            /// <summary>Config file path (if discovered via config)</summary>
            public string ConfigPath;

            public enum DiscoverySource
            {
                Process,
                PortScan,
                ConfigFile,
                RepositoryRemote,
                InstallDirectory
            }
        }

        // Default Lore server ports to try
        private static readonly int[] DefaultPorts = { 41339, 41337, 41338, 41340, 8080, 8081 };

        // Standard install locations
        private static readonly string[] ConfigSearchPaths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoreForUnity", "config"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lore", "config"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoreForUnity"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lore"),
            @"Q:\lore-server\config",
            @"C:\lore-server\config",
        };

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        /// <summary>
        /// Run all scan methods and return unique candidates.
        /// </summary>
        public static async Task<List<ServerCandidate>> ScanAllAsync()
        {
            var candidates = new List<ServerCandidate>();

            var processTask = ScanProcessesAsync();
            var portTask = ScanPortsAsync();
            var configTask = ScanConfigFilesAsync();
            var repoTask = ScanRepositoryRemotesAsync();
            var installTask = ScanInstallDirectoryAsync();

            await Task.WhenAll(processTask, portTask, configTask, repoTask, installTask);

            candidates.AddRange(processTask.Result);
            candidates.AddRange(portTask.Result);
            candidates.AddRange(configTask.Result);
            candidates.AddRange(repoTask.Result);
            candidates.AddRange(installTask.Result);

            // Deduplicate by URL
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new List<ServerCandidate>();
            foreach (var c in candidates.OrderByDescending(c => c.IsAlive).ThenBy(c => c.Source))
            {
                if (!string.IsNullOrEmpty(c.Url) && seen.Add(c.Url))
                    unique.Add(c);
            }

            return unique;
        }

        // ── Process scan ──

        private static async Task<List<ServerCandidate>> ScanProcessesAsync()
        {
            var candidates = new List<ServerCandidate>();

            await Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcessesByName("loreserver");
                    foreach (var proc in processes)
                    {
                        try
                        {
                            // Try to read command line by using WMI query via process start info
                            // Fallback: just use default port
                            var url = "http://127.0.0.1:41339";
                            var alive = PingUrl(url).GetAwaiter().GetResult();

                            candidates.Add(new ServerCandidate
                            {
                                DisplayName = $"Process (PID {proc.Id}) — {url}" + (alive ? " ✓" : ""),
                                Url = url,
                                IsAlive = alive,
                                Source = ServerCandidate.DiscoverySource.Process,
                            });
                        }
                        catch { }
                        finally { proc.Dispose(); }
                    }
                }
                catch { }
            });

            return candidates;
        }

        // ── Port scan ──

        private static async Task<List<ServerCandidate>> ScanPortsAsync()
        {
            var candidates = new List<ServerCandidate>();
            var tasks = new List<Task>();

            foreach (var port in DefaultPorts)
            {
                var url = $"http://127.0.0.1:{port}";
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var alive = await PingUrl(url);
                        if (alive)
                        {
                            lock (candidates)
                            {
                                candidates.Add(new ServerCandidate
                                {
                                    DisplayName = $"{url} ✓",
                                    Url = url,
                                    IsAlive = true,
                                    Source = ServerCandidate.DiscoverySource.PortScan,
                                });
                            }
                        }
                    }
                    catch { }
                }));
            }

            await Task.WhenAll(tasks);
            return candidates;
        }

        // ── Config file scan ──

        private static async Task<List<ServerCandidate>> ScanConfigFilesAsync()
        {
            var candidates = new List<ServerCandidate>();

            await Task.Run(() =>
            {
                foreach (var dir in ConfigSearchPaths)
                {
                    try
                    {
                        if (!Directory.Exists(dir)) continue;

                        foreach (var file in Directory.GetFiles(dir, "*.toml", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                var content = File.ReadAllText(file);
                                var port = ExtractPortFromConfig(content);
                                var host = ExtractHostFromConfig(content) ?? "127.0.0.1";
                                var url = $"http://{host}:{port ?? 41339}";

                                var alive = PingUrl(url).GetAwaiter().GetResult();

                                candidates.Add(new ServerCandidate
                                {
                                    DisplayName = $"Config: {file}" + (alive ? " ✓" : " (stopped)"),
                                    Url = url,
                                    IsAlive = alive,
                                    Source = ServerCandidate.DiscoverySource.ConfigFile,
                                    ConfigPath = file,
                                });
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            });

            return candidates;
        }

        private static int? ExtractPortFromConfig(string content)
        {
            var match = Regex.Match(content,
                @"^(?:port|http_port|listen_port)\s*=\s*(\d+)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                return port;

            match = Regex.Match(content,
                @"\[server\][^\[]*port\s*=\s*(\d+)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out port))
                return port;

            return null;
        }

        private static string ExtractHostFromConfig(string content)
        {
            var match = Regex.Match(content,
                @"^(?:host|bind|listen_host|http_host)\s*=\s*""?([^""\s]+)""?",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        // ── Repository remote scan ──

        private static async Task<List<ServerCandidate>> ScanRepositoryRemotesAsync()
        {
            var candidates = new List<ServerCandidate>();

            try
            {
                var info = await LoreCliService.GetRepositoryInfoAsync();
                if (info != null && !string.IsNullOrEmpty(info.RemoteUrl))
                {
                    var httpUrl = info.RemoteUrl
                        .Replace("lore://", "http://")
                        .TrimEnd('/');

                    var alive = await PingUrl(httpUrl);

                    candidates.Add(new ServerCandidate
                    {
                        DisplayName = $"Repository remote: {info.RemoteUrl}" + (alive ? " ✓" : " (stopped)"),
                        Url = httpUrl,
                        IsAlive = alive,
                        Source = ServerCandidate.DiscoverySource.RepositoryRemote,
                    });
                }
            }
            catch { }

            return candidates;
        }

        // ── Install directory scan ──

        private static async Task<List<ServerCandidate>> ScanInstallDirectoryAsync()
        {
            var candidates = new List<ServerCandidate>();

            var installDir = LoreServerManager.InstallPath;
            try
            {
                if (Directory.Exists(installDir))
                {
                    var serverExe = Path.Combine(installDir, "loreserver.exe");
                    var configDir = Path.Combine(installDir, "config");

                    if (File.Exists(serverExe))
                    {
                        var defaultUrl = "http://127.0.0.1:41339";
                        var alive = await PingUrl(defaultUrl);

                        candidates.Add(new ServerCandidate
                        {
                            DisplayName = $"Install dir: {installDir}" + (alive ? " ✓" : " (stopped)"),
                            Url = defaultUrl,
                            IsAlive = alive,
                            Source = ServerCandidate.DiscoverySource.InstallDirectory,
                            ConfigPath = configDir,
                        });
                    }
                }
            }
            catch { }

            return candidates;
        }

        // ── Health check ──

        private static async Task<bool> PingUrl(string baseUrl)
        {
            try
            {
                var url = $"{baseUrl.TrimEnd('/')}/health_check";
                var response = await _httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
