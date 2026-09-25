using System.Text.RegularExpressions;

namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Parser that extracts confirmation prompt lines, selected options, and shell commands
/// from the visible viewport text of terminal windows.
/// </summary>
public sealed class CommandPromptParser : ICommandPromptParser
{
    private static readonly Regex ToolWrapperRegex = new(
        @"^(?:[●\*\-]\s*)?(?:Bash|run_command|exec|cmd|powershell)\s*\(\s*(.+?)\s*\)\s*(?:\(ctrl\+o to expand\))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommandPrefixRegex = new(
        @"^(?:Command|Cmd|Executing):\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CommandExtractionResult Parse(
        string? rawVisibleText,
        string expectedPrompt = "Run this command?",
        string expectedSelectedOption = "Yes, run command")
    {
        if (string.IsNullOrWhiteSpace(rawVisibleText))
        {
            return CommandExtractionResult.Failed("Visible viewport text is empty.");
        }

        var rawLines = rawVisibleText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var lines = new string[rawLines.Length];
        for (int i = 0; i < rawLines.Length; i++)
        {
            lines[i] = rawLines[i].Trim();
        }

        // 1. Find the last matching prompt line in the visible text
        int promptIdx = -1;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Contains(expectedPrompt, StringComparison.OrdinalIgnoreCase))
            {
                promptIdx = i;
                break;
            }
        }

        if (promptIdx < 0)
        {
            return CommandExtractionResult.Failed(
                $"Prompt '{expectedPrompt}' was not found in visible text.",
                isPromptVisible: false,
                isOptionSelected: false);
        }

        string promptText = lines[promptIdx];

        // 2. Find the expected option below the prompt line
        int optionIdx = -1;
        bool isSelected = false;
        string? selectedOptionText = null;

        int searchLimit = Math.Min(lines.Length - 1, promptIdx + 12);
        for (int i = promptIdx + 1; i <= searchLimit; i++)
        {
            string line = lines[i];
            if (line.Contains(expectedSelectedOption, StringComparison.OrdinalIgnoreCase))
            {
                optionIdx = i;
                // Check if this option has a selection marker: '>' or '❯' or '*'
                if (line.StartsWith(">") || line.StartsWith("❯") || line.StartsWith("*"))
                {
                    isSelected = true;
                    selectedOptionText = line;
                }
                else
                {
                    selectedOptionText = line;
                }
                break;
            }
        }

        if (optionIdx < 0)
        {
            return CommandExtractionResult.Failed(
                $"Expected option '{expectedSelectedOption}' was not found below prompt.",
                isPromptVisible: true,
                isOptionSelected: false,
                promptText: promptText);
        }

        if (!isSelected)
        {
            return CommandExtractionResult.Failed(
                $"Expected option '{expectedSelectedOption}' is present but not selected.",
                isPromptVisible: true,
                isOptionSelected: false,
                promptText: promptText,
                selectedOptionText: selectedOptionText);
        }

        // 3. Extract the command associated with this prompt
        string? extractedCommand = null;

        // Check Zone A: between prompt and options
        if (optionIdx > promptIdx + 1)
        {
            for (int i = promptIdx + 1; i < optionIdx; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || IsSeparatorLine(line) || IsOptionLine(line))
                    continue;

                extractedCommand = CleanCommand(line);
                if (!string.IsNullOrEmpty(extractedCommand))
                    break;
            }
        }

        // Check Zone B: preceding the prompt (searching upward)
        if (string.IsNullOrEmpty(extractedCommand))
        {
            for (int i = promptIdx - 1; i >= Math.Max(0, promptIdx - 15); i--)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || IsSeparatorLine(line))
                    continue;

                // If another prompt line is encountered, stop going up
                if (line.Contains(expectedPrompt, StringComparison.OrdinalIgnoreCase))
                    break;

                extractedCommand = CleanCommand(line);
                if (!string.IsNullOrEmpty(extractedCommand))
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(extractedCommand))
        {
            return CommandExtractionResult.Failed(
                "Could not extract command from visible prompt area.",
                isPromptVisible: true,
                isOptionSelected: true,
                promptText: promptText,
                selectedOptionText: selectedOptionText);
        }

        return CommandExtractionResult.Successful(
            commandText: extractedCommand,
            promptText: promptText,
            selectedOptionText: selectedOptionText!);
    }

    private static bool IsSeparatorLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        char first = line[0];
        if (first is '─' or '-' or '=' or '_' or '~' or '#')
        {
            // If the line consists mostly of repeating punctuation
            int matchCount = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == first) matchCount++;
            }
            if (matchCount >= 3 && (double)matchCount / line.Length >= 0.7)
                return true;
        }
        return false;
    }

    private static bool IsOptionLine(string line)
    {
        // Typical option lines start with '1.', '2.', '> 1.', '❯ 1.', etc.
        return Regex.IsMatch(line, @"^[>❯\*]?\s*\d+\.\s+");
    }

    private static string CleanCommand(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        // 1. Tool call wrapper: ● Bash(...) or Bash(...)
        var toolMatch = ToolWrapperRegex.Match(line);
        if (toolMatch.Success && toolMatch.Groups.Count > 1)
        {
            line = toolMatch.Groups[1].Value.Trim();
        }

        // 2. Command prefix: Command: <cmd>
        var prefixMatch = CommandPrefixRegex.Match(line);
        if (prefixMatch.Success && prefixMatch.Groups.Count > 1)
        {
            line = prefixMatch.Groups[1].Value.Trim();
        }

        // 3. Strip code block ticks: `cmd` or ```cmd```
        if (line.StartsWith("```") && line.EndsWith("```") && line.Length > 6)
        {
            line = line.Substring(3, line.Length - 6).Trim();
        }
        else if (line.StartsWith("`") && line.EndsWith("`") && line.Length > 2)
        {
            line = line.Substring(1, line.Length - 2).Trim();
        }

        // 4. Strip trailing (ctrl+o to expand)
        int expandIdx = line.IndexOf("(ctrl+o to expand)", StringComparison.OrdinalIgnoreCase);
        if (expandIdx >= 0)
        {
            line = line.Substring(0, expandIdx).Trim();
        }

        return line.Trim();
    }
}
