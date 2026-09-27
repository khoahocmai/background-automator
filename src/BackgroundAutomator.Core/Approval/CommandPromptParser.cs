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

    private static readonly Regex ToolWrapperStartRegex = new(
        @"^(?:[●\*\-]\s*)?(?:Bash|run_command|exec|cmd|powershell)\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OtherToolWrapperRegex = new(
        @"^(?:[●\*\-]\s*)?[A-Za-z_][A-Za-z0-9_]*\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FencedBlockStartRegex = new(
        @"^```(?:sh|bash|powershell|cmd|ps1)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PermissionHeaderRegex = new(
        @"^(?:[●\*\-]\s*)?(?:\*\*)?Requesting permission for:?(?:\*\*)?\s*(.*)$",
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

        // 3. Authoritative Extraction: Check for explicit "Requesting permission for:" block
        // In Antigravity and modern LLM CLI interfaces, the active prompt has the authoritative structure:
        //   Requesting permission for:
        //   <command block>
        //   Run this command?
        int prevPromptIdx = -1;
        for (int i = promptIdx - 1; i >= 0; i--)
        {
            if (lines[i].Contains(expectedPrompt, StringComparison.OrdinalIgnoreCase))
            {
                prevPromptIdx = i;
                break;
            }
        }

        // Safety: verify no misplaced permission headers below prompt
        for (int i = promptIdx + 1; i < optionIdx; i++)
        {
            if (PermissionHeaderRegex.IsMatch(lines[i]))
            {
                var malformed = new PermissionPromptEnvelope(
                    IsStructurallyValid: false,
                    RawCommandBlock: null,
                    NormalizedCommandBlock: null,
                    PromptText: promptText,
                    SelectedOptionText: selectedOptionText,
                    IsApprovalPromptVisible: true,
                    IsYesOptionSelected: isSelected,
                    IsAmbiguous: true,
                    AmbiguityReason: "Malformed prompt boundaries: 'Requesting permission for:' marker detected below prompt.");

                return CommandExtractionResult.Ambiguous(
                    "Malformed prompt boundaries: 'Requesting permission for:' marker detected below prompt.",
                    promptText,
                    selectedOptionText,
                    malformed);
            }
        }

        var permIndices = new List<(int Index, string InlineText)>();
        for (int i = prevPromptIdx + 1; i < promptIdx; i++)
        {
            var match = PermissionHeaderRegex.Match(lines[i]);
            if (match.Success)
            {
                permIndices.Add((i, match.Groups[1].Value.Trim()));
            }
        }

        if (permIndices.Count > 1)
        {
            var malformed = new PermissionPromptEnvelope(
                IsStructurallyValid: false,
                RawCommandBlock: null,
                NormalizedCommandBlock: null,
                PromptText: promptText,
                SelectedOptionText: selectedOptionText,
                IsApprovalPromptVisible: true,
                IsYesOptionSelected: isSelected,
                IsAmbiguous: true,
                AmbiguityReason: $"Malformed prompt boundaries: multiple 'Requesting permission for:' markers ({permIndices.Count}) detected before prompt.");

            return CommandExtractionResult.Ambiguous(
                $"Malformed prompt boundaries: multiple 'Requesting permission for:' markers ({permIndices.Count}) detected before prompt.",
                promptText,
                selectedOptionText,
                malformed);
        }

        if (permIndices.Count == 1)
        {
            var (permIdx, inlineCmd) = permIndices[0];
            var sectionLines = new List<(string Clean, int OriginalIndex)>();

            if (!string.IsNullOrEmpty(inlineCmd))
            {
                sectionLines.Add((inlineCmd, permIdx));
            }

            for (int i = permIdx + 1; i < promptIdx; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || IsSeparatorLine(line) || IsOptionLine(line))
                    continue;

                sectionLines.Add((line, i));
            }

            if (sectionLines.Count == 0)
            {
                var emptyEnvelope = new PermissionPromptEnvelope(
                    IsStructurallyValid: false,
                    RawCommandBlock: string.Empty,
                    NormalizedCommandBlock: string.Empty,
                    PromptText: promptText,
                    SelectedOptionText: selectedOptionText,
                    IsApprovalPromptVisible: true,
                    IsYesOptionSelected: isSelected,
                    ParseFailureReason: "No command candidate found inside permission section.");

                return CommandExtractionResult.Failed(
                    "No command candidate found inside permission section.",
                    isPromptVisible: true,
                    isOptionSelected: true,
                    promptText: promptText,
                    selectedOptionText: selectedOptionText,
                    envelope: emptyEnvelope);
            }

            var (rawBlock, normBlock) = BuildCommandBlocks(sectionLines);
            var permCandidates = ExtractCandidatesFromPermissionSection(sectionLines, rawLines, out string? permParseError);

            if (!string.IsNullOrEmpty(permParseError))
            {
                var errorEnvelope = new PermissionPromptEnvelope(
                    IsStructurallyValid: false,
                    RawCommandBlock: rawBlock,
                    NormalizedCommandBlock: normBlock,
                    PromptText: promptText,
                    SelectedOptionText: selectedOptionText,
                    IsApprovalPromptVisible: true,
                    IsYesOptionSelected: isSelected,
                    ParseFailureReason: permParseError,
                    IsAmbiguous: true,
                    AmbiguityReason: $"Permission section could not be parsed reliably: {permParseError}");

                return CommandExtractionResult.Ambiguous(
                    $"Permission section could not be parsed reliably: {permParseError}",
                    promptText,
                    selectedOptionText,
                    errorEnvelope);
            }

            if (permCandidates.Count == 0)
            {
                var noCandEnvelope = new PermissionPromptEnvelope(
                    IsStructurallyValid: false,
                    RawCommandBlock: rawBlock,
                    NormalizedCommandBlock: normBlock,
                    PromptText: promptText,
                    SelectedOptionText: selectedOptionText,
                    IsApprovalPromptVisible: true,
                    IsYesOptionSelected: isSelected,
                    ParseFailureReason: "No command candidate found inside permission section.");

                return CommandExtractionResult.Failed(
                    "No command candidate found inside permission section.",
                    isPromptVisible: true,
                    isOptionSelected: true,
                    promptText: promptText,
                    selectedOptionText: selectedOptionText,
                    envelope: noCandEnvelope);
            }

            if (permCandidates.Count > 1)
            {
                var multiEnvelope = new PermissionPromptEnvelope(
                    IsStructurallyValid: true,
                    RawCommandBlock: rawBlock,
                    NormalizedCommandBlock: normBlock,
                    PromptText: promptText,
                    SelectedOptionText: selectedOptionText,
                    IsApprovalPromptVisible: true,
                    IsYesOptionSelected: isSelected,
                    ParsedCommand: null,
                    ParseFailureReason: $"Multiple conflicting command candidates ({permCandidates.Count}) detected inside permission section.",
                    IsAmbiguous: true,
                    AmbiguityReason: $"Multiple conflicting command candidates ({permCandidates.Count}) detected inside permission section.");

                return CommandExtractionResult.Ambiguous(
                    $"Multiple conflicting command candidates ({permCandidates.Count}) detected inside permission section.",
                    promptText,
                    selectedOptionText,
                    multiEnvelope);
            }

            var validEnvelope = new PermissionPromptEnvelope(
                IsStructurallyValid: true,
                RawCommandBlock: rawBlock,
                NormalizedCommandBlock: normBlock,
                PromptText: promptText,
                SelectedOptionText: selectedOptionText,
                IsApprovalPromptVisible: true,
                IsYesOptionSelected: isSelected,
                ParsedCommand: permCandidates[0]);

            return CommandExtractionResult.Successful(permCandidates[0], promptText, selectedOptionText!, validEnvelope);
        }

        // 4. Fallback Extraction: Check Zone A (between prompt and options)
        if (optionIdx > promptIdx + 1)
        {
            var zoneALines = new List<string>();
            for (int i = promptIdx + 1; i < optionIdx; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || IsSeparatorLine(line) || IsOptionLine(line))
                    continue;

                zoneALines.Add(line);
            }

            if (zoneALines.Count > 0)
            {
                var zoneACandidates = ExtractCandidatesFromLines(zoneALines, rawLines, promptIdx + 1);
                if (zoneACandidates.Count > 1)
                {
                    return CommandExtractionResult.Ambiguous(
                        $"Multiple conflicting command candidates ({zoneACandidates.Count}) detected between prompt and options.",
                        promptText,
                        selectedOptionText);
                }

                if (zoneACandidates.Count == 1)
                {
                    return CommandExtractionResult.Successful(zoneACandidates[0], promptText, selectedOptionText!);
                }
            }
        }

        // Check Zone B: preceding the prompt (searching upward up to 20 lines)
        int zoneBStart = Math.Max(0, promptIdx - 20);
        for (int i = promptIdx - 1; i >= zoneBStart; i--)
        {
            if (lines[i].Contains(expectedPrompt, StringComparison.OrdinalIgnoreCase))
            {
                zoneBStart = i + 1;
                break;
            }
        }

        var zoneBLines = new List<(string Clean, int OriginalIndex)>();
        for (int i = zoneBStart; i < promptIdx; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || IsSeparatorLine(line) || IsOptionLine(line))
                continue;

            zoneBLines.Add((line, i));
        }

        if (zoneBLines.Count > 0)
        {
            var zoneBCandidates = ExtractCandidatesFromZoneB(zoneBLines, rawLines);
            if (zoneBCandidates.Count > 1)
            {
                return CommandExtractionResult.Ambiguous(
                    $"Multiple conflicting command candidates ({zoneBCandidates.Count}) detected before prompt.",
                    promptText,
                    selectedOptionText);
            }

            if (zoneBCandidates.Count == 1)
            {
                return CommandExtractionResult.Successful(zoneBCandidates[0], promptText, selectedOptionText!);
            }
        }

        return CommandExtractionResult.Failed(
            "Could not extract command from visible prompt area.",
            isPromptVisible: true,
            isOptionSelected: true,
            promptText: promptText,
            selectedOptionText: selectedOptionText);
    }

    private static List<string> ExtractCandidatesFromPermissionSection(
        List<(string Clean, int OriginalIndex)> sectionLines,
        string[] rawLines,
        out string? parseError)
    {
        parseError = null;
        var candidates = new List<string>();
        int i = 0;

        while (i < sectionLines.Count)
        {
            var (line, origIdx) = sectionLines[i];

            // 1. Fenced code block (``` ... ```)
            if (FencedBlockStartRegex.IsMatch(line) || line.StartsWith("```"))
            {
                int endIdx = -1;
                for (int j = i + 1; j < sectionLines.Count; j++)
                {
                    if (sectionLines[j].Clean.EndsWith("```") || sectionLines[j].Clean == "```")
                    {
                        endIdx = j;
                        break;
                    }
                }

                if (endIdx < 0)
                {
                    parseError = "Unclosed fenced code block in permission section.";
                    return candidates;
                }

                var blockContent = new List<string>();
                for (int k = i + 1; k < endIdx; k++)
                {
                    blockContent.Add(sectionLines[k].Clean);
                }
                string fencedCmd = string.Join(" ", blockContent).Trim();
                if (!string.IsNullOrEmpty(fencedCmd))
                {
                    candidates.Add(fencedCmd);
                }
                i = endIdx + 1;
                continue;
            }

            // 2. Tool wrapper: ● Bash(...) or Bash(...)
            var singleMatch = ToolWrapperRegex.Match(line);
            if (singleMatch.Success && singleMatch.Groups.Count > 1)
            {
                string cmd = CleanCommand(singleMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(cmd))
                {
                    candidates.Add(cmd);
                }
                i++;
                continue;
            }

            // 3. Multiline tool wrapper: starts with Bash( and closes on a later line with )
            if (ToolWrapperStartRegex.IsMatch(line))
            {
                int closeIdx = -1;
                for (int j = i + 1; j < sectionLines.Count; j++)
                {
                    if (sectionLines[j].Clean.Contains(")"))
                    {
                        closeIdx = j;
                        break;
                    }
                }

                if (closeIdx < 0)
                {
                    parseError = "Unclosed tool wrapper in permission section.";
                    return candidates;
                }

                int openParen = line.IndexOf('(');
                string firstPart = openParen >= 0 ? line.Substring(openParen + 1).Trim() : string.Empty;
                var toolLines = new List<string>();
                if (!string.IsNullOrEmpty(firstPart)) toolLines.Add(firstPart);

                for (int k = i + 1; k < closeIdx; k++)
                {
                    toolLines.Add(sectionLines[k].Clean);
                }

                string closeLine = sectionLines[closeIdx].Clean;
                int closeParen = closeLine.LastIndexOf(')');
                string lastPart = closeParen > 0 ? closeLine.Substring(0, closeParen).Trim() : string.Empty;
                if (!string.IsNullOrEmpty(lastPart)) toolLines.Add(lastPart);

                string multiCmd = CleanCommand(string.Join(" ", toolLines));
                if (!string.IsNullOrEmpty(multiCmd))
                {
                    candidates.Add(multiCmd);
                }
                i = closeIdx + 1;
                continue;
            }

            // 4. Command prefix: Command: <cmd>
            var prefixMatch = CommandPrefixRegex.Match(line);
            if (prefixMatch.Success && prefixMatch.Groups.Count > 1)
            {
                string cmd = CleanCommand(prefixMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(cmd))
                {
                    candidates.Add(cmd);
                }
                i++;
                continue;
            }

            // 5. Raw command line (with potential continuation lines)
            var rawCmdLines = new List<string> { line };
            while (i + 1 < sectionLines.Count)
            {
                string prevLine = sectionLines[i].Clean;
                var (nextLine, nextOrig) = sectionLines[i + 1];

                bool hasExplicitContinuation = prevLine.EndsWith("|") || prevLine.EndsWith("`") || prevLine.EndsWith("\\");
                string rawNext = nextOrig < rawLines.Length ? rawLines[nextOrig] : string.Empty;
                bool isIndented = rawNext.StartsWith("  ") || rawNext.StartsWith("\t");
                bool hasUnclosed = HasUnclosedTokens(string.Join(" ", rawCmdLines));

                if (hasExplicitContinuation || isIndented || hasUnclosed)
                {
                    rawCmdLines.Add(nextLine);
                    i++;
                }
                else
                {
                    break;
                }
            }

            string joinedRaw = JoinContinuationLines(rawCmdLines);
            string cleaned = CleanCommand(joinedRaw);
            if (!string.IsNullOrEmpty(cleaned))
            {
                candidates.Add(cleaned);
            }

            i++;
        }

        return candidates;
    }

    private static List<string> ExtractCandidatesFromZoneB(
        List<(string Clean, int OriginalIndex)> zoneLines,
        string[] rawLines)
    {
        var candidates = new List<string>();
        int i = 0;

        while (i < zoneLines.Count)
        {
            var (line, origIdx) = zoneLines[i];

            // 1. Fenced code block (```)
            if (FencedBlockStartRegex.IsMatch(line) || line.StartsWith("```"))
            {
                int endIdx = -1;
                for (int j = i + 1; j < zoneLines.Count; j++)
                {
                    if (zoneLines[j].Clean.EndsWith("```") || zoneLines[j].Clean == "```")
                    {
                        endIdx = j;
                        break;
                    }
                }

                if (endIdx > i)
                {
                    var blockContent = new List<string>();
                    for (int k = i + 1; k < endIdx; k++)
                    {
                        blockContent.Add(zoneLines[k].Clean);
                    }
                    string fencedCmd = string.Join(" ", blockContent).Trim();
                    if (!string.IsNullOrEmpty(fencedCmd))
                    {
                        candidates.Add(fencedCmd);
                    }
                    i = endIdx + 1;
                    continue;
                }
            }

            // 2. Tool wrapper: ● Bash(...) or Bash(...)
            var singleMatch = ToolWrapperRegex.Match(line);
            if (singleMatch.Success && singleMatch.Groups.Count > 1)
            {
                string cmd = CleanCommand(singleMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(cmd))
                {
                    candidates.Add(cmd);
                }
                i++;
                continue;
            }

            // 3. Multiline tool wrapper: starts with ● Bash( and closes on a later line with )
            if (ToolWrapperStartRegex.IsMatch(line))
            {
                int closeIdx = -1;
                for (int j = i + 1; j < zoneLines.Count; j++)
                {
                    string candidateClosing = zoneLines[j].Clean;
                    if (candidateClosing.Contains(")"))
                    {
                        closeIdx = j;
                        break;
                    }
                }

                if (closeIdx > i)
                {
                    // Extract content inside ( ... )
                    int openParen = line.IndexOf('(');
                    string firstPart = openParen >= 0 ? line.Substring(openParen + 1).Trim() : string.Empty;
                    var toolLines = new List<string>();
                    if (!string.IsNullOrEmpty(firstPart)) toolLines.Add(firstPart);

                    for (int k = i + 1; k < closeIdx; k++)
                    {
                        toolLines.Add(zoneLines[k].Clean);
                    }

                    string closeLine = zoneLines[closeIdx].Clean;
                    int closeParen = closeLine.LastIndexOf(')');
                    string lastPart = closeParen > 0 ? closeLine.Substring(0, closeParen).Trim() : string.Empty;
                    if (!string.IsNullOrEmpty(lastPart)) toolLines.Add(lastPart);

                    string multiCmd = CleanCommand(string.Join(" ", toolLines));
                    if (!string.IsNullOrEmpty(multiCmd))
                    {
                        candidates.Add(multiCmd);
                    }
                    i = closeIdx + 1;
                    continue;
                }
            }

            // 4. Other non-command tool call (e.g. ● Read(...), ● View(...), ● Grep(...)) -> ignore
            if (OtherToolWrapperRegex.IsMatch(line))
            {
                // If it closes on same line, skip
                if (line.Contains(")"))
                {
                    i++;
                    continue;
                }
                // If multiline other tool, find closing paren and skip
                for (int j = i + 1; j < zoneLines.Count; j++)
                {
                    if (zoneLines[j].Clean.Contains(")"))
                    {
                        i = j + 1;
                        break;
                    }
                }
                i++;
                continue;
            }

            // 5. Command: <cmd>
            var prefixMatch = CommandPrefixRegex.Match(line);
            if (prefixMatch.Success && prefixMatch.Groups.Count > 1)
            {
                string cmd = CleanCommand(prefixMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(cmd))
                {
                    candidates.Add(cmd);
                }
                i++;
                continue;
            }

            // 6. Raw command line (with potential continuation lines)
            var rawCmdLines = new List<string>();
            rawCmdLines.Add(line);
            int currentOrig = origIdx;

            while (i + 1 < zoneLines.Count)
            {
                string prevLine = zoneLines[i].Clean;
                var (nextLine, nextOrig) = zoneLines[i + 1];

                // Check if prevLine has explicit continuation: ends with | or ` or \
                bool hasExplicitContinuation = prevLine.EndsWith("|") || prevLine.EndsWith("`") || prevLine.EndsWith("\\");

                // Check if nextLine is indented in raw text (2 or more spaces or tab)
                string rawNext = nextOrig < rawLines.Length ? rawLines[nextOrig] : string.Empty;
                bool isIndented = rawNext.StartsWith("  ") || rawNext.StartsWith("\t");

                if (hasExplicitContinuation || isIndented)
                {
                    rawCmdLines.Add(nextLine);
                    i++;
                }
                else
                {
                    break;
                }
            }

            // Join raw command lines
            string joinedRaw = JoinContinuationLines(rawCmdLines);
            string cleaned = CleanCommand(joinedRaw);
            if (!string.IsNullOrEmpty(cleaned))
            {
                candidates.Add(cleaned);
            }

            i++;
        }

        return candidates;
    }

    private static List<string> ExtractCandidatesFromLines(List<string> lines, string[] rawLines, int startRawIdx)
    {
        var candidates = new List<string>();
        int i = 0;
        while (i < lines.Count)
        {
            var rawCmdLines = new List<string> { lines[i] };
            while (i + 1 < lines.Count)
            {
                string prev = lines[i];
                string next = lines[i + 1];
                bool hasContinuation = prev.EndsWith("|") || prev.EndsWith("`") || prev.EndsWith("\\");
                int rawIdx = startRawIdx + i + 1;
                bool isIndented = rawIdx < rawLines.Length && (rawLines[rawIdx].StartsWith("  ") || rawLines[rawIdx].StartsWith("\t"));

                if (hasContinuation || isIndented)
                {
                    rawCmdLines.Add(next);
                    i++;
                }
                else
                {
                    break;
                }
            }

            string joined = JoinContinuationLines(rawCmdLines);
            string cleaned = CleanCommand(joined);
            if (!string.IsNullOrEmpty(cleaned))
            {
                candidates.Add(cleaned);
            }
            i++;
        }
        return candidates;
    }

    private static string JoinContinuationLines(List<string> lines)
    {
        if (lines.Count == 1) return lines[0];

        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < lines.Count; k++)
        {
            string piece = lines[k].Trim();
            if (piece.EndsWith("`") && piece.Length > 1)
            {
                piece = piece.Substring(0, piece.Length - 1).Trim();
            }
            else if (piece.EndsWith("\\") && piece.Length > 1)
            {
                piece = piece.Substring(0, piece.Length - 1).Trim();
            }

            if (k > 0) sb.Append(' ');
            sb.Append(piece);
        }
        return sb.ToString().Trim();
    }

    private static bool IsSeparatorLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        if (line.StartsWith("#")) return true;
        char first = line[0];
        if (first is '─' or '-' or '=' or '_' or '~')
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

    private static (string Raw, string Normalized) BuildCommandBlocks(List<(string Clean, int OriginalIndex)> sectionLines)
    {
        var cleanLines = sectionLines.Select(s => s.Clean).ToList();
        string raw = string.Join("\n", cleanLines);

        // Check if enclosed in markdown code fences:
        if (cleanLines.Count >= 2 && cleanLines[0].StartsWith("```") && cleanLines[^1].EndsWith("```"))
        {
            var inner = cleanLines.Skip(1).Take(cleanLines.Count - 2).ToList();
            string innerRaw = string.Join("\n", inner);
            string innerNorm = string.Join(" ", inner);
            return (innerRaw, innerNorm);
        }

        // Check if starts with tool wrapper Bash( and ends with )
        if (cleanLines.Count >= 1 && ToolWrapperStartRegex.IsMatch(cleanLines[0]) && cleanLines[^1].EndsWith(")"))
        {
            var unwrapLines = new List<string>();
            int openParen = cleanLines[0].IndexOf('(');
            string first = openParen >= 0 ? cleanLines[0].Substring(openParen + 1).Trim() : string.Empty;
            if (!string.IsNullOrEmpty(first)) unwrapLines.Add(first);

            for (int k = 1; k < cleanLines.Count - 1; k++)
            {
                unwrapLines.Add(cleanLines[k]);
            }

            if (cleanLines.Count > 1)
            {
                string last = cleanLines[^1];
                int closeParen = last.LastIndexOf(')');
                string endPart = closeParen > 0 ? last.Substring(0, closeParen).Trim() : string.Empty;
                if (!string.IsNullOrEmpty(endPart)) unwrapLines.Add(endPart);
            }

            string unwrappedRaw = string.Join("\n", unwrapLines);
            string unwrappedNorm = string.Join(" ", unwrapLines);
            return (unwrappedRaw, unwrappedNorm);
        }

        string normalized = string.Join(" ", cleanLines);
        return (raw, normalized);
    }

    private static bool HasUnclosedTokens(string text)
    {
        int paren = 0;
        int brace = 0;
        int bracket = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int k = 0; k < text.Length; k++)
        {
            char c = text[k];
            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (c == '"' && !inSingleQuote)
            {
                if (k > 0 && (text[k - 1] == '`' || text[k - 1] == '\\'))
                {
                    // escaped quote
                }
                else
                {
                    inDoubleQuote = !inDoubleQuote;
                }
            }
            else if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == '(') paren++;
                else if (c == ')' && paren > 0) paren--;
                else if (c == '{') brace++;
                else if (c == '}' && brace > 0) brace--;
                else if (c == '[') bracket++;
                else if (c == ']' && bracket > 0) bracket--;
            }
        }

        return paren > 0 || brace > 0 || bracket > 0 || inSingleQuote || inDoubleQuote;
    }
}
