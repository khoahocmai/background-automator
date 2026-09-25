using BackgroundAutomator.Core.Approval;
using Xunit;

namespace BackgroundAutomator.Tests;

public class CommandPromptParserTests
{
    private readonly CommandPromptParser _parser = new();

    [Fact]
    public void Parse_Extracts_Standard_CLI_Prompt_And_Command()
    {
        string rawText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
  3. No, do not run (esc)
";

        var result = _parser.Parse(rawText);

        Assert.True(result.Success);
        Assert.True(result.IsApprovalPromptVisible);
        Assert.True(result.IsYesOptionSelected);
        Assert.Equal("dotnet test BackgroundAutomator.sln", result.CommandText);
        Assert.Equal("Run this command?", result.PromptText);
        Assert.Equal("> 1. Yes, run command", result.SelectedOptionText);
    }

    [Fact]
    public void Parse_Extracts_Bash_Tool_Wrapper_Command()
    {
        string rawText = @"
● Bash(dotnet build BackgroundAutomator.sln) (ctrl+o to expand)
──────────────────────────────────────────────────────────────
Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var result = _parser.Parse(rawText);

        Assert.True(result.Success);
        Assert.Equal("dotnet build BackgroundAutomator.sln", result.CommandText);
        Assert.True(result.IsYesOptionSelected);
    }

    [Fact]
    public void Parse_Extracts_RunCommand_Tool_Wrapper()
    {
        string rawText = @"
● run_command(Get-ChildItem -Path ""D:\personal-project"")
Run this command?
❯ 1. Yes, run command
  2. Cancel
";

        var result = _parser.Parse(rawText);

        Assert.True(result.Success);
        Assert.Equal(@"Get-ChildItem -Path ""D:\personal-project""", result.CommandText);
        Assert.True(result.IsYesOptionSelected);
    }

    [Fact]
    public void Parse_Extracts_Command_Between_Prompt_And_Options()
    {
        string rawText = @"
Run this command?
  git status
> 1. Yes, run command
  2. No, do not run
";

        var result = _parser.Parse(rawText);

        Assert.True(result.Success);
        Assert.Equal("git status", result.CommandText);
    }

    [Fact]
    public void Parse_Extracts_From_RealWorld_Cascadia_Terminal_Viewport_Structure()
    {
        // Live raw visible text structure captured from Windows Terminal TermControl (visible range)
        string liveUiaText = @"
● Read(D:/personal-project/background-clicker/src/BackgroundAutomator.Core/TextDetection/TextMatcher.cs)
● Bash(dotnet test BackgroundAutomator.sln)
────────────────────────────────────────────────────────────────────────────────────────────────────────────────
Run this command?
> 1. Yes, run command
  2. No, edit command
────────────────────────────────────────────────────────────────────────────────────────────────────────────────
esc to cancel                                                                  accept-edits · Gemini 3.8 Flash
";

        var result = _parser.Parse(liveUiaText);

        Assert.True(result.Success);
        Assert.Equal("dotnet test BackgroundAutomator.sln", result.CommandText);
        Assert.Equal("Run this command?", result.PromptText);
        Assert.Equal("> 1. Yes, run command", result.SelectedOptionText);
    }

    [Fact]
    public void Parse_Fails_When_Expected_Option_Is_Not_Selected()
    {
        string rawText = @"
dotnet test BackgroundAutomator.sln

Run this command?
  1. Yes, run command
> 2. No, edit command
  3. No, do not run (esc)
";

        var result = _parser.Parse(rawText);

        Assert.False(result.Success);
        Assert.True(result.IsApprovalPromptVisible);
        Assert.False(result.IsYesOptionSelected);
        Assert.Contains("not selected", result.FailureReason);
    }

    [Fact]
    public void Parse_Fails_When_Prompt_Is_Missing()
    {
        string rawText = @"
dotnet test BackgroundAutomator.sln
Build succeeded.
Passed! - Failed: 0, Passed: 211
";

        var result = _parser.Parse(rawText);

        Assert.False(result.Success);
        Assert.False(result.IsApprovalPromptVisible);
        Assert.False(result.IsYesOptionSelected);
        Assert.Contains("Prompt 'Run this command?' was not found", result.FailureReason);
    }

    [Fact]
    public void Parse_Fails_When_Option_Is_Missing()
    {
        string rawText = @"
dotnet test BackgroundAutomator.sln

Run this command?
";

        var result = _parser.Parse(rawText);

        Assert.False(result.Success);
        Assert.True(result.IsApprovalPromptVisible);
        Assert.False(result.IsYesOptionSelected);
        Assert.Contains("not found below prompt", result.FailureReason);
    }

    [Fact]
    public void Parse_Fails_When_Command_Cannot_Be_Found()
    {
        string rawText = @"
───────────────────────────────────
Run this command?
> 1. Yes, run command
  2. No
";

        var result = _parser.Parse(rawText);

        Assert.False(result.Success);
        Assert.True(result.IsApprovalPromptVisible);
        Assert.True(result.IsYesOptionSelected);
        Assert.Contains("Could not extract command", result.FailureReason);
    }

    [Fact]
    public void Parse_Selects_Latest_Active_Prompt_When_Multiple_Prompts_Exist()
    {
        string rawText = @"
# First prompt answered earlier:
● Bash(echo first)
Run this command?
  1. Yes, run command

# Second active prompt:
● Bash(dotnet test BackgroundAutomator.sln)
Run this command?
> 1. Yes, run command
  2. No
";

        var result = _parser.Parse(rawText);

        Assert.True(result.Success);
        Assert.Equal("dotnet test BackgroundAutomator.sln", result.CommandText);
        Assert.True(result.IsYesOptionSelected);
    }

    [Fact]
    public void Parse_Extracts_Multiline_Pipe_Continuation_Command()
    {
        string rawText = @"
Get-ChildItem ""D:\workspace\very-long-path"" |
  Where-Object { $_.Name -like ""*.json"" }

Run this command?
> 1. Yes, run command
  2. No, do not run
";
        var result = _parser.Parse(rawText);

        Assert.True(result.Success);
        Assert.False(result.IsAmbiguous);
        Assert.Equal(@"Get-ChildItem ""D:\workspace\very-long-path"" | Where-Object { $_.Name -like ""*.json"" }", result.CommandText);
    }

    [Fact]
    public void Parse_Extracts_Multiline_Backtick_Continuation_Command()
    {
        string rawText = @"
Get-ChildItem `
  -Path D:\workspace `
  -Recurse

Run this command?
> 1. Yes, run command
  2. No, do not run
";
        var result = _parser.Parse(rawText);

        Assert.True(result.Success);
        Assert.False(result.IsAmbiguous);
        Assert.Equal(@"Get-ChildItem -Path D:\workspace -Recurse", result.CommandText);
    }

    [Fact]
    public void Parse_Extracts_Multiline_Tool_Wrapper_Command()
    {
        string rawText = @"
● Bash(
  git status
)

Run this command?
> 1. Yes, run command
  2. No, do not run
";
        var result = _parser.Parse(rawText);

        Assert.True(result.Success);
        Assert.False(result.IsAmbiguous);
        Assert.Equal("git status", result.CommandText);
    }

    [Fact]
    public void Parse_Returns_Ambiguous_When_Multiple_Tool_Blocks_Precede_Prompt()
    {
        string rawText = @"
● Bash(git status)
● Bash(git diff)

Run this command?
> 1. Yes, run command
  2. No, do not run
";
        var result = _parser.Parse(rawText);

        Assert.False(result.Success);
        Assert.True(result.IsAmbiguous);
        Assert.Null(result.CommandText);
        Assert.Contains("Multiple conflicting command candidates", result.AmbiguityReason);
    }

    [Fact]
    public void Parse_Returns_Ambiguous_When_Multiple_Raw_Lines_Precede_Prompt()
    {
        string rawText = @"
git pull
git status

Run this command?
> 1. Yes, run command
  2. No, do not run
";
        var result = _parser.Parse(rawText);

        Assert.False(result.Success);
        Assert.True(result.IsAmbiguous);
        Assert.Null(result.CommandText);
        Assert.Contains("Multiple conflicting command candidates", result.AmbiguityReason);
    }

    [Fact]
    public void Parse_Returns_Failure_On_Null_Or_Empty_Input()
    {
        var resultNull = _parser.Parse(null);
        Assert.False(resultNull.Success);

        var resultEmpty = _parser.Parse("   ");
        Assert.False(resultEmpty.Success);
    }
}
