using BackgroundAutomator.Core.Approval;
using Xunit;

namespace BackgroundAutomator.Tests;

public class CommandApprovalEvaluatorTests
{
    private static CommandApprovalRule CreateDefaultRule() => new()
    {
        Name = "Test Rule",
        ExpectedProcess = "WindowsTerminal.exe",
        ExpectedWindowClass = "CASCADIA",
        ExpectedPrompt = "Run this command?",
        ExpectedSelectedOption = "Yes, run command",
        AllowedCommand = "dotnet test BackgroundAutomator.sln",
        CommandMatchMode = CommandMatchMode.Exact,
        Enabled = true
    };

    private static CommandPromptSnapshot CreateValidSnapshot() => new(
        TargetHwnd: (IntPtr)0x12345,
        RawVisibleText: "...",
        CommandText: "dotnet test BackgroundAutomator.sln",
        PromptText: "Run this command?",
        SelectedOptionText: "> 1. Yes, run command",
        IsApprovalPromptVisible: true,
        IsYesOptionSelected: true);

    [Fact]
    public void Evaluator_Allows_When_All_Conditions_Match_Exactly()
    {
        var rule = CreateDefaultRule();
        var snapshot = CreateValidSnapshot();

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA_HOSTING_WINDOW_CLASS");

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.BlockReason);
        Assert.Contains("explicitly approved", decision.Explanation);
    }

    [Fact]
    public void Evaluator_Blocks_When_Command_Does_Not_Match_AllowedCommand()
    {
        var rule = CreateDefaultRule();
        var snapshot = CreateValidSnapshot() with { CommandText = "rm -rf /" };

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.CommandNotAllowed, decision.BlockReason);
        Assert.Contains("not allowed", decision.Explanation);
    }

    [Fact]
    public void Evaluator_Blocks_When_Command_Is_Prefix_Instead_Of_Exact()
    {
        var rule = CreateDefaultRule();
        // Exact mode must reject prefix extensions
        var snapshot = CreateValidSnapshot() with { CommandText = "dotnet test BackgroundAutomator.sln; rm -rf /" };

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.CommandNotAllowed, decision.BlockReason);
    }

    [Fact]
    public void Evaluator_Blocks_When_Rule_Is_Disabled()
    {
        var rule = CreateDefaultRule() with { Enabled = false };
        var snapshot = CreateValidSnapshot();

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.CommandNotAllowed, decision.BlockReason);
        Assert.Contains("disabled", decision.Explanation);
    }

    [Fact]
    public void Evaluator_Blocks_When_Target_Process_Mismatches()
    {
        var rule = CreateDefaultRule();
        var snapshot = CreateValidSnapshot();

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "notepad.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.TargetMismatch, decision.BlockReason);
        Assert.Contains("Target process 'notepad.exe'", decision.Explanation);
    }

    [Fact]
    public void Evaluator_Blocks_When_Window_Class_Mismatches()
    {
        var rule = CreateDefaultRule();
        var snapshot = CreateValidSnapshot();

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "Notepad_Class");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.TargetMismatch, decision.BlockReason);
    }

    [Fact]
    public void Evaluator_Blocks_When_Prompt_Is_Not_Visible()
    {
        var rule = CreateDefaultRule();
        var snapshot = CreateValidSnapshot() with { IsApprovalPromptVisible = false };

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.PromptNotVisible, decision.BlockReason);
    }

    [Fact]
    public void Evaluator_Blocks_When_Option_Is_Not_Selected()
    {
        var rule = CreateDefaultRule();
        var snapshot = CreateValidSnapshot() with { IsYesOptionSelected = false };

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.OptionNotSelected, decision.BlockReason);
    }

    [Fact]
    public void Evaluator_Blocks_When_CommandText_Is_Missing()
    {
        var rule = CreateDefaultRule();
        var snapshot = CreateValidSnapshot() with { CommandText = null };

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.CommandNotFound, decision.BlockReason);
    }
}
