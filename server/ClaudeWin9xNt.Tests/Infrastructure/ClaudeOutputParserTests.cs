using Shouldly;
using ClaudeWin9xNtServer.Infrastructure;

namespace ClaudeWin9xNtServer.Tests.Infrastructure;

public class ClaudeOutputParserTests
{
    private readonly ClaudeOutputParser _parser = new();

    [Fact]
    public void Parse_SystemInit_ReturnsSessionStarted()
    {
        var json = """{"type":"system","subtype":"init"}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBe("[Session started]");
        result.AppendNewline.ShouldBeTrue();
    }

    [Fact]
    public void Parse_SystemInit_OnlyShowsOnce()
    {
        var json = """{"type":"system","subtype":"init"}""";

        var first = _parser.Parse(json);
        var second = _parser.Parse(json);

        first.Text.ShouldBe("[Session started]");
        second.Text.ShouldBeNull();
    }

    [Fact]
    public void Parse_AssistantWithMessageContent_ReturnsText()
    {
        var json = """{"type":"assistant","message":{"content":[{"type":"text","text":"Hello world"}]}}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBe("Hello world");
        result.AppendNewline.ShouldBeTrue();
    }

    [Fact]
    public void Parse_AssistantWithDirectContent_ReturnsText()
    {
        var json = """{"type":"assistant","content":[{"type":"text","text":"Direct content"}]}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBe("Direct content");
        result.AppendNewline.ShouldBeTrue();
    }

    [Fact]
    public void Parse_AssistantWithToolUse_ReturnsToolMessage()
    {
        var json = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash"}]}}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBe("[Using tool: Bash]");
        result.AppendNewline.ShouldBeTrue();
    }

    [Fact]
    public void Parse_AssistantWithMultipleContentItems_CombinesThem()
    {
        var json = """{"type":"assistant","message":{"content":[{"type":"text","text":"First"},{"type":"text","text":"Second"}]}}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBe("First\nSecond");
    }

    [Fact]
    public void Parse_ContentBlockDelta_ReturnsTextWithoutNewline()
    {
        var json = """{"type":"content_block_delta","delta":{"text":"streaming"}}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBe("streaming");
        result.AppendNewline.ShouldBeFalse();
    }

    [Fact]
    public void Parse_ContentBlockStop_ReturnsNewlineOnly()
    {
        var json = """{"type":"content_block_stop"}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBeNull();
        result.AppendNewline.ShouldBeTrue();
    }

    [Fact]
    public void Parse_ResultWithError_ReturnsErrorMessage()
    {
        var json = """{"type":"result","is_error":true}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBe("[Error occurred]");
    }

    [Fact]
    public void Parse_ResultWithoutError_ReturnsEmpty()
    {
        var json = """{"type":"result","is_error":false}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBeNull();
    }

    [Fact]
    public void Parse_ToolResult_ReturnsToolOutput()
    {
        var json = """{"type":"tool_result","content":"command output"}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBe("[Tool output: command output]");
    }

    [Fact]
    public void Parse_ToolResultTooLong_ReturnsEmpty()
    {
        var longContent = new string('x', 600);
        var json = $$$"""{"type":"tool_result","content":"{{{longContent}}}"}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBeNull();
    }

    [Fact]
    public void Parse_InvalidJson_PassesThroughAsText()
    {
        var notJson = "This is not JSON";

        var result = _parser.Parse(notJson);

        result.Text.ShouldBe("This is not JSON");
        result.AppendNewline.ShouldBeTrue();
    }

    [Fact]
    public void Parse_EmptyLine_ReturnsEmpty()
    {
        var result = _parser.Parse("");

        result.Text.ShouldBeNull();
        result.AppendNewline.ShouldBeFalse();
    }

    [Fact]
    public void Parse_WhitespaceLine_ReturnsEmpty()
    {
        var result = _parser.Parse("   ");

        result.Text.ShouldBeNull();
        result.AppendNewline.ShouldBeFalse();
    }

    [Fact]
    public void Parse_UnknownType_ReturnsEmpty()
    {
        var json = """{"type":"unknown_future_type","data":"something"}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBeNull();
        result.AppendNewline.ShouldBeFalse();
    }

    [Fact]
    public void Parse_JsonWithoutType_ReturnsEmpty()
    {
        var json = """{"foo":"bar"}""";

        var result = _parser.Parse(json);

        result.Text.ShouldBeNull();
        result.AppendNewline.ShouldBeFalse();
    }

    [Fact]
    public void Reset_AllowsInitToShowAgain()
    {
        var json = """{"type":"system","subtype":"init"}""";
        _parser.Parse(json);

        _parser.Reset();
        var result = _parser.Parse(json);

        result.Text.ShouldBe("[Session started]");
    }
}
