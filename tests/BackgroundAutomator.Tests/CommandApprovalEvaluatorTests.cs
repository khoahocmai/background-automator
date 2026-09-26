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

    [Fact]
    public void Evaluator_Blocks_When_Snapshot_Is_Ambiguous()
    {
        var rule = CreateDefaultRule();
        var snapshot = CreateValidSnapshot() with
        {
            IsAmbiguous = true,
            AmbiguityReason = "Multiple candidate commands detected above prompt"
        };

        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.AmbiguousPrompt, decision.BlockReason);
        Assert.Contains("Multiple candidate commands", decision.Explanation);
    }

    [Fact]
    public void Evaluator_RuleSet_Allows_Matching_Command_And_Identifies_Rule()
    {
        var rule1 = new CommandApprovalRule { Name = "Build", AllowedCommand = "dotnet build BackgroundAutomator.sln" };
        var rule2 = new CommandApprovalRule { Name = "Test", AllowedCommand = "dotnet test BackgroundAutomator.sln" };
        var rule3 = new CommandApprovalRule { Name = "Git Status", AllowedCommand = "git status" };

        var ruleSet = new ApprovalRuleSet("Dev Tools", new[] { rule1, rule2, rule3 });
        var snapshot = CreateValidSnapshot() with { CommandText = "dotnet test BackgroundAutomator.sln" };

        var decision = CommandApprovalEvaluator.Evaluate(ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.BlockReason);
        Assert.Equal("Test", decision.MatchedRuleName);
        Assert.Equal(rule2.Id, decision.MatchedRuleId);
        Assert.Contains("explicitly approved by rule 'Test'", decision.Explanation);
    }

    [Fact]
    public void Evaluator_RuleSet_Blocks_When_No_Rule_Matches()
    {
        var rule1 = new CommandApprovalRule { Name = "Build", AllowedCommand = "dotnet build" };
        var rule2 = new CommandApprovalRule { Name = "Test", AllowedCommand = "dotnet test" };

        var ruleSet = new ApprovalRuleSet("Dev Tools", new[] { rule1, rule2 });
        var snapshot = CreateValidSnapshot() with { CommandText = "rm -rf /" };

        var decision = CommandApprovalEvaluator.Evaluate(ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.CommandNotAllowed, decision.BlockReason);
        Assert.Contains("not allowed by any rule in set 'Dev Tools'", decision.Explanation);
    }

    [Fact]
    public void Evaluator_RuleSet_Ignores_Disabled_Rules()
    {
        var rule1 = new CommandApprovalRule { Name = "Disabled Git", AllowedCommand = "git push", Enabled = false };
        var rule2 = new CommandApprovalRule { Name = "Enabled Git", AllowedCommand = "git status", Enabled = true };

        var ruleSet = new ApprovalRuleSet("Git", new[] { rule1, rule2 });
        var snapshot = CreateValidSnapshot() with { CommandText = "git push" };

        var decision = CommandApprovalEvaluator.Evaluate(ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.CommandNotAllowed, decision.BlockReason);
    }

    [Fact]
    public void Evaluator_RuleSet_Blocks_When_Snapshot_Is_Ambiguous()
    {
        var rule1 = new CommandApprovalRule { Name = "Allowed", AllowedCommand = "Get-Date" };
        var ruleSet = new ApprovalRuleSet("Tools", new[] { rule1 });

        var snapshot = CreateValidSnapshot() with
        {
            CommandText = "Get-Date",
            IsAmbiguous = true,
            AmbiguityReason = "Multiple candidate commands detected"
        };

        var decision = CommandApprovalEvaluator.Evaluate(ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA");

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.AmbiguousPrompt, decision.BlockReason);
    }
}
