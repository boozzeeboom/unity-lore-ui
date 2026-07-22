using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectC.LoreUnity
{
    /// <summary>
    /// Main Lore VCS window. Dockable EditorWindow with tabs:
    /// Status, History, Branches, Diff.
    /// </summary>
    public class LoreWindow : EditorWindow
    {
        [MenuItem("Lore/Lore Window", false, 10)]
        public static void ShowWindow()
        {
            var wnd = GetWindow<LoreWindow>();
            wnd.titleContent = new GUIContent("Lore VCS");
            wnd.minSize = new Vector2(500, 350);
        }

        // ── UI references ──

        private Label _branchLabel;
        private Label _serverStatus;
        private Label _statusSummary;
        private Label _statusBar;
        private TextField _commitMsg;
        private Button _startStopBtn;
        private ListView _fileList;
        private ListView _commitList;
        private Label _commitDetailText;
        private ListView _localBranchList;
        private ListView _remoteBranchList;
        private Label _diffFileLabel;
        private Label _diffText;
        private ListView _commitFilesList;
        private Label _commitFilesLabel;

        private VisualElement _statusPanel;
        private VisualElement _historyPanel;
        private VisualElement _branchesPanel;
        private VisualElement _diffPanel;
        private VisualElement _commitArea;

        // ── Data ──

        private LoreStatus _currentStatus;
        private List<LoreCommit> _currentHistory = new List<LoreCommit>();
        private List<LoreBranch> _localBranches = new List<LoreBranch>();
        private List<LoreBranch> _remoteBranches = new List<LoreBranch>();
        private List<(string Display, LoreFileEntry Entry)> _fileDisplayList = new List<(string, LoreFileEntry)>();
        private List<LoreCommitFile> _currentCommitFiles = new List<LoreCommitFile>();
        private string _currentBranchName = "—";

        // ── Timer ──

        private double _lastRefreshTime;
        private bool _isRefreshing;

        // ── Lifecycle ──

        private void OnEnable()
        {
            // Load UXML
            var uxmlPath = "Packages/com.projectc.lore-unity/Editor/LoreWindow.uxml";
            var ussPath = "Packages/com.projectc.lore-unity/Editor/LoreWindow.uss";

            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (asset == null)
            {
                Debug.LogError($"[Lore] UXML not found at {uxmlPath}. Check package installation.");
                return;
            }

            var ui = asset.CloneTree();
            rootVisualElement.Add(ui);

            // Load USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);

            // Bind UI elements
            BindUI();

            // Wire events
            WireEvents();

            // Start refresh
            SwitchTab("status");
            LoreServerManager.OnServerStatusChanged += OnServerStatusChange;
            EditorApplication.update += OnEditorUpdate;

            // First refresh on next frame
            EditorApplication.delayCall += () => _ = RefreshAllAsync();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            LoreServerManager.OnServerStatusChanged -= OnServerStatusChange;
        }

        // ── UI Binding ──

        private void BindUI()
        {
            var root = rootVisualElement;

            _branchLabel = root.Q<Label>("branch-label");
            _serverStatus = root.Q<Label>("server-status");
            _statusSummary = root.Q<Label>("status-summary");
            _statusBar = root.Q<Label>("status-bar");
            _commitMsg = root.Q<TextField>("commit-message");
            _commitDetailText = root.Q<Label>("commit-detail-text");
            _diffFileLabel = root.Q<Label>("diff-file-label");
            _diffText = root.Q<Label>("diff-text");

            _startStopBtn = root.Q<Button>("start-stop-btn");

            _fileList = root.Q<ListView>("file-list");
            _commitList = root.Q<ListView>("commit-list");
            _localBranchList = root.Q<ListView>("local-branch-list");
            _remoteBranchList = root.Q<ListView>("remote-branch-list");
            _commitFilesList = root.Q<ListView>("commit-files-list");
            _commitFilesLabel = root.Q<Label>("commit-files-label");

            _statusPanel = root.Q<VisualElement>("status-panel");
            _historyPanel = root.Q<VisualElement>("history-panel");
            _branchesPanel = root.Q<VisualElement>("branches-panel");
            _diffPanel = root.Q<VisualElement>("diff-panel");
            _commitArea = root.Q<VisualElement>("commit-area");
        }

        private void WireEvents()
        {
            var root = rootVisualElement;

            // Refresh button
            root.Q<Button>("refresh-btn").clicked += () => _ = RefreshAllAsync();

            // Scan button
            root.Q<Button>("scan-btn").clicked += () => _ = ScanForServersAsync();

            // Start/Stop server button
            _startStopBtn.clicked += () => _ = ToggleServerAsync();

            // Settings button
            root.Q<Button>("settings-btn").clicked += () =>
            {
                SettingsService.OpenUserPreferences("Preferences/Lore");
            };

            // Tab buttons (reliable text rendering vs Toggle)
            root.Q<Button>("tab-status").clicked += () => SwitchTab("status");
            root.Q<Button>("tab-history").clicked += () => SwitchTab("history");
            root.Q<Button>("tab-branches").clicked += () => SwitchTab("branches");
            root.Q<Button>("tab-diff").clicked += () => SwitchTab("diff");

            // Commit
            root.Q<Button>("stage-all-btn").clicked += () => _ = StageAllAsync();
            root.Q<Button>("commit-push-btn").clicked += () => _ = CommitPushAsync();
            _commitMsg.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return && !string.IsNullOrEmpty(_commitMsg.value))
                {
                    evt.StopPropagation();
                    _ = CommitPushAsync();
                }
            });

            // History actions
            root.Q<Button>("copy-hash-btn").clicked += CopyCommitHash;
            root.Q<Button>("revert-btn").clicked += () => _ = RevertCommitAsync();

            // Back button — clear commit selection
            root.Q<Button>("back-btn").clicked += ClearCommitSelection;

            // Branch actions
            root.Q<Button>("new-branch-btn").clicked += () => _ = PromptCreateBranchAsync();
            root.Q<Button>("switch-branch-btn").clicked += () => _ = PromptSwitchBranchAsync();
            root.Q<Button>("merge-branch-btn").clicked += () => _ = PromptMergeBranchAsync();

            // File list selection → diff
            _fileList.selectionChanged += OnFileSelected;

            // Commit list selection (wired once, not on every UpdateHistoryTab)
            _commitList.selectionChanged += OnCommitSelected;
        }

        // ── Tab switching ──

        private void SwitchTab(string tab)
        {
            _statusPanel.style.display = tab == "status" ? DisplayStyle.Flex : DisplayStyle.None;
            _historyPanel.style.display = tab == "history" ? DisplayStyle.Flex : DisplayStyle.None;
            _branchesPanel.style.display = tab == "branches" ? DisplayStyle.Flex : DisplayStyle.None;
            _diffPanel.style.display = tab == "diff" ? DisplayStyle.Flex : DisplayStyle.None;

            // Commit area only on Status tab
            if (_commitArea != null)
                _commitArea.style.display = tab == "status" ? DisplayStyle.Flex : DisplayStyle.None;

            // Update tab button active class
            var root = rootVisualElement;
            foreach (var name in new[] { "tab-status", "tab-history", "tab-branches", "tab-diff" })
            {
                var btn = root.Q<Button>(name);
                if (btn != null)
                {
                    if (name == "tab-" + tab)
                        btn.AddToClassList("tab-active");
                    else
                        btn.RemoveFromClassList("tab-active");
                }
            }
        }

        // ── Refresh ──

        private async void OnEditorUpdate()
        {
            // Periodic refresh based on interval setting
            var interval = LoreSettings.RefreshInterval;
            if (interval <= 0) interval = 30;

            if (EditorApplication.timeSinceStartup - _lastRefreshTime > interval)
            {
                _lastRefreshTime = EditorApplication.timeSinceStartup;
                await RefreshAllAsync();
            }
        }

        private void OnServerStatusChange(bool alive)
        {
            EditorApplication.delayCall += () =>
            {
                UpdateServerIndicator(alive);
                if (alive) _ = RefreshAllAsync();
            };
        }

        private async Task RefreshAllAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                SetStatus("Refreshing...");

                // Check server
                var serverAlive = await LoreServerManager.HealthCheckAsync();
                UpdateServerIndicator(serverAlive);

                if (!serverAlive)
                {
                    SetStatus("Server offline. Lore operations may be limited.");
                    return;
                }

                // Refresh data in parallel
                var statusTask = LoreCliService.GetStatusAsync();
                var historyTask = LoreCliService.GetHistoryAsync(10);
                var branchesTask = LoreCliService.GetBranchesAsync();

                await Task.WhenAll(statusTask, historyTask, branchesTask);

                _currentStatus = statusTask.Result;
                _currentHistory = historyTask.Result;
                var branches = branchesTask.Result;

                // Extract local vs remote
                _localBranches = branches?.Where(b => !b.IsRemote).ToList() ?? new List<LoreBranch>();
                _remoteBranches = branches?.Where(b => b.IsRemote).ToList() ?? new List<LoreBranch>();

                // Update UI on main thread
                EditorApplication.delayCall += UpdateUI;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Lore] Refresh failed: {ex.Message}");
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        // ── Scan for servers ──

        private async Task ScanForServersAsync()
        {
            SetStatus("Scanning for Lore servers...");

            try
            {
                var servers = await LoreServerScanner.ScanAllAsync();

                if (servers.Count == 0)
                {
                    EditorUtility.DisplayDialog("Scan Complete",
                        "No Lore servers found on this machine.\n\n" +
                        "• Run Lore → Install Lore Server to set one up.\n" +
                        "• Or start loreserver manually from command line.",
                        "OK");
                    SetStatus("No servers found.");
                    return;
                }

                // Build message
                var msg = $"Found {servers.Count} Lore server(s):\n\n";
                foreach (var s in servers)
                {
                    var status = s.IsAlive ? "● running" : "○ stopped";
                    msg += $"  {status}  {s.Url}\n";
                    msg += $"         ({s.Source})\n\n";
                }
                msg += "Select one to connect, or Cancel to keep current.";

                // Let user choose
                var choice = EditorUtility.DisplayDialogComplex("Lore Servers Found",
                    msg,
                    "Use First", "Cancel", "Show All...");

                if (choice == 0) // Use First
                {
                    var first = servers.First();
                    LoreSettings.ServerUrl = first.Url;
                    SetStatus($"Connected to {first.Url}");
                    await Task.Delay(500);

                    // Auto-start if stopped
                    if (!first.IsAlive)
                    {
                        SetStatus("Starting server...");
                        var started = await LoreServerManager.StartServerAsync();
                        if (started)
                            SetStatus("Server started ✓");
                        else
                            SetStatus("Server not running. Click ▶ to start it.");
                    }

                    await RefreshAllAsync();
                }
                else if (choice == 2) // Show All
                {
                    if (servers.Count > 0)
                    {
                        var sel = servers[0];
                        LoreSettings.ServerUrl = sel.Url;
                        SetStatus($"Connected to {sel.Url}");
                        await Task.Delay(500);

                        // Auto-start if stopped
                        if (!sel.IsAlive)
                        {
                            SetStatus("Starting server...");
                            var started = await LoreServerManager.StartServerAsync();
                            if (started)
                                SetStatus("Server started ✓");
                            else
                                SetStatus("Server not running. Click ▶ to start it.");
                        }

                        await RefreshAllAsync();
                    }
                }
                else
                {
                    SetStatus("Scan cancelled.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Lore] Scan failed: {ex.Message}");
                EditorUtility.DisplayDialog("Scan Failed", ex.Message, "OK");
                SetStatus("Scan failed.");
            }
        }

        private void UpdateUI()
        {
            // Reset commit detail on refresh
            _commitDetailText.text = "Select a commit";
            _selectedCommitHash = null;

            UpdateStatusTab();
            UpdateHistoryTab();
            UpdateBranchesTab();
            UpdateServerIndicator();

            _lastRefreshTime = EditorApplication.timeSinceStartup;
            SetStatus("Ready");
        }

        // ── Status Tab ──

        private void UpdateStatusTab()
        {
            if (_currentStatus == null)
            {
                _statusSummary.text = "No status data. Is server running?";
                _fileList.itemsSource = null;
                _fileList.Rebuild();
                return;
            }

            _branchLabel.text = _currentStatus.BranchName;
            _currentBranchName = _currentStatus.BranchName;

            var staged = _currentStatus.Files.Count(f => f.IsStaged);
            var modified = _currentStatus.Files.Count(f => !f.IsStaged);
            _statusSummary.text = $"{_currentStatus.BranchName} · " +
                                  $"rev {_currentStatus.CurrentRevision} · " +
                                  $"{staged} staged · {modified} modified" +
                                  (_currentStatus.IsSynced ? " · synced" : " · unpushed");

            // Build display list
            _fileDisplayList = _currentStatus.Files
                .Select(f =>
                {
                    var icon = f.IsStaged ? "📦" : f.Status switch
                    {
                        LoreFileStatusType.Modified => "🟡",
                        LoreFileStatusType.Added => "➕",
                        LoreFileStatusType.Deleted => "🔴",
                        _ => "  "
                    };
                    return (Display: $"{icon} {f.Path}", Entry: f);
                })
                .ToList();

            _fileList.itemsSource = _fileDisplayList;
            _fileList.makeItem = () => new Label();
            _fileList.bindItem = (element, index) =>
            {
                if (element is Label label && index < _fileDisplayList.Count)
                {
                    var item = _fileDisplayList[index];
                    label.text = item.Display;
                    label.tooltip = item.Entry.Status.ToString() + (item.Entry.IsStaged ? " (staged)" : "");
                }
            };
            _fileList.Rebuild();
        }

        private void OnFileSelected(IEnumerable<object> selection)
        {
            var item = selection?.FirstOrDefault();
            if (item is (string Display, LoreFileEntry entry))
            {
                // Switch to diff tab and show diff
                SwitchTab("diff");
                _ = ShowDiffAsync(entry.Path);
            }
        }

        private async Task ShowDiffAsync(string path)
        {
            var diff = await LoreCliService.GetDiffAsync(path);
            _diffFileLabel.text = path;
            _diffText.text = diff ?? "(no diff content — file may be empty or binary)";
        }

        // ── History Tab ──

        private void UpdateHistoryTab()
        {
            _commitList.itemsSource = _currentHistory;
            _commitList.makeItem = () => new Label();
            _commitList.bindItem = (element, index) =>
            {
                if (element is Label label && index < _currentHistory.Count)
                {
                    var c = _currentHistory[index];
                    var unpushed = c.IsUnpushed ? "↑" : " ";
                    var date = c.Date.ToString("yyyy-MM-dd");
                    label.text = $"{unpushed} #{c.RevisionNumber}  {c.ShortHash}  {date}  {c.Message?.Split('\n').FirstOrDefault() ?? ""}";
                    label.tooltip = c.Message;

                    // Use CSS class for unpushed color (works with :selected)
                    label.RemoveFromClassList("commit-unpushed");
                    if (c.IsUnpushed) label.AddToClassList("commit-unpushed");
                }
            };

            _commitList.Rebuild();
        }

        private void OnCommitSelected(IEnumerable<object> selection)
        {
            var commit = selection?.FirstOrDefault() as LoreCommit;
            if (commit == null) return;

            _commitDetailText.text = $"Revision: {commit.RevisionNumber}\n" +
                                     $"Hash: {commit.Signature}\n" +
                                     $"Parent: {commit.ParentSignature ?? "—"}\n" +
                                     $"Date: {commit.Date:yyyy-MM-dd HH:mm:ss}\n\n" +
                                     $"{commit.Message}";

            _selectedCommitHash = commit.Signature;

            // Load files changed in this commit
            _ = ShowCommitFilesAsync(commit);
        }

        private string _selectedCommitHash;

        private void ClearCommitSelection()
        {
            _commitList.ClearSelection();
            _commitDetailText.text = "Select a commit";
            _selectedCommitHash = null;
            _currentCommitFiles.Clear();
            _commitFilesList.itemsSource = null;
            _commitFilesList.Rebuild();
            _commitFilesLabel.text = "Changed Files:";
        }

        private async Task ShowCommitFilesAsync(LoreCommit commit)
        {
            var files = await LoreCliService.GetCommitFilesAsync(commit);
            _currentCommitFiles = files;

            if (_commitFilesList == null) return;

            _commitFilesLabel.text = files.Count > 0
                ? $"Changed Files ({files.Count}):"
                : "Changed Files:";

            _commitFilesList.itemsSource = _currentCommitFiles;
            _commitFilesList.makeItem = () =>
            {
                var ve = new VisualElement();
                ve.style.flexDirection = FlexDirection.Row;
                var label = new Label();
                label.style.flexGrow = 1f;
                label.style.fontSize = 10;
                label.style.paddingLeft = 4;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                var extractBtn = new Button();
                extractBtn.text = "📋";
                extractBtn.tooltip = "Save As...";
                extractBtn.style.fontSize = 9;
                extractBtn.style.paddingLeft = 2;
                extractBtn.style.paddingRight = 2;
                extractBtn.style.backgroundColor = new Color(0, 0, 0, 0);
                extractBtn.style.borderLeftWidth = 0;
                extractBtn.style.borderRightWidth = 0;
                extractBtn.style.borderTopWidth = 0;
                extractBtn.style.borderBottomWidth = 0;
                ve.Add(label);
                ve.Add(extractBtn);
                return ve;
            };
            _commitFilesList.bindItem = (element, index) =>
            {
                if (index >= _currentCommitFiles.Count) return;
                var cf = _currentCommitFiles[index];
                var label = element[0] as Label;
                var btn = element[1] as Button;

                if (label != null)
                {
                    var icon = cf.Status switch
                    {
                        LoreFileStatusType.Added => "➕",
                        LoreFileStatusType.Deleted => "🔴",
                        _ => "🟡"
                    };
                    var stat = cf.Additions > 0 || cf.Deletions > 0
                        ? $" (+{cf.Additions}/-{cf.Deletions})"
                        : "";
                    label.text = $"{icon} {cf.Path}{stat}";
                    label.tooltip = $"{cf.Status}: {cf.Path}";
                }

                if (btn != null)
                {
                    btn.clickable = null;
                    var capturedPath = cf.Path;
                    var capturedHash = _selectedCommitHash;
                    btn.clicked += () => _ = ExtractCommitFileAsync(capturedHash, capturedPath);
                }
            };
            _commitFilesList.Rebuild();
        }

        private void CopyCommitHash()
        {
            if (!string.IsNullOrEmpty(_selectedCommitHash))
            {
                EditorGUIUtility.systemCopyBuffer = _selectedCommitHash;
                SetStatus($"Copied: {_selectedCommitHash.Substring(0, 7)}");
            }
        }

        private async Task ExtractCommitFileAsync(string hash, string path)
        {
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(path))
                return;

            // Determine a default file name
            var fileName = System.IO.Path.GetFileName(path);
            var savePath = EditorUtility.SaveFilePanel(
                "Save file from commit " + hash.Substring(0, 7),
                "",
                fileName,
                "");

            if (string.IsNullOrEmpty(savePath)) return;

            SetStatus($"Extracting {path} from commit {hash.Substring(0, 7)}...");
            var content = await LoreCliService.GetCommitFileContentAsync(hash, path);
            if (content == null)
            {
                EditorUtility.DisplayDialog("Extract Failed",
                    $"Could not get content of {path} at commit {hash.Substring(0, 7)}.",
                    "OK");
                SetStatus("Extract failed");
                return;
            }

            try
            {
                System.IO.File.WriteAllText(savePath, content);
                SetStatus($"Saved: {savePath}");
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Save Failed",
                    $"Could not write file:\n{ex.Message}",
                    "OK");
                SetStatus("Extract failed");
            }
        }

        private async Task RevertCommitAsync()
        {
            if (string.IsNullOrEmpty(_selectedCommitHash)) return;

            if (!EditorUtility.DisplayDialog("Revert Revision",
                    $"Revert revision {_selectedCommitHash.Substring(0, 7)}?\nThis will create a new commit that undoes the changes.",
                    "Revert", "Cancel"))
                return;

            SetStatus("Reverting...");
            var result = await LoreCliService.RevertRevisionAsync(_selectedCommitHash);
            SetStatus(result.Success ? "Reverted successfully" : $"Revert failed: {result.Output}");
            if (result.Success) await RefreshAllAsync();
        }

        // ── Branches Tab ──

        private void UpdateBranchesTab()
        {
            UpdateBranchList(_localBranchList, _localBranches, true);
            UpdateBranchList(_remoteBranchList, _remoteBranches, false);
        }

        private void UpdateBranchList(ListView listView, List<LoreBranch> branches, bool isLocal)
        {
            listView.itemsSource = branches;
            listView.makeItem = () => new Label();
            listView.bindItem = (element, index) =>
            {
                if (element is Label label && index < branches.Count)
                {
                    var b = branches[index];
                    var prefix = b.IsCurrent ? "* " : "  ";
                    var unpushed = b.LatestSignature != b.RemoteLatestSignature && !string.IsNullOrEmpty(b.RemoteLatestSignature) ? " ↑" : "";
                    label.text = $"{prefix}{b.Name} ({b.ShortHash}){unpushed}";
                    label.style.color = b.IsCurrent ? new StyleColor(new Color(1f, 0.84f, 0.2f)) : new StyleColor(Color.gray);
                    label.style.unityFontStyleAndWeight = b.IsCurrent ? FontStyle.Bold : FontStyle.Normal;
                }
            };
            listView.Rebuild();
        }

        // ── Branch actions with input popup ──

        private async Task PromptCreateBranchAsync()
        {
            var name = ShowInputPopup("Create Branch", "Enter new branch name:", "");
            if (string.IsNullOrEmpty(name)) return;

            SetStatus($"Creating branch '{name}'...");
            var result = await LoreCliService.CreateBranchAsync(name);
            SetStatus(result.Success ? $"Branch '{name}' created" : $"Failed: {result.Output}");
            if (result.Success) await RefreshAllAsync();
        }

        private async Task PromptSwitchBranchAsync()
        {
            var name = ShowInputPopup("Switch Branch", "Enter branch name to switch to:", _currentBranchName);
            if (string.IsNullOrEmpty(name)) return;

            SetStatus($"Switching to '{name}'...");
            var result = await LoreCliService.SwitchBranchAsync(name);
            SetStatus(result.Success ? $"Switched to '{name}'" : $"Failed: {result.Output}");
            if (result.Success) await RefreshAllAsync();
        }

        private async Task PromptMergeBranchAsync()
        {
            var name = ShowInputPopup("Merge Branch", "Enter branch name to merge into current:", "");
            if (string.IsNullOrEmpty(name)) return;

            SetStatus($"Merging '{name}'...");
            var result = await LoreCliService.MergeBranchAsync(name);
            SetStatus(result.Success ? $"Merged '{name}'" : $"Failed: {result.Output}");
            if (result.Success) await RefreshAllAsync();
        }

        /// <summary>
        /// Simple modal input dialog using IMGUI EditorWindow.
        /// </summary>
        private static string ShowInputPopup(string title, string label, string defaultValue)
        {
            var wnd = CreateInstance<LoreInputPopup>();
            wnd.titleContent = new GUIContent(title);
            wnd.Label = label;
            wnd.DefaultValue = defaultValue;
            wnd.Result = null;
            wnd.ShowModalUtility();
            return wnd.Result;
        }

        /// <summary>
        /// Temporary modal popup for text input.
        /// </summary>
        private class LoreInputPopup : EditorWindow
        {
            public string Label;
            public string DefaultValue;
            public string Result;
            private string _text;
            private bool _firstFrame = true;

            private void OnGUI()
            {
                if (_firstFrame)
                {
                    _text = DefaultValue;
                    _firstFrame = false;
                }

                GUILayout.Space(8);
                GUILayout.Label(Label);
                GUI.SetNextControlName("inputField");
                _text = GUILayout.TextField(_text ?? "", 100);
                GUILayout.Space(8);

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("OK", GUILayout.Width(80)))
                {
                    Result = _text;
                    Close();
                }
                if (GUILayout.Button("Cancel", GUILayout.Width(80)))
                {
                    Result = null;
                    Close();
                }
                GUILayout.EndHorizontal();

                // Focus the text field on first frame
                if (_firstFrame)
                {
                    EditorGUI.FocusTextInControl("inputField");
                    _firstFrame = false;
                }
            }
        }

        // ── Actions: Stage / Commit / Push ──

        private async Task StageAllAsync()
        {
            SetStatus("Staging...");
            var result = await LoreCliService.StageAllAsync();
            SetStatus(result.Success ? "Staged all changes" : $"Stage failed: {result.Output}");
            if (result.Success) await RefreshAllAsync();
        }

        private async Task CommitPushAsync()
        {
            var msg = _commitMsg.value.Trim();
            if (string.IsNullOrEmpty(msg))
            {
                SetStatus("Write a commit message first.");
                return;
            }

            SetStatus("Staging & committing...");

            // Stage all first
            await LoreCliService.StageAllAsync();

            // Commit
            var commitResult = await LoreCliService.CommitAsync(msg);
            if (!commitResult.Success)
            {
                SetStatus($"Commit failed: {commitResult.Output}");
                return;
            }

            SetStatus("Pushing...");

            // Push
            var pushResult = await LoreCliService.PushAsync();
            if (pushResult.Success)
            {
                _commitMsg.value = "";
                SetStatus("Committed + pushed ✓");
            }
            else
            {
                SetStatus($"Committed (local), but push failed: {pushResult.Output}");
            }

            await RefreshAllAsync();
        }

        // ── Server indicator ──

        private void UpdateServerIndicator(bool? aliveOverride = null)
        {
            var alive = aliveOverride ?? LoreServerManager.HealthCheckSync();
            _serverStatus.text = alive ? "●" : "○";
            _serverStatus.style.color = alive ? new StyleColor(new Color(0.2f, 0.8f, 0.2f))
                                              : new StyleColor(new Color(0.8f, 0.2f, 0.2f));
            _serverStatus.tooltip = alive
                ? $"Server running at {LoreSettings.ServerUrl}"
                : "Server offline";

            // Update start/stop button
            if (_startStopBtn != null)
            {
                var hasExe = !string.IsNullOrEmpty(LoreCliService.ResolveServerPath());
                _startStopBtn.text = alive ? "■" : "▶";
                _startStopBtn.tooltip = alive ? "Stop Lore server" : "Start Lore server";
                _startStopBtn.style.display = (alive || hasExe) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ── Start / Stop server ──

        private async Task ToggleServerAsync()
        {
            if (LoreServerManager.IsRunning)
            {
                LoreServerManager.StopServer();
                UpdateServerIndicator(false);
                SetStatus("Server stopped.");
            }
            else
            {
                SetStatus("Starting server...");
                var success = await LoreServerManager.StartServerAsync();
                UpdateServerIndicator(success);
                if (success)
                {
                    SetStatus("Server started ✓");
                    await RefreshAllAsync();
                }
                else
                {
                    SetStatus("Failed to start server. Check preferences.");
                }
            }
        }

        // ── Helpers ──

        private void SetStatus(string msg)
        {
            if (_statusBar != null)
                _statusBar.text = msg;
        }
    }
}
