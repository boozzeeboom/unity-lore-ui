using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectC.LoreUnity
{
    /// <summary>
    /// Settings window: Edit → Preferences → Lore.
    /// All values stored in EditorPrefs via LoreSettings.
    /// </summary>
    public class LoreConfigWindow : EditorWindow
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Lore", SettingsScope.User)
            {
                label = "Lore VCS",
                keywords = new[] { "Lore", "VCS", "Version Control", "Server" },
                activateHandler = (searchContext, rootElement) =>
                {
                    var prefs = new VisualElement
                    {
                        style = { padding = 10 }
                    };

                    // ── Lore Executable ──
                    prefs.Add(new Label("Lore Executable") { style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold } });
                    prefs.Add(new Label("Path to lore.exe. Leave empty to search PATH.") { style = { fontSize = 10, color = Color.gray } });

                    var lorePathField = new TextField { value = LoreSettings.LoreExePath };
                    lorePathField.RegisterValueChangedCallback(evt => LoreSettings.LoreExePath = evt.newValue);
                    prefs.Add(lorePathField);

                    var loreFindBtn = new Button(() =>
                    {
                        var found = LoreCliService.FindLoreInPath();
                        if (found != null)
                        {
                            LoreSettings.LoreExePath = found;
                            lorePathField.value = found;
                            EditorUtility.DisplayDialog("Lore Found", $"Found at:\n{found}", "OK");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Lore Not Found",
                                "lore.exe not found in PATH. Download from:\n" +
                                "https://github.com/EpicGames/lore/releases", "OK");
                        }
                    }) { text = "Search PATH" };
                    prefs.Add(loreFindBtn);

                    prefs.Add(new VisualElement { style = { height = 10 } });

                    // ── Server Executable ──
                    prefs.Add(new Label("Server Executable") { style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold } });
                    prefs.Add(new Label("Path to loreserver.exe.") { style = { fontSize = 10, color = Color.gray } });

                    var serverPathField = new TextField { value = LoreSettings.ServerExePath };
                    serverPathField.RegisterValueChangedCallback(evt => LoreSettings.ServerExePath = evt.newValue);
                    prefs.Add(serverPathField);

                    prefs.Add(new VisualElement { style = { height = 10 } });

                    // ── Server URL ──
                    prefs.Add(new Label("Server URL") { style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold } });

                    var serverUrlField = new TextField { value = LoreSettings.ServerUrl };
                    serverUrlField.RegisterValueChangedCallback(evt => LoreSettings.ServerUrl = evt.newValue);
                    prefs.Add(serverUrlField);

                    var scanBtnRow = new VisualElement
                    {
                        style = { flexDirection = FlexDirection.Row, marginTop = 4 }
                    };
                    var scanBtn = new Button(async () =>
                    {
                        scanBtn.text = "Scanning...";
                        scanBtn.SetEnabled(false);

                        var servers = await LoreServerScanner.ScanAllAsync();
                        if (servers.Count == 0)
                        {
                            EditorUtility.DisplayDialog("Scan Complete",
                                "No Lore servers found on this machine.", "OK");
                        }
                        else
                        {
                            var msg = $"Found {servers.Count} server(s):\n\n";
                            foreach (var s in servers)
                            {
                                msg += $"  {(s.IsAlive ? "●" : "○")}  {s.Url}\n";
                                msg += $"       ({s.Source})\n\n";
                            }

                            var choice = EditorUtility.DisplayDialogComplex("Lore Servers Found",
                                msg, "Use First", "Cancel", "");

                            if (choice == 0)
                            {
                                var first = servers.First();
                                LoreSettings.ServerUrl = first.Url;
                                serverUrlField.value = first.Url;
                            }
                        }

                        scanBtn.text = "Detect Servers";
                        scanBtn.SetEnabled(true);
                    })
                    { text = "Detect Servers", style = { width = 120 } };
                    scanBtnRow.Add(scanBtn);
                    prefs.Add(scanBtnRow);

                    prefs.Add(new VisualElement { style = { height = 10 } });

                    // ── Repository Path ──
                    prefs.Add(new Label("Repository Path") { style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold } });

                    var repoPathField = new TextField { value = LoreSettings.RepoPath };
                    repoPathField.RegisterValueChangedCallback(evt => LoreSettings.RepoPath = evt.newValue);
                    prefs.Add(repoPathField);

                    var browseBtn = new Button(() =>
                    {
                        var selected = EditorUtility.OpenFolderPanel("Select Repository", LoreSettings.RepoPath, "");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            LoreSettings.RepoPath = selected;
                            repoPathField.value = selected;
                        }
                    }) { text = "Browse..." };
                    prefs.Add(browseBtn);

                    prefs.Add(new VisualElement { style = { height = 10 } });

                    // ── Auto-start ──
                    var autoStartToggle = new Toggle("Auto-start server on Unity load")
                    {
                        value = LoreSettings.AutoStartServer
                    };
                    autoStartToggle.RegisterValueChangedCallback(evt => LoreSettings.AutoStartServer = evt.newValue);
                    prefs.Add(autoStartToggle);

                    prefs.Add(new VisualElement { style = { height = 6 } });

                    // ── Refresh Interval ──
                    var intervalField = new IntegerField("Auto-refresh interval (seconds)")
                    {
                        value = LoreSettings.RefreshInterval
                    };
                    intervalField.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue < 5) intervalField.value = 5;
                        if (evt.newValue > 300) intervalField.value = 300;
                        LoreSettings.RefreshInterval = intervalField.value;
                    });
                    prefs.Add(intervalField);

                    prefs.Add(new VisualElement { style = { height = 16 } });

                    // ── Status ──
                    var statusBox = new VisualElement
                    {
                        style = { backgroundColor = new Color(0.15f, 0.15f, 0.15f),
                                   padding = 8, marginTop = 8 }
                    };
                    statusBox.Add(new Label("Status") { style = { fontSize = 11, bold = true } });

                    var lorePath = LoreCliService.ResolveLorePath();
                    var serverPath = LoreCliService.ResolveServerPath();
                    statusBox.Add(new Label($"Lore: {(lorePath ?? "NOT FOUND"):blue}"));
                    statusBox.Add(new Label($"Server: {(serverPath ?? "NOT FOUND"):blue}"));

                    prefs.Add(statusBox);

                    rootElement.Add(prefs);
                }
            };
        }
    }
}
