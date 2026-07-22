using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

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
            // %LOCALAPPDATA%/LoreForUnity/config/
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoreForUnity", "config"),
            // %APPDATA%/Lore/config/
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lore", "config"),
            // next to installed lore.exe in standard locations
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoreForUnity"),
            // Home directory
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lore"),
            // Q: lore-server config (Project C specific)
            @"Q:\lore-server\config",
            // C: lore-server config
            @"C:\lore-server\config",
        };

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        /// <summary>
        /// Run all scan methods and return unique candidates.
        /// </summary>
        public static async Task<List<ServerCandidate>> ScanAllAsync()
        {
            var candidates = new List<ServerCandidate>();

            // Run all scans in parallel
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

        /// <summary>
        /// Find running loreserver processes and extract port from command line.
        /// </summary>
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
                            var cmdLine = GetProcessCommandLine(proc.Id);
                            var port = ExtractPortFromCommandLine(cmdLine);

                            var url = port.HasValue
                                ? $"http://127.0.0.1:{port.Value}"
                                : "http://127.0.0.1:41339";

                            // Check if actually alive
                            var alive = PingUrl(url).GetAwaiter().GetResult();

                            candidates.Add(new ServerCandidate
                            {
                                DisplayName = $"Process (PID {proc.Id}) — {url}" + (alive ? " ✓" : ""),
                                Url = url,
                                IsAlive = alive,
                                Source = ServerCandidate.DiscoverySource.Process,
                            });
                        }
                        catch
                        {
                            // skip inaccessible processes
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch
                {
                    // GetProcessesByName can fail on some systems
                }
            });

            return candidates;
        }

        /// <summary>
        /// Extract port number from loreserver command line arguments.
        /// </summary>
        private static int? ExtractPortFromCommandLine(string cmdLine)
        {
            if (string.IsNullOrEmpty(cmdLine)) return null;

            // Look for --port <number> or -p <number>
            var portMatch = System.Text.RegularExpressions.Regex.Match(cmdLine, @"--port\s+(\d+)");
            if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out var port))
                return port;

            // Some configs use --http-port
            portMatch = System.Text.RegularExpressions.Regex.Match(cmdLine, @"--http-port\s+(\d+)");
            if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out port))
                return port;

            return null;
        }

        /// <summary>
        /// Get command line of a process by PID (Windows-only via WMI).
        /// </summary>
        private static string GetProcessCommandLine(int pid)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                foreach (var obj in searcher.Get())
                {
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            catch
            {
                // WMI not available
            }
            return "";
        }

        // ── Port scan ──

        /// <summary>
        /// Try health check on default ports and common alternatives.
        /// </summary>
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
                    catch
                    {
                        // port unreachable
                    }
                }));
            }

            await Task.WhenAll(tasks);
            return candidates;
        }

        // ── Config file scan ──

        /// <summary>
        /// Scan standard config directories for loreserver config files.
        /// </summary>
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

                        // Look for config.toml, server.toml, or any .toml
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
                            catch
                            {
                                // skip unreadable configs
                            }
                        }
                    }
                    catch
                    {
                        // skip inaccessible dirs
                    }
                }
            });

            return candidates;
        }

        private static int? ExtractPortFromConfig(string content)
        {
            var match = System.Text.RegularExpressions.Regex.Match(content,
                @"^(?:port|http_port|listen_port)\s*=\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                return port;

            // Also check [server] section
            match = System.Text.RegularExpressions.Regex.Match(content,
                @"\[server\][^\[]*port\s*=\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out port))
                return port;

            return null;
        }

        private static string ExtractHostFromConfig(string content)
        {
            var match = System.Text.RegularExpressions.Regex.Match(content,
                @"^(?:host|bind|listen_host|http_host)\s*=\s*""?([^""\s]+)""?",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        // ── Repository remote scan ──

        /// <summary>
        /// Read `lore repository info` and extract remote URL.
        /// </summary>
        private static async Task<List<ServerCandidate>> ScanRepositoryRemotesAsync()
        {
            var candidates = new List<ServerCandidate>();

            try
            {
                var info = await LoreCliService.GetRepositoryInfoAsync();
                if (info != null && !string.IsNullOrEmpty(info.RemoteUrl))
                {
                    // Convert lore://host:port to http://host:port
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
            catch
            {
                // repo not available
            }

            return candidates;
        }

        // ── Install directory scan ──

        /// <summary>
        /// Check the standard install directory for config.
        /// </summary>
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
            catch
            {
                // skip
            }

            return candidates;
        }

        // ── Health check ──

        /// <summary>
        /// Ping a URL's /health_check endpoint. Returns true if 200.
        /// </summary>
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
