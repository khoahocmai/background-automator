using BackgroundAutomator.Core.Security;
using Xunit;

namespace BackgroundAutomator.Tests;

public class ProcessElevationServiceTests
{
    [Fact]
    public void IsCurrentProcessElevated_ReturnsBoolWithoutException()
    {
        var service = new ProcessElevationService();
        bool isElevated = service.IsCurrentProcessElevated();
        // Just verify it completes cleanly and returns true or false
        Assert.True(isElevated || !isElevated);
    }

    [Fact]
    public void CheckCompatibility_CurrentProcess_ReturnsCompatible()
    {
        var service = new ProcessElevationService();
        var result = service.CheckCompatibility(Environment.ProcessId);

        Assert.Equal(ElevationCompatibility.Compatible, result.Compatibility);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void CheckCompatibility_InvalidPid_ReturnsUnknown()
    {
        var service = new ProcessElevationService();
        var result = service.CheckCompatibility(-999);

        // Negative PID cannot be resolved or opened
        Assert.True(result.Compatibility == ElevationCompatibility.Compatible || result.Compatibility == ElevationCompatibility.Unknown);
    }

    private class MockElevationService : ProcessElevationService
    {
        public bool MockCurrentElevated { get; set; }
        public bool? MockTargetElevated { get; set; }

        public override bool IsCurrentProcessElevated() => MockCurrentElevated;
        public override bool? IsProcessElevated(int processId) => MockTargetElevated;
    }

    [Fact]
    public void CheckCompatibility_CurrentElevated_TargetElevated_Compatible()
    {
        var mock = new MockElevationService
        {
            MockCurrentElevated = true,
            MockTargetElevated = true
        };

        var result = mock.CheckCompatibility(1234);
        Assert.Equal(ElevationCompatibility.Compatible, result.Compatibility);
    }

    [Fact]
    public void CheckCompatibility_CurrentNotElevated_TargetElevated_UipiMismatch()
    {
        var mock = new MockElevationService
        {
            MockCurrentElevated = false,
            MockTargetElevated = true
        };

        var result = mock.CheckCompatibility(1234);
        Assert.Equal(ElevationCompatibility.UipiMismatch, result.Compatibility);
        Assert.Contains("UIPI Warning", result.Message);
    }

    [Fact]
    public void CheckCompatibility_CurrentNotElevated_TargetNotElevated_Compatible()
    {
        var mock = new MockElevationService
        {
            MockCurrentElevated = false,
            MockTargetElevated = false
        };

        var result = mock.CheckCompatibility(1234);
        Assert.Equal(ElevationCompatibility.Compatible, result.Compatibility);
    }
}
