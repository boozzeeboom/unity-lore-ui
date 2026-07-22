using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectC.LoreUnity
{
    /// <summary>
    /// First-run wizard. Guides user through:
    /// 1. Finding/installing lore.exe
    /// 2. Finding/installing loreserver.exe
    /// 3. Starting the server
    /// 4. Configuring the repository path
    /// </summary>
    public class LoreInstallWizard : EditorWindow
    {
        [MenuItem("Lore/Install Lore Server", false, 100)]
        public static void ShowWindow()
        {
            var wnd = GetWindow<LoreInstallWizard>(true, "Lore VCS — Setup Wizard", true);
            wnd.minSize = new Vector2(500, 380);
            wnd.maxSize = new Vector2(500, 380);
        }

        private Label _stepLabel;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private Button _actionBtn;
        private Button _closeBtn;
        private VisualElement _detailsBox;

        private int _currentStep;

        private void CreateGUI()
        {
            var root = rootVisualElement;

            // Title
            root.Add(new Label("Lore VCS Setup Wizard")
            {
                style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold,
                          padding = 10, marginBottom = 6 }
            });

            // Step indicator
            _stepLabel = new Label("Step 1: Check Lore CLI") { style = { padding = 4, fontSize = 13 } };
            root.Add(_stepLabel);

            // Progress
            _progressBar = new ProgressBar { value = 0, style = { height = 6, margin = 4 } };
            root.Add(_progressBar);

            // Details box
            _detailsBox = new VisualElement
            {
                style = { backgroundColor = new Color(0.12f, 0.12f, 0.12f),
                          padding = 8, margin = 4, minHeight = 120,
                          whiteSpace = WhiteSpace.Normal }
            };
            _detailsBox.Add(new Label("Checking for Lore CLI..."));
            root.Add(_detailsBox);

            // Status
            _statusLabel = new Label("") { style = { padding = 4, fontSize = 11, color = Color.gray } };
            root.Add(_statusLabel);

            // Bottom buttons
            var btnRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd,
                          padding = 6, marginTop = 8 }
            };

            _actionBtn = new Button(RunNextStep) { text = "Start", style = { width = 120 } };
            btnRow.Add(_actionBtn);

            _closeBtn = new Button(() => Close()) { text = "Close", style = { width = 80, marginLeft = 4 } };
            btnRow.Add(_closeBtn);

            root.Add(btnRow);

            // Start first check on delay
            EditorApplication.delayCall += RunNextStep;
        }

        private async void RunNextStep()
        {
            _actionBtn.SetEnabled(false);

            switch (_currentStep)
            {
                case 0: await StepCheckLore(); break;
                case 1: await StepInstallOrFind(); break;
                case 2: await StepStartServer(); break;
                case 3: await StepRepoPath(); break;
                case 4: StepDone(); break;
            }

            _currentStep++;
            _progressBar.value = (_currentStep / 5f) * 100;
        }

        private async Task StepCheckLore()
        {
            _stepLabel.text = "Step 1: Check Lore CLI";
            Log("Checking if lore.exe is available...");

            var lorePath = LoreCliService.ResolveLorePath();
            if (lorePath != null)
            {
                Log($"✓ Found lore.exe at: {lorePath}");
                var (code, _) = await LoreCliService.ExecuteAsync("--version");
                Log(code == 0 ? "✓ lore.exe responds" : "⚠ lore.exe found but has errors");
            }
            else
            {
                Log("⚠ lore.exe not found in PATH or settings.");
                Log("→ Will download and install in next step.");
            }

            _actionBtn.text = "Next: Install";
        }

        private async Task StepInstallOrFind()
        {
            _stepLabel.text = "Step 2: Install / Find Lore";

            var lorePath = LoreCliService.ResolveLorePath();
            if (lorePath != null)
            {
                Log("✓ lore.exe already available. Skipping download.");
                // Check server too
                var serverPath = LoreCliService.ResolveServerPath();
                if (serverPath != null)
                    Log($"✓ loreserver.exe at: {serverPath}");
                else
                    Log("⚠ loreserver.exe not found — will need server for push/sync.");
            }
            else
            {
                Log("Downloading Lore CLI from GitHub...");
                var (success, message) = await LoreServerManager.DownloadAndInstallAsync();
                if (success)
                {
                    Log($"✓ Installed to: {message}");
                    Log($"✓ lore.exe: {LoreSettings.LoreExePath}");
                    Log($"✓ loreserver.exe: {LoreSettings.ServerExePath}");
                }
                else
                {
                    Log($"✗ {message}");
                    Log("→ You can also download manually from:");
                    Log("  https://github.com/EpicGames/lore/releases");
                    Log("  Then set paths in Edit → Preferences → Lore.");
                }
            }

            _actionBtn.text = "Next: Start Server";
        }

        private async Task StepStartServer()
        {
            _stepLabel.text = "Step 3: Start Server";

            var alive = await LoreServerManager.HealthCheckAsync();
            if (alive)
            {
                Log("✓ Server is already running.");
            }
            else
            {
                Log("Starting loreserver...");
                var started = await LoreServerManager.StartServerAsync();
                if (started)
                    Log($"✓ Server started at {LoreSettings.ServerUrl}");
                else
                    Log("⚠ Could not start server. You can start it later from the Lore window.");
            }

            _actionBtn.text = "Next: Repository";
        }

        private async Task StepRepoPath()
        {
            _stepLabel.text = "Step 4: Repository Path";

            var repoPath = LoreSettings.RepoPath;
            var testPath = repoPath ?? System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);

            Log($"Repository path: {testPath}");
            var (code, output) = await LoreCliService.ExecuteAsync("status", "--revision-only");
            if (code == 0)
            {
                Log("✓ Valid Lore repository.");
                Log(output);
            }
            else
            {
                Log($"⚠ Not a Lore repository (exit {code}):");
                Log(output);
                Log("→ You can initialize one or point to an existing clone.");
                Log("→ Change path in Edit → Preferences → Lore → Repository Path.");
            }

            _actionBtn.text = "Finish";
        }

        private void StepDone()
        {
            _stepLabel.text = "Setup Complete ✓";
            Log("Lore VCS is configured.");
            Log("Open the Lore window: Lore → Lore Window");
            Log("Or access settings: Edit → Preferences → Lore");

            LoreSettings.FirstRunDone = true;
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
            _detailsBox.Add(label);
            _detailsBox.ScrollTo(label);
        }
    }
}
