namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Evaluates live confirmation prompt snapshots against configured CommandApprovalRules or ApprovalRuleSets.
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
    /// <param name="policyMode">Approval policy mode (ExactRules or FoolMode).</param>
    /// <returns>An <see cref="ApprovalDecision"/> indicating Allowed or Blocked with reason.</returns>
    public static ApprovalDecision Evaluate(
        CommandApprovalRule rule,
        CommandPromptSnapshot snapshot,
        string? actualProcessName = null,
        string? actualWindowClass = null,
        ApprovalPolicyMode policyMode = ApprovalPolicyMode.ExactRules)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        if (policyMode == ApprovalPolicyMode.ExactRules && !rule.Enabled)
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

        // If in FOOL MODE, command allowlist matching is bypassed.
        // Prompt structure, target identity, and selected option have all been verified.
        // We decouple permission envelope recognition from single-line command extraction.
        if (policyMode == ApprovalPolicyMode.FoolMode)
        {
            if (snapshot.Envelope != null)
            {
                if (!snapshot.Envelope.IsStructurallyValid)
                {
                    if (snapshot.Envelope.IsAmbiguous)
                    {
                        return ApprovalDecision.Blocked(
                            ApprovalBlockReason.AmbiguousPrompt,
                            snapshot.Envelope.AmbiguityReason ?? "Malformed permission prompt envelope boundaries.");
                    }

                    return ApprovalDecision.Blocked(
                        ApprovalBlockReason.CommandNotFound,
                        snapshot.Envelope.ParseFailureReason ?? "Could not extract command from visible prompt area.");
                }

                if (string.IsNullOrWhiteSpace(snapshot.Envelope.RawCommandBlock))
                {
                    return ApprovalDecision.Blocked(
                        ApprovalBlockReason.CommandNotFound,
                        "Could not extract command from visible prompt area.");
                }

                string preview = snapshot.Envelope.GetCommandPreview(100);
                return ApprovalDecision.Allowed(
                    $"Unrestricted approval granted under FOOL MODE. Command: \"{preview}\"",
                    matchedRuleId: null,
                    matchedRuleName: "FOOL MODE");
            }

            // Fallback for prompts without explicit permission envelopes
            if (snapshot.IsAmbiguous)
            {
                return ApprovalDecision.Blocked(
                    ApprovalBlockReason.AmbiguousPrompt,
                    snapshot.AmbiguityReason ?? "Multiple conflicting prompts or ambiguous command candidates detected.");
            }

            if (string.IsNullOrWhiteSpace(snapshot.CommandText))
            {
                return ApprovalDecision.Blocked(
                    ApprovalBlockReason.CommandNotFound,
                    "Could not extract command from visible prompt area.");
            }

            string cmdDesc = snapshot.CommandText.Trim();
            return ApprovalDecision.Allowed(
                $"Unrestricted approval granted under FOOL MODE. Command: \"{cmdDesc}\"",
                matchedRuleId: null,
                matchedRuleName: "FOOL MODE");
        }

        // 5. Fail-closed ambiguity check for ExactRules
        if (snapshot.IsAmbiguous)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.AmbiguousPrompt,
                snapshot.AmbiguityReason ?? "Multiple conflicting prompts or ambiguous command candidates detected.");
        }

        // 6. Command availability for ExactRules
        if (string.IsNullOrWhiteSpace(snapshot.CommandText))
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.CommandNotFound,
                "Could not extract command from visible prompt area.");
        }

        // 7. Explicit command allowlist matching (Exact mode)
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
            $"Command \"{extracted}\" explicitly approved by rule '{rule.Name}'.",
            rule.Id,
            rule.Name);
    }

    /// <summary>
    /// Evaluates whether the current prompt snapshot meets all safety criteria of the approval rule set.
    /// </summary>
    /// <param name="ruleSet">Configured approval rule set.</param>
    /// <param name="snapshot">Snapshot of current terminal prompt state.</param>
    /// <param name="actualProcessName">Actual process name of target window.</param>
    /// <param name="actualWindowClass">Actual window class name of target window.</param>
    /// <param name="policyMode">Approval policy mode (ExactRules or FoolMode).</param>
    /// <returns>An <see cref="ApprovalDecision"/> indicating Allowed or Blocked with reason.</returns>
    public static ApprovalDecision Evaluate(
        ApprovalRuleSet ruleSet,
        CommandPromptSnapshot snapshot,
        string? actualProcessName = null,
        string? actualWindowClass = null,
        ApprovalPolicyMode policyMode = ApprovalPolicyMode.ExactRules)
    {
        if (ruleSet == null)
            throw new ArgumentNullException(nameof(ruleSet));
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        // 1. Target process matching
        if (!string.IsNullOrWhiteSpace(ruleSet.ExpectedProcess) && !string.IsNullOrWhiteSpace(actualProcessName))
        {
            string expectedProc = Path.GetFileNameWithoutExtension(ruleSet.ExpectedProcess.Trim());
            string actualProc = Path.GetFileNameWithoutExtension(actualProcessName.Trim());

            if (!string.Equals(expectedProc, actualProc, StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalDecision.Blocked(
                    ApprovalBlockReason.TargetMismatch,
                    $"Target process '{actualProcessName}' does not match expected '{ruleSet.ExpectedProcess}'.");
            }
        }

        // 2. Window class matching (optional)
        if (!string.IsNullOrWhiteSpace(ruleSet.ExpectedWindowClass) && !string.IsNullOrWhiteSpace(actualWindowClass))
        {
            if (!actualWindowClass.Contains(ruleSet.ExpectedWindowClass.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalDecision.Blocked(
                    ApprovalBlockReason.TargetMismatch,
                    $"Window class '{actualWindowClass}' does not match expected '{ruleSet.ExpectedWindowClass}'.");
            }
        }

        // 3. Approval prompt visibility
        if (!snapshot.IsApprovalPromptVisible)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.PromptNotVisible,
                $"Confirmation prompt '{ruleSet.ExpectedPrompt}' is not visible.");
        }

        // 4. Expected approval option selection
        if (!snapshot.IsYesOptionSelected)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.OptionNotSelected,
                $"Expected option '{ruleSet.ExpectedSelectedOption}' is not currently selected.");
        }

        // If in FOOL MODE, command allowlist matching is bypassed.
        // Prompt structure, target identity, and selected option have all been verified.
        // We decouple permission envelope recognition from single-line command extraction.
        if (policyMode == ApprovalPolicyMode.FoolMode)
        {
            if (snapshot.Envelope != null)
            {
                if (!snapshot.Envelope.IsStructurallyValid)
                {
                    if (snapshot.Envelope.IsAmbiguous)
                    {
                        return ApprovalDecision.Blocked(
                            ApprovalBlockReason.AmbiguousPrompt,
                            snapshot.Envelope.AmbiguityReason ?? "Malformed permission prompt envelope boundaries.");
                    }

                    return ApprovalDecision.Blocked(
                        ApprovalBlockReason.CommandNotFound,
                        snapshot.Envelope.ParseFailureReason ?? "Could not extract command from visible prompt area.");
                }

                if (string.IsNullOrWhiteSpace(snapshot.Envelope.RawCommandBlock))
                {
                    return ApprovalDecision.Blocked(
                        ApprovalBlockReason.CommandNotFound,
                        "Could not extract command from visible prompt area.");
                }

                string preview = snapshot.Envelope.GetCommandPreview(100);
                return ApprovalDecision.Allowed(
                    $"Unrestricted approval granted under FOOL MODE. Command: \"{preview}\"",
                    matchedRuleId: null,
                    matchedRuleName: "FOOL MODE");
            }

            // Fallback for prompts without explicit permission envelopes
            if (snapshot.IsAmbiguous)
            {
                return ApprovalDecision.Blocked(
                    ApprovalBlockReason.AmbiguousPrompt,
                    snapshot.AmbiguityReason ?? "Multiple conflicting prompts or ambiguous command candidates detected.");
            }

            if (string.IsNullOrWhiteSpace(snapshot.CommandText))
            {
                return ApprovalDecision.Blocked(
                    ApprovalBlockReason.CommandNotFound,
                    "Could not extract command from visible prompt area.");
            }

            string cmdDesc = snapshot.CommandText.Trim();
            return ApprovalDecision.Allowed(
                $"Unrestricted approval granted under FOOL MODE. Command: \"{cmdDesc}\"",
                matchedRuleId: null,
                matchedRuleName: "FOOL MODE");
        }

        // 5. Fail-closed ambiguity check for ExactRules
        if (snapshot.IsAmbiguous)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.AmbiguousPrompt,
                snapshot.AmbiguityReason ?? "Multiple conflicting prompts or ambiguous command candidates detected.");
        }

        // 6. Command availability for ExactRules
        if (string.IsNullOrWhiteSpace(snapshot.CommandText))
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.CommandNotFound,
                "Could not extract command from visible prompt area.");
        }

        // 7. Explicit command allowlist matching across enabled rules in the set (Exact mode)
        var enabledRules = ruleSet.Rules.Where(r => r.Enabled).ToList();
        if (enabledRules.Count == 0)
        {
            return ApprovalDecision.Blocked(
                ApprovalBlockReason.CommandNotAllowed,
                $"Rule set '{ruleSet.Name}' contains no enabled rules.");
        }

        string extracted = snapshot.CommandText.Trim();

        foreach (var rule in enabledRules)
        {
            string allowed = rule.AllowedCommand?.Trim() ?? string.Empty;
            bool matches = rule.CommandMatchMode switch
            {
                CommandMatchMode.Exact => string.Equals(extracted, allowed, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(extracted, allowed, StringComparison.OrdinalIgnoreCase)
            };

            if (matches)
            {
                return ApprovalDecision.Allowed(
                    $"Command \"{extracted}\" explicitly approved by rule '{rule.Name}'.",
                    rule.Id,
                    rule.Name);
            }
        }

        return ApprovalDecision.Blocked(
            ApprovalBlockReason.CommandNotAllowed,
            $"Extracted command \"{extracted}\" is not allowed by any rule in set '{ruleSet.Name}'.");
    }
}
