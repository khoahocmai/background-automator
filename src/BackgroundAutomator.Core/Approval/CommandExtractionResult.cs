namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Output of parsing raw terminal text looking for confirmation prompts, options, and commands.
/// </summary>
public sealed record CommandExtractionResult(
    bool Success,
    string? CommandText = null,
    string? PromptText = null,
    string? SelectedOptionText = null,
    bool IsApprovalPromptVisible = false,
    bool IsYesOptionSelected = false,
    string? FailureReason = null)
{
    public static CommandExtractionResult Successful(
        string commandText,
        string promptText,
        string selectedOptionText) =>
        new(
            Success: true,
            CommandText: commandText,
            PromptText: promptText,
            SelectedOptionText: selectedOptionText,
            IsApprovalPromptVisible: true,
            IsYesOptionSelected: true);

    public static CommandExtractionResult Failed(
        string failureReason,
        bool isPromptVisible = false,
        bool isOptionSelected = false,
        string? promptText = null,
        string? selectedOptionText = null) =>
        new(
            Success: false,
            CommandText: null,
            PromptText: promptText,
            SelectedOptionText: selectedOptionText,
            IsApprovalPromptVisible: isPromptVisible,
            IsYesOptionSelected: isOptionSelected,
            FailureReason: failureReason);
}
