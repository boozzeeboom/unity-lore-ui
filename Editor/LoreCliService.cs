using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ProjectC.LoreUnity
{
    /// <summary>
    /// Async bridge to lore CLI. All methods run on a background thread
    /// and return results via Task. Use EditorApplication.delayCall to
    /// update UI from results.
    /// </summary>
    public static class LoreCliService
    {
        /// <summary>
        /// Try to find lore.exe in PATH environment variable.
        /// Returns full path or null.
        /// </summary>
        public static string FindLoreInPath()
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var full = Path.Combine(dir.Trim(), "lore.exe");
                    if (File.Exists(full)) return full;
                    // Also check without .exe (Unix/WSL compat)
                    var noExt = Path.Combine(dir.Trim(), "lore");
                    if (File.Exists(noExt)) return noExt;
                }
                catch
                {
                    // skip inaccessible dirs
                }
            }
            return null;
        }

        /// <summary>
        /// Resolve the actual lore executable path.
        /// Priority: Settings > PATH > null.
        /// </summary>
        public static string ResolveLorePath()
        {
            if (!string.IsNullOrEmpty(LoreSettings.LoreExePath) && File.Exists(LoreSettings.LoreExePath))
                return LoreSettings.LoreExePath;

            return FindLoreInPath();
        }

        /// <summary>
        /// Find loreserver.exe. Priority: Settings > next to lore.exe > PATH.
        /// </summary>
        public static string ResolveServerPath()
        {
            if (!string.IsNullOrEmpty(LoreSettings.ServerExePath) && File.Exists(LoreSettings.ServerExePath))
                return LoreSettings.ServerExePath;

            // Next to lore.exe
            var lorePath = ResolveLorePath();
            if (!string.IsNullOrEmpty(lorePath))
            {
                var loreDir = Path.GetDirectoryName(lorePath);
                var serverPath = Path.Combine(loreDir, "loreserver.exe");
                if (File.Exists(serverPath)) return serverPath;
            }

            // In PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var full = Path.Combine(dir.Trim(), "loreserver.exe");
                    if (File.Exists(full)) return full;
                }
                catch { }
            }

            return null;
        }

        // ── Core execution ──

        /// <summary>
        /// Execute Lore CLI with given arguments. Runs on background thread.
        /// Returns (exitCode, stdout+stderr).
        /// </summary>
        public static async Task<(int ExitCode, string Output)> ExecuteAsync(params string[] args)
        {
            return await Task.Run(() => Execute(args));
        }

        /// <summary>
        /// Synchronous execution (used from background thread).
        /// </summary>
        private static (int, string) Execute(string[] args)
        {
            var lorePath = ResolveLorePath();
            if (string.IsNullOrEmpty(lorePath))
                return (-1, "[ERROR] lore.exe not found. Configure path in Edit → Preferences → Lore.");

            var repoPath = LoreSettings.RepoPath;
            if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath))
                return (-1, "[ERROR] Repository path not found: " + repoPath);

            // Build args: always set --repository and --non-interactive
            var fullArgs = new List<string>(args);
            // If args already contain --repository, don't add again
            if (!args.Any(a => a.StartsWith("--repository")))
            {
                fullArgs.Insert(0, "--repository");
                fullArgs.Insert(1, repoPath);
            }
            fullArgs.Add("--non-interactive");

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = lorePath,
                    Arguments = string.Join(" ", fullArgs.Select(EscapeArg)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new System.Text.UTF8Encoding(false),
                    StandardErrorEncoding = new System.Text.UTF8Encoding(false)
                };

                var sw = Stopwatch.StartNew();
                process.Start();

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();

                process.WaitForExit(120000); // 2 min timeout
                sw.Stop();

                var combined = (stdout + "\n" + stderr).Trim();
                return (process.ExitCode, combined);
            }
            catch (Exception ex)
            {
                return (-1, $"[ERROR] Failed to execute lore.exe: {ex.Message}");
            }
        }

        private static string EscapeArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.Contains(' ') || arg.Contains('"'))
                return "\"" + arg.Replace("\"", "\\\"") + "\"";
            return arg;
        }

        // ── High-level commands ──

        public static async Task<LoreStatus> GetStatusAsync()
        {
            var (code, output) = await ExecuteAsync("status", "--scan");
            if (code != 0)
            {
                UnityEngine.Debug.LogWarning($"[Lore] status failed (exit {code}): {output}");
                return null;
            }
            var status = LoreCliParser.ParseStatus(output);
            if (status == null)
                UnityEngine.Debug.LogWarning($"[Lore] Failed to parse status output:\n{output}");
            return status;
        }

        public static async Task<List<LoreCommit>> GetHistoryAsync(int count = 20)
        {
            var (code, output) = await ExecuteAsync("history", count.ToString());
            if (code != 0) return new List<LoreCommit>();

            var commits = LoreCliParser.ParseHistory(output);
            // Mark unpushed
            var (code2, statusOut) = await ExecuteAsync("status", "--revision-only");
            if (code2 == 0)
            {
                var status = LoreCliParser.ParseStatus(statusOut);
                if (status != null)
                {
                    foreach (var c in commits)
                    {
                        c.IsUnpushed = status.RemoteRevision > 0 && c.RevisionNumber > status.RemoteRevision;
                    }
                }
            }

            return commits;
        }

        public static async Task<List<string>> GetHistoryOnelineAsync(int count = 20)
        {
            var (code, output) = await ExecuteAsync("history", "--oneline", count.ToString());
            if (code != 0) return new List<string>();

            return output
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();
        }

        public static async Task<List<LoreBranch>> GetBranchesAsync()
        {
            var (code, output) = await ExecuteAsync("branch", "list");
            if (code != 0) return new List<LoreBranch>();

            return LoreCliParser.ParseBranchList(output);
        }

        public static async Task<LoreCommit> GetRevisionInfoAsync(string hash)
        {
            var (code, output) = await ExecuteAsync("revision", "info", hash);
            if (code != 0) return null;

            return LoreCliParser.ParseRevisionInfo(output);
        }

        public static async Task<string> GetDiffAsync(string path = null, string source = null, string target = null)
        {
            var args = new List<string> { "diff" };
            if (source != null) { args.Add("--source"); args.Add(source); }
            if (target != null) { args.Add("--target"); args.Add(target); }
            if (path != null) args.Add(path);

            var (code, output) = await ExecuteAsync(args.ToArray());
            return code == 0 ? output : null;
        }

        public static async Task<(bool Success, string Output)> StageAllAsync()
        {
            var (code, output) = await ExecuteAsync("stage", "--scan", ".");
            return (code == 0, output);
        }

        public static async Task<(bool Success, string Output)> UnstageAsync(string path)
        {
            var (code, output) = await ExecuteAsync("unstage", path);
            return (code == 0, output);
        }

        public static async Task<(bool Success, string Output)> CommitAsync(string message)
        {
            var (code, output) = await ExecuteAsync("commit", message);
            return (code == 0, output);
        }

        public static async Task<(bool Success, string Output)> PushAsync(string branch = null)
        {
            var args = new List<string> { "push" };
            if (branch != null) args.Add(branch);
            var (code, output) = await ExecuteAsync(args.ToArray());
            return (code == 0, output);
        }

        public static async Task<(bool Success, string Output)> SyncAsync(string hash = null)
        {
            var args = new List<string> { "sync" };
            if (hash != null) args.Add(hash);
            var (code, output) = await ExecuteAsync(args.ToArray());
            return (code == 0, output);
        }

        public static async Task<(bool Success, string Output)> CreateBranchAsync(string name)
        {
            var (code, output) = await ExecuteAsync("branch", "create", name);
            return (code == 0, output);
        }

        public static async Task<(bool Success, string Output)> SwitchBranchAsync(string name)
        {
            var (code, output) = await ExecuteAsync("branch", "switch", name);
            return (code == 0, output);
        }

        public static async Task<(bool Success, string Output)> MergeBranchAsync(string name)
        {
            var (code, output) = await ExecuteAsync("branch", "merge", name);
            return (code == 0, output);
        }

        public static async Task<(bool Success, string Output)> RevertRevisionAsync(string hash)
        {
            var (code, output) = await ExecuteAsync("revision", "revert", hash);
            return (code == 0, output);
        }

        public static async Task<LoreRepositoryInfo> GetRepositoryInfoAsync()
        {
            var (code, output) = await ExecuteAsync("repository", "info");
            if (code != 0) return null;
            return LoreCliParser.ParseRepositoryInfo(output);
        }

        /// <summary>
        /// Verify Lore CLI works. Returns true if `lore status` succeeds.
        /// </summary>
        public static async Task<bool> HealthCheckAsync()
        {
            var (code, output) = await ExecuteAsync("status", "--revision-only");
            return code == 0;
        }
    }
}
