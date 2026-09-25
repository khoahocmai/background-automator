namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Parser interface for extracting command confirmation prompts, options, and commands from visible viewport text.
/// </summary>
public interface ICommandPromptParser
{
    /// <summary>
    /// Parses the visible text looking for the specified prompt, selected option, and associated command.
    /// </summary>
    /// <param name="rawVisibleText">Raw visible viewport text.</param>
    /// <param name="expectedPrompt">Expected prompt string (e.g. "Run this command?").</param>
    /// <param name="expectedSelectedOption">Expected selected option text (e.g. "Yes, run command").</param>
    /// <returns>A <see cref="CommandExtractionResult"/> indicating parse success and extracted details.</returns>
    CommandExtractionResult Parse(
        string? rawVisibleText,
        string expectedPrompt = "Run this command?",
        string expectedSelectedOption = "Yes, run command");
}
