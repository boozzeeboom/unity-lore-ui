using System.Threading.Tasks;
using NUnit.Framework;
using ProjectC.LoreUnity;

namespace ProjectC.LoreUnity.Tests.Editor
{
    /// <summary>
    /// Tests for LoreCliService. These are integration tests that
    /// require lore.exe to be available in PATH or configured in settings.
    /// Marked as explicit — run manually when lore is available.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public class LoreCliServiceTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Ensure repo path is set for service calls
            if (string.IsNullOrEmpty(LoreSettings.RepoPath))
            {
                LoreSettings.RepoPath = TestContext.Parameters["RepoPath"]
                    ?? System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
            }
        }

        [Test]
        [Explicit("Requires lore.exe in PATH")]
        public void FindLoreInPath_ReturnsPath()
        {
            var path = LoreCliService.FindLoreInPath();
            Assert.IsNotNull(path, "lore.exe should be found in PATH");
            TestContext.WriteLine($"Found lore at: {path}");
        }

        [Test]
        [Explicit("Requires lore.exe configured")]
        public async Task ExecuteAsync_Version_ReturnsZeroExitCode()
        {
            var (code, output) = await LoreCliService.ExecuteAsync("--version");
            Assert.AreEqual(0, code);
            TestContext.WriteLine($"Lore version output:\n{output}");
        }

        [Test]
        [Explicit("Requires valid Lore repository")]
        public async Task GetStatusAsync_ReturnsStatus()
        {
            var status = await LoreCliService.GetStatusAsync();
            Assert.IsNotNull(status, "Status should not be null");
            Assert.IsNotNull(status.BranchName, "Branch name should not be null");
            TestContext.WriteLine($"Branch: {status.BranchName}, Rev: {status.CurrentRevision}");
        }

        [Test]
        [Explicit("Requires valid Lore repository")]
        public async Task GetHistoryAsync_ReturnsHistory()
        {
            var history = await LoreCliService.GetHistoryAsync(5);
            Assert.IsNotNull(history);
            TestContext.WriteLine($"Got {history.Count} commits");
        }

        [Test]
        [Explicit("Requires valid Lore repository")]
        public async Task GetBranchesAsync_ReturnsBranches()
        {
            var branches = await LoreCliService.GetBranchesAsync();
            Assert.IsNotNull(branches);
            TestContext.WriteLine($"Got {branches.Count} branches");
        }

        [Test]
        [Explicit("Requires valid Lore repository")]
        public async Task GetRepositoryInfoAsync_ReturnsInfo()
        {
            var info = await LoreCliService.GetRepositoryInfoAsync();
            Assert.IsNotNull(info);
            TestContext.WriteLine($"Repo: {info.Name}, URL: {info.RemoteUrl}");
        }

        [Test]
        [Explicit("Requires valid Lore repository")]
        public async Task HealthCheckAsync_ReturnsTrue()
        {
            var healthy = await LoreCliService.HealthCheckAsync();
            Assert.IsTrue(healthy, "Lore CLI health check should pass");
        }
    }
}
