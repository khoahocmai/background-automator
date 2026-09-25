namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Evaluates live confirmation prompt snapshots against configured CommandApprovalRules.
/// Implements strict fail-closed safety semantics.
/// </summary>
public static class CommandApprovalEvaluator
{
    /// <summary>
    /// Evaluates whether the current prompt snapshot meets all safety criteria of the approval rule.
    /// </summary>
    /// <param name="rule">Configured approval rule.</param>
    /// <param name="snapshot">Snapshot of current terminal prompt state.</param>
    /// <param name="actualProcessName">Actual process name of target window.</param>
    /// <param name="actualWindowClass">Actual window class name of target window.</param>
    /// <returns>An <see cref="ApprovalDecision"/> indicating Allowed or Blocked with reason.</returns>
    public static ApprovalDecision Evaluate(
        CommandApprovalRule rule,
        CommandPromptSnapshot snapshot,
        string? actualProcessName = null,
        string? actualWindowClass = null)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        if (!rule.Enabled)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.CommandNotAllowed,
                $"Approval rule '{rule.Name}' is disabled.");
        }

        // 1. Target process matching
        if (!string.IsNullOrWhiteSpace(rule.ExpectedProcess) && !string.IsNullOrWhiteSpace(actualProcessName))
        {
            string expectedProc = Path.GetFileNameWithoutExtension(rule.ExpectedProcess.Trim());
            string actualProc = Path.GetFileNameWithoutExtension(actualProcessName.Trim());

            if (!string.Equals(expectedProc, actualProc, StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalDecision.Blocked(
                    ApprovalBlockReason.TargetMismatch,
                    $"Target process '{actualProcessName}' does not match expected '{rule.ExpectedProcess}'.");
            }
        }

        // 2. Window class matching (optional)
        if (!string.IsNullOrWhiteSpace(rule.ExpectedWindowClass) && !string.IsNullOrWhiteSpace(actualWindowClass))
        {
            if (!actualWindowClass.Contains(rule.ExpectedWindowClass.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalDecision.Blocked(
                    ApprovalBlockReason.TargetMismatch,
                    $"Window class '{actualWindowClass}' does not match expected '{rule.ExpectedWindowClass}'.");
            }
        }

        // 3. Approval prompt visibility
        if (!snapshot.IsApprovalPromptVisible)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.PromptNotVisible,
                $"Confirmation prompt '{rule.ExpectedPrompt}' is not visible.");
        }

        // 4. Expected approval option selection
        if (!snapshot.IsYesOptionSelected)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.OptionNotSelected,
                $"Expected option '{rule.ExpectedSelectedOption}' is not currently selected.");
        }

        // 5. Command availability
        if (string.IsNullOrWhiteSpace(snapshot.CommandText))
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.CommandNotFound,
                "Could not extract command from visible prompt area.");
        }

        // 6. Explicit command allowlist matching (Exact mode)
        string extracted = snapshot.CommandText.Trim();
        string allowed = rule.AllowedCommand?.Trim() ?? string.Empty;

        bool matches = rule.CommandMatchMode switch
        {
            CommandMatchMode.Exact => string.Equals(extracted, allowed, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(extracted, allowed, StringComparison.OrdinalIgnoreCase)
        };

        if (!matches)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.CommandNotAllowed,
                $"Extracted command \"{extracted}\" is not allowed by rule '{rule.Name}' (Allowed: \"{allowed}\").");
        }

        return ApprovalDecision.Allowed(
            $"Command \"{extracted}\" explicitly approved by rule '{rule.Name}'.");
    }
}
