
namespace FastMCP;

public readonly struct ToolResult
{
    public string Text { get; }
    public bool IsError { get; }
    public bool IsProtocolError { get; }

    public ToolResult(string text, bool isError = false, bool isProtocolError = false)
    {
        Text = text;
        IsError = isError;
        IsProtocolError = isProtocolError;
    }

    public static implicit operator ToolResult(string text)
        => new(text, false, false);

    public static ToolResult Error(string message)
        => new(message, true, false);

    public static ToolResult ProtocolError(string message)
        => new(message, true, true);
}
