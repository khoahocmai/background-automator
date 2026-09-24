using Xunit;

// Disable test parallelization because native Win32 UI tests create top-level windows
// on the shared Windows Desktop and query global screen coordinates via WindowFromPoint.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
