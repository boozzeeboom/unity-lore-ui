using UnityEditor;
using UnityEngine;

namespace ProjectC.LoreUnity
{
    /// <summary>
    /// EditorPrefs-backed settings for Lore VCS integration.
    /// Stored per-user (not in project) — each developer configures independently.
    /// </summary>
    public static class LoreSettings
    {
        private const string Prefix = "LoreUnity_";

        // ── Keys ──

        private const string KeyLorePath = Prefix + "LoreExePath";
        private const string KeyServerPath = Prefix + "ServerExePath";
        private const string KeyServerUrl = Prefix + "ServerUrl";
        private const string KeyRepoPath = Prefix + "RepoPath";
        private const string KeyAutoStart = Prefix + "AutoStartServer";
        private const string KeyRefreshInterval = Prefix + "RefreshInterval";
        private const string KeyFirstRun = Prefix + "FirstRunDone";

        // ── Defaults ──

        private const string DefaultServerUrl = "http://127.0.0.1:41339";
        private const int DefaultRefreshInterval = 30;
        private const bool DefaultAutoStart = true;

        // ── Properties ──

        public static string LoreExePath
        {
            get => EditorPrefs.GetString(KeyLorePath, "");
            set => EditorPrefs.SetString(KeyLorePath, value);
        }

        public static string ServerExePath
        {
            get => EditorPrefs.GetString(KeyServerPath, "");
            set => EditorPrefs.SetString(KeyServerPath, value);
        }

        public static string ServerUrl
        {
            get => EditorPrefs.GetString(KeyServerUrl, DefaultServerUrl);
            set => EditorPrefs.SetString(KeyServerUrl, value);
        }

        public static string RepoPath
        {
            get => EditorPrefs.GetString(KeyRepoPath, GetDefaultRepoPath());
            set => EditorPrefs.SetString(KeyRepoPath, value);
        }

        public static bool AutoStartServer
        {
            get => EditorPrefs.GetBool(KeyAutoStart, DefaultAutoStart);
            set => EditorPrefs.SetBool(KeyAutoStart, value);
        }

        public static int RefreshInterval
        {
            get => EditorPrefs.GetInt(KeyRefreshInterval, DefaultRefreshInterval);
            set => EditorPrefs.SetInt(KeyRefreshInterval, value);
        }

        public static bool FirstRunDone
        {
            get => EditorPrefs.GetBool(KeyFirstRun, false);
            set => EditorPrefs.SetBool(KeyFirstRun, value);
        }

        // ── Helpers ──

        private static string GetDefaultRepoPath()
        {
            // Default to parent of project's Assets folder
            var assetPath = Application.dataPath; // e.g. "Q:/.../ProjectC_client/Assets"
            var parent = System.IO.Path.GetDirectoryName(assetPath); // up one level
            return parent ?? assetPath;
        }

        /// <summary>
        /// Validate that lore.exe exists at the configured path (or in PATH).
        /// Returns true if found.
        /// </summary>
        public static bool ValidateLorePath()
        {
            if (string.IsNullOrEmpty(LoreExePath))
                return LoreCliService.FindLoreInPath() != null;

            return System.IO.File.Exists(LoreExePath);
        }

        /// <summary>
        /// Validate that loreserver.exe exists at the configured path.
        /// </summary>
        public static bool ValidateServerPath()
        {
            if (string.IsNullOrEmpty(ServerExePath))
                return false;

            return System.IO.File.Exists(ServerExePath);
        }

        /// <summary>
        /// Reset all settings to defaults (does not clear RepoPath).
        /// </summary>
        public static void Reset()
        {
            LoreExePath = "";
            ServerExePath = "";
            ServerUrl = DefaultServerUrl;
            AutoStartServer = DefaultAutoStart;
            RefreshInterval = DefaultRefreshInterval;
        }

        /// <summary>
        /// Get a useful display string for the current lore executable.
        /// </summary>
        public static string LoreExeDisplay
        {
            get
            {
                if (!string.IsNullOrEmpty(LoreExePath))
                    return LoreExePath;
                var found = LoreCliService.FindLoreInPath();
                return found ?? "(not found)";
            }
        }
    }
}
