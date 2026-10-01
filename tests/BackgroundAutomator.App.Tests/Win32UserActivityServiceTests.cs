using BackgroundAutomator.App.Services;
using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.App.Tests;

public class Win32UserActivityServiceTests
{
    [Fact]
    public void Section28_SyntheticEnter_DoesNotCauseFalseUserActivity()
    {
        // Setup: pre-injection physical idle = 5 seconds
        // Tick timeline:
        // Input occurred at t=1000, current time t=6000 (5s idle).
        uint simulatedTick = 6000;
        uint lastInputTime = 1000;

        var service = new Win32UserActivityService(
            (ref LASTINPUTINFO lii) => { lii.dwTime = lastInputTime; return true; },
            () => simulatedTick);

        Assert.True(service.TryGetIdleDuration(out var preIdle));
        Assert.Equal(TimeSpan.FromSeconds(5), preIdle);

        // Inject Enter at t=6000. SendInput updates lastInputTime to 6000.
        lastInputTime = 6000;
        service.NotifyInputInjected(preIdle);

        // 200ms elapsed since injection without any user input:
        simulatedTick = 6200;

        Assert.True(service.TryGetIdleDuration(out var idleAfter));
        // Idle duration should continue approximately as 5 sec + elapsed (>= 5 sec)
        Assert.True(idleAfter >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Section29_PhysicalInput_50msAfterEnter_DetectedAsPhysical()
    {
        // Injected Enter at t=1000
        uint simulatedTick = 1000;
        uint lastInputTime = 1000;

        var service = new Win32UserActivityService(
            (ref LASTINPUTINFO lii) => { lii.dwTime = lastInputTime; return true; },
            () => simulatedTick);

        service.NotifyInputInjected(TimeSpan.FromSeconds(5));

        // 50ms later, real physical-input tick appears at t=1050
        simulatedTick = 1050;
        lastInputTime = 1050;

        Assert.True(service.TryGetIdleDuration(out var idle));

        // Must NOT be classified as synthetic! Idle duration must reflect real input (0ms)
        Assert.Equal(TimeSpan.Zero, idle);

        // Subsequent check at t=1100 with no further input should report 50ms elapsed since physical click
        simulatedTick = 1100;
        Assert.True(service.TryGetIdleDuration(out var idle2));
        Assert.Equal(TimeSpan.FromMilliseconds(50), idle2);
    }

    [Fact]
    public void Section30_PhysicalInput_ImmediatelyAfterEnter_DistinctTick_Detected()
    {
        // Injected Enter at t=5000
        uint simulatedTick = 5000;
        uint lastInputTime = 5000;

        var service = new Win32UserActivityService(
            (ref LASTINPUTINFO lii) => { lii.dwTime = lastInputTime; return true; },
            () => simulatedTick);

        service.NotifyInputInjected(TimeSpan.FromSeconds(3));

        // Distinct tick X + 1 (5001)
        simulatedTick = 5001;
        lastInputTime = 5001;

        Assert.True(service.TryGetIdleDuration(out var idle));
        Assert.Equal(TimeSpan.Zero, idle);
    }

    [Fact]
    public void Section31_SameSyntheticTick_AppliesSyntheticCorrection()
    {
        uint simulatedTick = 2000;
        uint lastInputTime = 2000;

        var service = new Win32UserActivityService(
            (ref LASTINPUTINFO lii) => { lii.dwTime = lastInputTime; return true; },
            () => simulatedTick);

        service.NotifyInputInjected(TimeSpan.FromSeconds(4));

        // While LASTINPUTINFO remains exactly equal to recorded injected tick:
        simulatedTick = 2300;
        Assert.True(service.TryGetIdleDuration(out var idle));
        Assert.True(idle >= TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Section32_NextSyntheticEnter_ReplacesMarkerCleanly()
    {
        uint simulatedTick = 1000;
        uint lastInputTime = 1000;

        var service = new Win32UserActivityService(
            (ref LASTINPUTINFO lii) => { lii.dwTime = lastInputTime; return true; },
            () => simulatedTick);

        // Enter A at t=1000
        service.NotifyInputInjected(TimeSpan.FromSeconds(2));

        // Real input arrives at t=1200
        simulatedTick = 1200;
        lastInputTime = 1200;
        Assert.True(service.TryGetIdleDuration(out var idleAfterPhysical));
        Assert.Equal(TimeSpan.Zero, idleAfterPhysical);

        // Enter B at t=3000
        simulatedTick = 3000;
        lastInputTime = 3000;
        service.NotifyInputInjected(TimeSpan.FromMilliseconds(1800));

        // Marker for B is active:
        simulatedTick = 3100;
        Assert.True(service.TryGetIdleDuration(out var idleAfterB));
        Assert.True(idleAfterB >= TimeSpan.FromMilliseconds(1800));

        // Physical input at t=3150 clears B marker
        simulatedTick = 3150;
        lastInputTime = 3150;
        Assert.True(service.TryGetIdleDuration(out var idlePhysical2));
        Assert.Equal(TimeSpan.Zero, idlePhysical2);
    }
}
