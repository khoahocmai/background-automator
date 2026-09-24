using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.Tests;

public class TargetRestartIntegrationTests : IDisposable
{
    private readonly string _tempProfileDir;
    private readonly ProfileStorageService _storage;

    public TargetRestartIntegrationTests()
    {
        _tempProfileDir = Path.Combine(Path.GetTempPath(), "TargetRestartTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempProfileDir);
        _storage = new ProfileStorageService(_tempProfileDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempProfileDir))
                Directory.Delete(_tempProfileDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void Profile_SurvivesProcessRestart_ReResolvesNewHwndAndExecutesClick()
    {
        IntPtr oldHwnd;

        // Step 1: Launch Process 1, record target descriptor, and save to profile
        using (var fixture1 = new TestTargetFixture())
        {
            oldHwnd = fixture1.MainWindowHandle;
            Assert.True(User32.IsWindow(oldHwnd));

            var coordServiceInit = new CoordinateService();
            var targetServiceInit = new WindowTargetService(coordServiceInit);
            var candidates = targetServiceInit.EnumerateTopLevelWindows();
            var candidate1 = candidates.First(c => c.ProcessId == fixture1.Process.Id);
            var target1 = targetServiceInit.ResolveTargetFromCandidate(candidate1)!;

            var descriptor = TargetDescriptor.FromWindowTarget(target1, TitleMatchMode.Contains);

            var profile = new ProfileModel
            {
                Name = "RestartableTestTargetProfile",
                Mode = ProfileMode.Simple,
                Target = descriptor,
                ClickPoints = new List<ClickPointConfig>
                {
                    new(50, 50, ClickType.Single, "Main Point")
                }
            };

            _storage.SaveProfile(profile);
        } // fixture1 exits and window is destroyed

        // Step 2: Verify old HWND is dead
        Thread.Sleep(200);
        Assert.False(User32.IsWindow(oldHwnd), "Old HWND should be closed after process termination.");

        // Step 3: Launch Process 2 (new PID, new HWND)
        using (var fixture2 = new TestTargetFixture())
        {
            IntPtr newHwnd = fixture2.MainWindowHandle;
            Assert.True(User32.IsWindow(newHwnd));
            Assert.NotEqual(oldHwnd, newHwnd);

            // Step 4: Load profile from disk (contains NO live HWND)
            var loadedProfile = _storage.LoadProfileByName("RestartableTestTargetProfile");
            Assert.NotNull(loadedProfile.Target);

            // Step 5: Re-resolve target against current desktop windows
            var coordService = new CoordinateService();
            var targetService = new WindowTargetService(coordService);
            var resolver = new TargetResolver(targetService, coordService);

            var resolutionResult = resolver.Resolve(loadedProfile.Target);

            Assert.True(resolutionResult.IsSuccess, $"Failed to re-resolve target: {resolutionResult.Message}");
            Assert.NotNull(resolutionResult.Target);
            Assert.Equal(newHwnd, resolutionResult.Target.RootHwnd);
            Assert.Equal(newHwnd, resolutionResult.Target.TargetHwnd);

            // Step 6: Execute click against freshly re-resolved target
            var clicker = new BackgroundClickerEngine();
            var targetPoint = new TargetPoint(resolutionResult.Target.TargetHwnd, 50, 50);
            var clickResult = clicker.Click(targetPoint);

            Assert.Equal(ClickResult.Success, clickResult);

            // Allow message queue to process and flush log
            Thread.Sleep(300);

            // Step 7: Verify click was received by Process 2
            Assert.True(File.Exists(fixture2.LogFilePath));
            string log = File.ReadAllText(fixture2.LogFilePath);
            Assert.Contains("WM_LBUTTONDOWN", log);
            Assert.Contains("WM_LBUTTONUP", log);
        }
    }
}
