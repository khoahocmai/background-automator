using System.Drawing;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.Targeting;

/// <summary>
/// Service responsible for locating and binding a live window/control based on a durable TargetDescriptor.
/// Correctly distinguishes unambiguous matches, zero matches (NotFound), and multiple matches (Ambiguous).
/// </summary>
public class TargetResolver
{
    private readonly WindowTargetService _targetService;
    private readonly CoordinateService _coordinateService;
    private readonly IAppLogger? _logger;

    public TargetResolver(WindowTargetService targetService, CoordinateService coordinateService, IAppLogger? logger = null)
    {
        _targetService = targetService;
        _coordinateService = coordinateService;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a live HWND and WindowTarget from a TargetDescriptor.
    /// </summary>
    public virtual TargetResolutionResult Resolve(TargetDescriptor descriptor, IntPtr ignoreHwnd = default)
    {
        if (descriptor == null)
            return TargetResolutionResult.CreateInvalid("TargetDescriptor cannot be null.");

        if (string.IsNullOrWhiteSpace(descriptor.ProcessName) && string.IsNullOrWhiteSpace(descriptor.WindowTitle))
        {
            return TargetResolutionResult.CreateInvalid("TargetDescriptor requires at least a ProcessName or WindowTitle.");
        }

        var allCandidates = _targetService.EnumerateTopLevelWindows(ignoreHwnd);
        var matches = new List<WindowTargetCandidate>();

        foreach (var c in allCandidates)
        {
            if (MatchesCandidate(c, descriptor))
            {
                matches.Add(c);
            }
        }

        if (matches.Count == 0)
        {
            _logger?.Debug($"TargetResolver: No window matching process='{descriptor.ProcessName}', title='{descriptor.WindowTitle}'");
            return TargetResolutionResult.CreateNotFound(
                $"No window matching process='{descriptor.ProcessName}', title='{descriptor.WindowTitle}' was found.");
        }

        if (matches.Count > 1)
        {
            _logger?.Warning($"TargetResolver: Ambiguous target resolution - found {matches.Count} matching windows.");
            return TargetResolutionResult.CreateAmbiguous(matches);
        }

        var rootCandidate = matches[0];
        IntPtr rootHwnd = rootCandidate.Hwnd;
        IntPtr targetHwnd = rootHwnd;
        IntPtr parentHwnd = IntPtr.Zero;
        string targetTitle = rootCandidate.WindowTitle;
        string targetClass = rootCandidate.WindowClass;

        // Child control resolution if child descriptor is specified
        if (descriptor.ChildDescriptor != null)
        {
            var childMatches = FindChildMatches(rootHwnd, descriptor.ChildDescriptor);
            if (childMatches.Count == 0)
            {
                _logger?.Warning($"TargetResolver: Root window found (HWND {HwndFormatter.Format(rootHwnd)}) but child control matching class='{descriptor.ChildDescriptor.ControlClass}' text='{descriptor.ChildDescriptor.ControlText}' not found.");
                return TargetResolutionResult.CreateNotFound(
                    $"Root window found (HWND {HwndFormatter.Format(rootHwnd)}) but child control matching class='{descriptor.ChildDescriptor.ControlClass}' text='{descriptor.ChildDescriptor.ControlText}' was not found.");
            }

            if (childMatches.Count > 1)
            {
                _logger?.Warning($"TargetResolver: Root window found (HWND {HwndFormatter.Format(rootHwnd)}) but found {childMatches.Count} matching child controls. Target is ambiguous.");
                var childCandidates = childMatches.Select(h => new WindowTargetCandidate(
                    h,
                    rootCandidate.ProcessId,
                    rootCandidate.ThreadId,
                    rootCandidate.ProcessName,
                    User32.GetWindowTextSafe(h),
                    User32.GetClassNameSafe(h)
                )).ToList();
                return TargetResolutionResult.CreateAmbiguous(childCandidates,
                    $"Found {childMatches.Count} matching child controls in root window (HWND {HwndFormatter.Format(rootHwnd)}). Ambiguity must be resolved before targeting.");
            }

            targetHwnd = childMatches[0];
            parentHwnd = User32.GetParent(targetHwnd);
            targetTitle = User32.GetWindowTextSafe(targetHwnd);
            targetClass = User32.GetClassNameSafe(targetHwnd);
        }

        // Determine coordinates
        Point clientCoords;
        if (descriptor.SavedClientX.HasValue && descriptor.SavedClientY.HasValue)
        {
            clientCoords = new Point(descriptor.SavedClientX.Value, descriptor.SavedClientY.Value);
        }
        else
        {
            // Default to center of client rectangle
            clientCoords = Point.Empty;
            if (User32.GetClientRect(targetHwnd, out RECT clientRect))
            {
                clientCoords = new Point(clientRect.Width / 2, clientRect.Height / 2);
            }
        }

        var clientPoint = new TargetPoint(targetHwnd, clientCoords.X, clientCoords.Y);
        Point screenPoint = _coordinateService.ClientToScreen(clientPoint);

        var resolvedTarget = new WindowTarget
        {
            RootHwnd = rootHwnd,
            TargetHwnd = targetHwnd,
            ParentHwnd = parentHwnd,
            ProcessId = rootCandidate.ProcessId,
            ThreadId = rootCandidate.ThreadId,
            ProcessName = rootCandidate.ProcessName,
            WindowTitle = !string.IsNullOrWhiteSpace(targetTitle) ? targetTitle : rootCandidate.WindowTitle,
            WindowClass = targetClass,
            ScreenPoint = screenPoint,
            ClientPoint = clientPoint
        };

        _logger?.Info($"TargetResolver: Successfully resolved target to HWND {HwndFormatter.Format(targetHwnd)} (Process: {resolvedTarget.ProcessName}, PID: {resolvedTarget.ProcessId})");
        return TargetResolutionResult.CreateSuccess(resolvedTarget);
    }

    private static bool MatchesCandidate(WindowTargetCandidate c, TargetDescriptor d)
    {
        // 1. Process Name matching (case-insensitive, ignoring optional .exe suffix)
        if (!string.IsNullOrWhiteSpace(d.ProcessName))
        {
            string candProc = NormalizeProcessName(c.ProcessName);
            string descProc = NormalizeProcessName(d.ProcessName);
            if (!string.Equals(candProc, descProc, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // 2. Window Class matching (if specified)
        if (!string.IsNullOrWhiteSpace(d.WindowClass))
        {
            if (!string.Equals(c.WindowClass, d.WindowClass, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // 3. Window Title matching
        if (d.MatchMode != TitleMatchMode.Any)
        {
            if (string.IsNullOrEmpty(d.WindowTitle))
                return true;

            switch (d.MatchMode)
            {
                case TitleMatchMode.Exact:
                    return string.Equals(c.WindowTitle, d.WindowTitle, StringComparison.Ordinal);

                case TitleMatchMode.Contains:
                    return c.WindowTitle.Contains(d.WindowTitle, StringComparison.OrdinalIgnoreCase);

                case TitleMatchMode.StartsWith:
                    return c.WindowTitle.StartsWith(d.WindowTitle, StringComparison.OrdinalIgnoreCase);

                default:
                    return true;
            }
        }

        return true;
    }

    private static string NormalizeProcessName(string procName)
    {
        if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return procName[..^4];
        return procName;
    }

    private static List<IntPtr> FindChildMatches(IntPtr rootHwnd, ChildTargetDescriptor childDesc)
    {
        var matched = new List<IntPtr>();
        if (rootHwnd == IntPtr.Zero || !User32.IsWindow(rootHwnd))
            return matched;

        User32.EnumChildWindows(rootHwnd, (hWnd, lParam) =>
        {
            if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
                return true;

            if (!string.IsNullOrWhiteSpace(childDesc.ControlClass))
            {
                string className = User32.GetClassNameSafe(hWnd);
                if (!string.Equals(className, childDesc.ControlClass, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (!string.IsNullOrWhiteSpace(childDesc.ControlText))
            {
                string text = User32.GetWindowTextSafe(hWnd);
                if (!string.Equals(text, childDesc.ControlText, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            matched.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        return matched;
    }
}
