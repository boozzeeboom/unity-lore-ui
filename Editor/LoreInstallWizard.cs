using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectC.LoreUnity
{
    /// <summary>
    /// Setup wizard. Guides user through:
    /// 1. Checking lore.exe
    /// 2. Scanning for existing servers (process, port, config)
    /// 3. Installing if nothing found
    /// 4. Starting / connecting to server
    /// 5. Repository path
    /// </summary>
    public class LoreInstallWizard : EditorWindow
    {
        [MenuItem("Lore/Install Lore Server", false, 100)]
        public static void ShowWindow()
        {
            var wnd = GetWindow<LoreInstallWizard>(true, "Lore VCS — Setup Wizard", true);
            wnd.minSize = new Vector2(580, 440);
            wnd.maxSize = new Vector2(580, 600);
        }

        private Label _stepLabel;
        private ProgressBar _progressBar;
        private Button _actionBtn;
        private Button _closeBtn;
        private ScrollView _detailsScroll;
        private VisualElement _detailsContent;
        private VisualElement _scanResultBox;

        private int _currentStep;
        private List<LoreServerScanner.ServerCandidate> _foundServers = new List<LoreServerScanner.ServerCandidate>();

        private void CreateGUI()
        {
            var root = rootVisualElement;

            root.Add(new Label("Lore VCS Setup Wizard")
            {
                style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold,
                          paddingTop = 10, paddingBottom = 10, paddingLeft = 10, paddingRight = 10,
                          marginBottom = 6 }
            });

            _stepLabel = new Label("Step 1: Check Lore CLI")
            {
                style = { paddingTop = 4, paddingBottom = 4, paddingLeft = 4, paddingRight = 4, fontSize = 13 }
            };
            root.Add(_stepLabel);

            _progressBar = new ProgressBar { value = 0, style = { height = 6, marginLeft = 4, marginRight = 4, marginTop = 4, marginBottom = 4 } };
            root.Add(_progressBar);

            // Scrollable details
            _detailsScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style = { backgroundColor = new Color(0.12f, 0.12f, 0.12f),
                          paddingTop = 8, paddingBottom = 8, paddingLeft = 8, paddingRight = 8,
                          marginLeft = 4, marginRight = 4, marginTop = 4, marginBottom = 4,
                          minHeight = 140, maxHeight = 260 }
            };
            _detailsContent = new VisualElement();
            _detailsContent.Add(new Label("Checking for Lore CLI..."));
            _detailsScroll.Add(_detailsContent);
            root.Add(_detailsScroll);

            // Scan result list (hidden by default)
            _scanResultBox = new VisualElement
            {
                style = { display = DisplayStyle.None,
                          backgroundColor = new Color(0.1f, 0.1f, 0.1f),
                          paddingTop = 6, paddingBottom = 6, paddingLeft = 6, paddingRight = 6,
                          marginLeft = 4, marginRight = 4, marginTop = 4, marginBottom = 4,
                          minHeight = 60, maxHeight = 160, overflow = Overflow.Hidden }
            };
            root.Add(_scanResultBox);

            // Bottom buttons
            var btnRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd,
                          paddingTop = 6, paddingBottom = 6, paddingLeft = 6, paddingRight = 6,
                          marginTop = 8 }
            };

            _actionBtn = new Button(RunNextStep) { text = "Start", style = { width = 130 } };
            btnRow.Add(_actionBtn);

            _closeBtn = new Button(() => Close()) { text = "Close", style = { width = 80, marginLeft = 4 } };
            btnRow.Add(_closeBtn);

            root.Add(btnRow);

            EditorApplication.delayCall += RunNextStep;
        }

        private async void RunNextStep()
        {
            _actionBtn.SetEnabled(false);
            _scanResultBox.style.display = DisplayStyle.None;

            switch (_currentStep)
            {
                case 0: await StepCheckLore(); break;
                case 1: await StepScanForServers(); break;
                case 2: await StepInstallOrConnect(); break;
                case 3: await StepStartServer(); break;
                case 4: await StepRepoPath(); break;
                case 5: StepDone(); break;
            }

            _currentStep++;
            _progressBar.value = (_currentStep / 6f) * 100;
        }

        // ── Step 1: Check Lore CLI ──

        private async Task StepCheckLore()
        {
            _stepLabel.text = "Step 1: Check Lore CLI";
            Log("Checking if lore.exe is available...");

            var lorePath = LoreCliService.ResolveLorePath();
            if (lorePath != null)
            {
                Log($"✓ Found lore.exe at: {lorePath}");
                var result = await LoreCliService.ExecuteAsync("--version");
                Log(result.ExitCode == 0 ? "✓ lore.exe responds" : "⚠ lore.exe found but has errors");
                var versionLine = result.Output?.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                if (!string.IsNullOrEmpty(versionLine) && !versionLine.StartsWith("["))
                    Log($"  Version: {versionLine.Trim()}");
            }
            else
            {
                Log("⚠ lore.exe not found in PATH or settings.");
                Log("→ Will download in step 3 if no server found.");
            }

            _actionBtn.text = "Next: Scan for Server";
        }

        // ── Step 2: Scan for existing servers ──

        private async Task StepScanForServers()
        {
            _stepLabel.text = "Step 2: Scan for Existing Servers";
            Log("Scanning local machine for Lore servers...");
            Log("(processes, ports, config files, repository remotes)");
            Log("");

            _foundServers = await LoreServerScanner.ScanAllAsync();

            if (_foundServers.Count == 0)
            {
                Log("✗ No Lore servers found on this machine.");
                Log("→ Will install a fresh copy in the next step.");
                _actionBtn.text = "Next: Install";
                return;
            }

            _scanResultBox.style.display = DisplayStyle.Flex;
            _scanResultBox.Clear();

            var header = new Label($"Found {_foundServers.Count} server candidate(s) — click to select:")
            {
                style = { fontSize = 11, color = Color.gray, marginBottom = 4 }
            };
            _scanResultBox.Add(header);

            var list = new ListView
            {
                itemsSource = _foundServers,
                fixedItemHeight = 24,
                selectionType = SelectionType.Single,
                style = { flexGrow = 1 }
            };
            list.makeItem = () => new Label { style = { fontSize = 11, paddingTop = 2, paddingBottom = 2, paddingLeft = 2, paddingRight = 2 } };
            list.bindItem = (element, i) =>
            {
                if (element is Label l && i < _foundServers.Count)
                {
                    var s = _foundServers[i];
                    l.text = s.DisplayName;
                    l.style.color = s.IsAlive ? new StyleColor(new Color(0.3f, 0.9f, 0.3f)) : new StyleColor(Color.gray);
                }
            };
            list.selectionChanged += objects =>
            {
                var sel = objects.FirstOrDefault() as LoreServerScanner.ServerCandidate;
                if (sel != null)
                {
                    LoreSettings.ServerUrl = sel.Url;
                    Log($"→ Selected: {sel.Url}");

                    if (!string.IsNullOrEmpty(sel.ConfigPath))
                    {
                        var parentDir = Path.GetDirectoryName(sel.ConfigPath);
                        var serverExe = Path.Combine(parentDir, "loreserver.exe");
                        if (File.Exists(serverExe))
                            LoreSettings.ServerExePath = serverExe;
                    }
                }
            };
            _scanResultBox.Add(list);

            Log("✓ Servers scanned. Select one from the list above,");
            Log("  or click Next to install a fresh copy.");

            _actionBtn.text = "Next: Connect / Install";
            _actionBtn.SetEnabled(true);
        }

        // ── Step 3: Install or Connect ──

        private async Task StepInstallOrConnect()
        {
            _stepLabel.text = "Step 3: Install / Connect";

            var aliveServer = _foundServers?.FirstOrDefault(s => s.IsAlive);
            if (aliveServer != null)
            {
                Log($"✓ Using live server at {aliveServer.Url}");
                LoreSettings.ServerUrl = aliveServer.Url;

                if (!string.IsNullOrEmpty(aliveServer.ConfigPath))
                {
                    var configDir = Path.GetDirectoryName(aliveServer.ConfigPath);
                    var loreExe = Path.Combine(configDir, "lore.exe");
                    if (!File.Exists(loreExe))
                        loreExe = Path.Combine(configDir, "..", "lore.exe");
                    if (File.Exists(loreExe))
                        LoreSettings.LoreExePath = Path.GetFullPath(loreExe);
                }

                _actionBtn.text = "Next: Start Server";
                return;
            }

            var lorePath = LoreCliService.ResolveLorePath();
            if (lorePath != null)
            {
                Log("✓ lore.exe already available.");
                _actionBtn.text = "Next: Start Server";
                return;
            }

            Log("Downloading Lore CLI from GitHub releases...");
            Log("This may take a moment.");
            var (success, message) = await LoreServerManager.DownloadAndInstallAsync();
            if (success)
            {
                Log($"✓ Installed to: {message}");
                Log($"  lore.exe: {LoreSettings.LoreExePath}");
                Log($"  loreserver.exe: {LoreSettings.ServerExePath}");
            }
            else
            {
                Log($"✗ {message}");
                Log("");
                Log("You can also install manually:");
                Log("  1. https://github.com/EpicGames/lore/releases");
                Log("  2. Extract to any folder");
                Log("  3. Set paths in Edit → Preferences → Lore");
            }

            _actionBtn.text = "Next: Start Server";
        }

        // ── Step 4: Start Server ──

        private async Task StepStartServer()
        {
            _stepLabel.text = "Step 4: Start Server";

            var alive = await LoreServerManager.HealthCheckAsync();
            if (alive)
            {
                Log($"✓ Server running at {LoreSettings.ServerUrl}");
                _actionBtn.text = "Next: Repository";
                return;
            }

            var serverPath = LoreCliService.ResolveServerPath();
            if (serverPath == null)
            {
                Log("⚠ loreserver.exe not found.");
                Log("  Without server: commit works locally, push/sync won't work.");
                Log("  To start manually, run loreserver from command line.");
                _actionBtn.text = "Next: Repository";
                return;
            }

            Log("Starting loreserver...");
            var started = await LoreServerManager.StartServerAsync();
            if (started)
                Log($"✓ Server started at {LoreSettings.ServerUrl}");
            else
                Log("⚠ Server process launched but health check failed.");
            Log("  You can restart later from the Lore window toolbar.");

            _actionBtn.text = "Next: Repository";
        }

        // ── Step 5: Repository Path ──

        private async Task StepRepoPath()
        {
            _stepLabel.text = "Step 5: Repository Path";

            var repoPath = LoreSettings.RepoPath;
            var displayPath = repoPath ?? Path.GetDirectoryName(Application.dataPath);

            Log($"Repository path: {displayPath}");
            var result = await LoreCliService.ExecuteAsync("status", "--revision-only");
            if (result.ExitCode == 0)
            {
                Log("✓ Valid Lore repository.");
                var oneline = result.Output?.Split('\n').FirstOrDefault(l => l.Contains("On branch")) ?? "";
                if (!string.IsNullOrEmpty(oneline)) Log($"  {oneline.Trim()}");
            }
            else
            {
                Log($"⚠ Not a Lore repository (exit {result.ExitCode}):");
                var lines = (result.Output ?? "").Split('\n');
                foreach (var l in lines.Take(3))
                    Log($"  {l.Trim()}");
                Log("");
                Log("→ Either change the path in Edit → Preferences → Lore,");
                Log("  or init a new repo with `lore repository create`");
            }

            _actionBtn.text = "Finish";
        }

        // ── Done ──

        private void StepDone()
        {
            _stepLabel.text = "Setup Complete ✓";
            Log("");
            Log("Lore VCS is configured.");
            Log("• Open window: Lore → Lore Window");
            Log("• Settings: Edit → Preferences → Lore");
            Log("• Quick stage/commit/push from the Lore window");

            LoreSettings.FirstRunDone = true;
            LoreSettings.AutoStartServer = true;

            _actionBtn.text = "Done";
            _actionBtn.SetEnabled(true);
            _actionBtn.clicked -= RunNextStep;
            _actionBtn.clicked += () => Close();

            _closeBtn.text = "Open Lore Window";
            _closeBtn.clicked -= Close;
            _closeBtn.clicked += () =>
            {
                Close();
                EditorApplication.delayCall += LoreWindow.ShowWindow;
            };
        }

        private void Log(string msg)
        {
            var label = new Label(msg)
            {
                style = { fontSize = 11, marginBottom = 2, whiteSpace = WhiteSpace.Normal }
            };
            _detailsContent.Add(label);
            label.BringToFront();
        }
    }
}
