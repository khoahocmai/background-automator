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
    string? FailureReason = null,
    bool IsAmbiguous = false,
    string? AmbiguityReason = null,
    PermissionPromptEnvelope? Envelope = null)
{
    public static CommandExtractionResult Successful(
        string commandText,
        string promptText,
        string selectedOptionText,
        PermissionPromptEnvelope? envelope = null) =>
        new(
            Success: true,
            CommandText: commandText,
            PromptText: promptText,
            SelectedOptionText: selectedOptionText,
            IsApprovalPromptVisible: true,
            IsYesOptionSelected: true,
            Envelope: envelope);

    public static CommandExtractionResult Failed(
        string failureReason,
        bool isPromptVisible = false,
        bool isOptionSelected = false,
        string? promptText = null,
        string? selectedOptionText = null,
        PermissionPromptEnvelope? envelope = null) =>
        new(
            Success: false,
            CommandText: null,
            PromptText: promptText,
            SelectedOptionText: selectedOptionText,
            IsApprovalPromptVisible: isPromptVisible,
            IsYesOptionSelected: isOptionSelected,
            FailureReason: failureReason,
            Envelope: envelope);

    public static CommandExtractionResult Ambiguous(
        string ambiguityReason,
        string? promptText = null,
        string? selectedOptionText = null,
        PermissionPromptEnvelope? envelope = null) =>
        new(
            Success: false,
            CommandText: null,
            PromptText: promptText,
            SelectedOptionText: selectedOptionText,
            IsApprovalPromptVisible: true,
            IsYesOptionSelected: true,
            FailureReason: ambiguityReason,
            IsAmbiguous: true,
            AmbiguityReason: ambiguityReason,
            Envelope: envelope);
}
