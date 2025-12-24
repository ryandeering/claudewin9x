using System.Text.Json;

namespace ClaudeWin9xNtServer.Infrastructure;

public class ClaudeOutputParser
{
    private bool _initShown;

    public record ParseResult(string? Text, bool AppendNewline);

    public static ParseResult Empty => new(null, false);
    public static ParseResult Text(string text) => new(text, true);
    public static ParseResult TextNoNewline(string text) => new(text, false);
    public static ParseResult Newline => new(null, true);

    public ParseResult Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElem))
            {
                return Empty;
            }

            return typeElem.GetString() switch
            {
                "system" => ParseSystem(root),
                "assistant" => ParseAssistant(root),
                "content_block_delta" => ParseContentBlockDelta(root),
                "content_block_stop" => Newline,
                "result" => ParseResultMessage(root),
                "tool_result" => ParseToolResult(root),
                _ => Empty
            };
        }
        catch (JsonException)
        {
            return Text(line);
        }
    }

    private ParseResult ParseSystem(JsonElement root)
    {
        if (_initShown)
        {
            return Empty;
        }

        if (root.TryGetProperty("subtype", out var subtype) && subtype.GetString() == "init")
        {
            _initShown = true;
            return Text("[Session started]");
        }

        return Empty;
    }

    private static ParseResult ParseAssistant(JsonElement root)
    {
        if (root.TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            return ParseContentArray(content);
        }

        if (root.TryGetProperty("content", out var directContent) &&
            directContent.ValueKind == JsonValueKind.Array)
        {
            return ParseContentArray(directContent);
        }

        return Empty;
    }

    private static ParseResult ParseContentArray(JsonElement content)
    {
        var texts = new List<string>();

        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType))
            {
                continue;
            }

            var typeStr = itemType.GetString();

            if (typeStr == "text" && item.TryGetProperty("text", out var text))
            {
                var textValue = text.GetString();
                if (!string.IsNullOrEmpty(textValue))
                {
                    texts.Add(textValue);
                }
            }
            else if (typeStr == "tool_use" && item.TryGetProperty("name", out var toolName))
            {
                texts.Add($"[Using tool: {toolName.GetString() ?? "unknown"}]");
            }
        }

        return texts.Count > 0 ? Text(string.Join("\n", texts)) : Empty;
    }

    private static ParseResult ParseContentBlockDelta(JsonElement root)
    {
        if (root.TryGetProperty("delta", out var delta) &&
            delta.TryGetProperty("text", out var deltaText))
        {
            var text = deltaText.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                return TextNoNewline(text);
            }
        }

        return Empty;
    }

    private static ParseResult ParseResultMessage(JsonElement root)
    {
        if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
        {
            return Text("[Error occurred]");
        }

        return Empty;
    }

    private static ParseResult ParseToolResult(JsonElement root)
    {
        if (root.TryGetProperty("content", out var toolContent))
        {
            var contentStr = toolContent.GetString();
            if (!string.IsNullOrEmpty(contentStr) && contentStr.Length < 500)
            {
                return Text($"[Tool output: {contentStr}]");
            }
        }

        return Empty;
    }

    public void Reset() => _initShown = false;
}
