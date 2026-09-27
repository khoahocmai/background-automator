using System.Security.Cryptography;
using System.Text;

namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Structural envelope representing a permission prompt block in the terminal viewport.
/// In Antigravity and modern CLI tools, this is the region between 'Requesting permission for:'
/// and 'Run this command?' with the selected option.
/// </summary>
public sealed record PermissionPromptEnvelope(
    bool IsStructurallyValid,
    string? RawCommandBlock,
    string? NormalizedCommandBlock,
    string? PromptText,
    string? SelectedOptionText,
    bool IsApprovalPromptVisible,
    bool IsYesOptionSelected,
    string? ParsedCommand = null,
    string? ParseFailureReason = null,
    bool IsAmbiguous = false,
    string? AmbiguityReason = null)
{
    /// <summary>
    /// Computes a stable cryptographic fingerprint of the permission prompt envelope
    /// to detect prompt changes or dismissals even when commands wrap or contain complex syntax.
    /// </summary>
    public string ComputePromptFingerprint()
    {
        string commandPart = NormalizedCommandBlock ?? RawCommandBlock ?? string.Empty;
        string promptPart = PromptText ?? string.Empty;
        string optionPart = SelectedOptionText ?? string.Empty;
        string content = $"{commandPart}\n{promptPart}\nSelectedOption={optionPart}";

        byte[] bytes = Encoding.UTF8.GetBytes(content);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Gets a compact, single-line bounded preview suitable for logging and status displays.
    /// </summary>
    public string GetCommandPreview(int maxLength = 100)
    {
        string? text = ParsedCommand ?? NormalizedCommandBlock ?? RawCommandBlock;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "[Unknown/Empty]";
        }

        string trimmed = text.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed.Substring(0, Math.Max(0, maxLength - 3)) + "...";
    }
}
