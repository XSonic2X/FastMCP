using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FastMCP;

public delegate ToolResult ToolHandler(JsonElement id, JsonElement args);

public static partial class HandlerMCP
{
    private static readonly Dictionary<string, (object Schema, ToolHandler Handler)> _tools = new();
    private static object[] _toolsList = Array.Empty<object>();
    private static readonly string[] typesString =
    [
        "string",
        "integer",
        "float",
        "double",
        "boolean",
        "byte",
        "array",
        "object",
        "dictionary",
        "any",
        "null",
        "undefined"
    ];

    public static string Str(this Types t)
        => typesString[(int)t];

    public static void CreateEmpty(ToolHandler handler, string name, string desc)
        => _tools[name] = (new
        {
            name,
            description = desc,
            inputSchema = new
            {
                type = "object",
                properties = new { }
            }
        }, handler);
    public static void Create(ToolHandler handler, string name, string desc, params (string propName, string type)[] props)
        => _tools[name] = (new
        {
            name,
            description = desc,
            inputSchema = new
            {
                type = "object",
                properties = Dictionary(props),
                required = SelectToArray(props)
            }
        }, handler);
    public static void CreateList(ToolHandler handler, string name, string desc, string listPropertyName, params (string propName, string type)[] itemProps)
        => _tools[name] = (new
        {
            name,
            description = desc,
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    [listPropertyName] = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = Dictionary(itemProps),
                            required = SelectToArray(itemProps)
                        }
                    }
                },
                required = new[] { listPropertyName }
            }
        }, handler);

    private static Dictionary<string, Property> Dictionary((string propName, string type)[] p)
        => p.ToDictionary(
            static p => p.propName,
            static p => new Property(p.type));

    private static string[] SelectToArray((string propName, string type)[] p)
        => p.Select(static p => p.propName).ToArray();

    public static void Start(StreamReader reader) 
        => Start(reader.BaseStream);

    public static void Start(Stream inputStream)
    {
        _toolsList = _tools.Values.Select(static t => t.Schema).ToArray();

        using var stdout = Console.OpenStandardOutput();
        using var writer = new Utf8JsonWriter(stdout, new JsonWriterOptions { Indented = false });
        using var lineReader = new ByteLineReader(inputStream);

    Beginning:
        if (!lineReader.TryReadLine(out ReadOnlySpan<byte> lineSpan))
        {
            if (lineReader.HasMoreData)
                goto Beginning;
            else
                return;
        }

        ReadOnlySpan<byte> cleanLine = CleanSpan(lineSpan);
        if (cleanLine.IsEmpty)
            goto Beginning;

        // Передаем stdout в обработчик
        ProcessRequestSpan(cleanLine, writer, stdout);

        goto Beginning;
    }

    private static void ProcessRequestSpan(ReadOnlySpan<byte> jsonBytes, Utf8JsonWriter writer, Stream stdout)
    {
        writer.Reset(stdout);

        JsonElement id = default;
        try
        {
            var reader = new Utf8JsonReader(jsonBytes);
            using var doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("id"u8, out id) || id.ValueKind is JsonValueKind.Undefined)
                return;

            if (!root.TryGetProperty("method"u8, out JsonElement methodEl))
                return;

            if (methodEl.ValueEquals("initialize"u8))
                WriteInitializeResponse(writer, id);
            else if (methodEl.ValueEquals("tools/list"u8))
                WriteToolsListResponse(writer, id);
            else if (methodEl.ValueEquals("tools/call"u8))
            {
                root.TryGetProperty("params"u8, out JsonElement paramsEl);
                ToolsCall(id, paramsEl, writer);
            }
            else
                WriteErrorResponse(writer, id, "Метод не найден.", -32601);

            writer.Flush();
            stdout.WriteByte((byte)'\n');
            stdout.Flush();
        }
        catch (Exception ex)
        {
            writer.Reset(stdout);
            WriteErrorResponse(writer, id, ex.Message, -32602);
            writer.Flush();
            stdout.WriteByte((byte)'\n');
            stdout.Flush();
        }
    }

    private static void ToolsCall(JsonElement id, JsonElement paramsEl, Utf8JsonWriter writer)
    {
        if (paramsEl.ValueKind is not JsonValueKind.Object || !paramsEl.TryGetProperty("name"u8, out var toolNameElement))
        {
            WriteErrorResponse(writer, id, "Нет названия инструмента.");
            return;
        }

        string? toolName = toolNameElement.GetString();
        if (toolName is null || !_tools.TryGetValue(toolName, out var tool))
        {
            WriteErrorResponse(writer, id, $"Unknown tool: {toolName}");
            return;
        }

        paramsEl.TryGetProperty("arguments"u8, out JsonElement args);

        try
        {
            ToolResult result = tool.Handler(id, args);

            if (result.IsProtocolError)
                WriteErrorResponse(writer, id, result.Text); // Отправляем жесткую ошибку JSON-RPC
            else
                WriteToolResponse(writer, id, result); // Отправляем стандартный ответ
        }
        catch (Exception ex)
        {
            WriteErrorResponse(writer, id, $"Ошибка выполнения: {ex.Message}");
        }
    }

    private static void WriteToolResponse(Utf8JsonWriter writer, JsonElement id, ToolResult result)
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc"u8, "2.0"u8);
        writer.WritePropertyName("id"u8);
        id.WriteTo(writer);

        writer.WritePropertyName("result"u8);
        writer.WriteStartObject();
        writer.WritePropertyName("content"u8);
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("type"u8, "text"u8);
        writer.WriteString("text"u8, result.Text);
        writer.WriteEndObject();
        writer.WriteEndArray();

        // Динамический флаг ошибки
        writer.WriteBoolean("isError"u8, result.IsError);

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static ReadOnlySpan<byte> CleanSpan(ReadOnlySpan<byte> span)
    {
        while (span.Length > 0 && (span[0] == (byte)'\r' || span[0] == (byte)'\n' || span[0] == (byte)' ' || span[0] == (byte)'\0' || span[0] == (byte)'\t'))
            span = span.Slice(1);
        while (span.Length > 0 && (span[^1] == (byte)'\r' || span[^1] == (byte)'\n' || span[^1] == (byte)' ' || span[^1] == (byte)'\0' || span[^1] == (byte)'\t'))
            span = span.Slice(0, span.Length - 1);
        return span;
    }

    private static void WriteSuccessResponse(Utf8JsonWriter writer, JsonElement id, string text)
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc"u8, "2.0"u8);
        writer.WritePropertyName("id"u8);
        id.WriteTo(writer);

        writer.WritePropertyName("result"u8);
        writer.WriteStartObject();
        writer.WritePropertyName("content"u8);
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("type"u8, "text"u8);
        writer.WriteString("text"u8, text);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteBoolean("isError"u8, false);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteErrorResponse(Utf8JsonWriter writer, JsonElement id, string message, int code = -32602)
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc"u8, "2.0"u8);
        writer.WritePropertyName("id"u8);
        if (id.ValueKind is JsonValueKind.Undefined)
            writer.WriteNullValue();
        else
            id.WriteTo(writer);

        writer.WritePropertyName("error"u8);
        writer.WriteStartObject();
        writer.WriteNumber("code"u8, code);
        writer.WriteString("message"u8, message);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteInitializeResponse(Utf8JsonWriter writer, JsonElement id)
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc"u8, "2.0"u8);
        writer.WritePropertyName("id"u8);
        id.WriteTo(writer);
        writer.WritePropertyName("result"u8);
        writer.WriteStartObject();
        writer.WriteString("protocolVersion"u8, "2024-11-05"u8);
        writer.WritePropertyName("capabilities"u8);
        writer.WriteStartObject();
        writer.WritePropertyName("tools"u8);
        writer.WriteStartObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("serverInfo"u8);
        writer.WriteStartObject();
        writer.WriteString("name"u8, "PCS"u8);
        writer.WriteString("version"u8, "3.1"u8);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteToolsListResponse(Utf8JsonWriter writer, JsonElement id)
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc"u8, "2.0"u8);
        writer.WritePropertyName("id"u8);
        id.WriteTo(writer);
        writer.WritePropertyName("result"u8);
        writer.WriteStartObject();
        writer.WritePropertyName("tools"u8);
        JsonSerializer.Serialize(writer, _toolsList);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    
}
partial class HandlerMCP
{

    public enum Types : byte
    {
        String,
        Integer,
        Float,
        Double,
        Boolean,
        Byte,
        Array,
        Object,
        Dictionary,
        Any,
        Null,
        Undefined
    }

    private ref struct ByteLineReader
    {
        private readonly Stream _stream;
        private byte[]? _buffer;
        private int _bufferOffset;

        public bool HasMoreData;

        public ByteLineReader(Stream stream)
        {
            _stream = stream;
            _buffer = ArrayPool<byte>.Shared.Rent(8192);
            _bufferOffset = 0;
            HasMoreData = true;
        }

        public bool TryReadLine(out ReadOnlySpan<byte> lineSpan)
        {
            if (_buffer is null)
            {
                lineSpan = default;
                return false;
            }

            ReadOnlySpan<byte> currentSpan = _buffer.AsSpan(0, _bufferOffset);
            int newLineIndex = currentSpan.IndexOf((byte)'\n');

            if (newLineIndex >= 0)
            {
                lineSpan = currentSpan.Slice(0, newLineIndex);
                ShiftBuffer(newLineIndex + 1);
                return true;
            }

            if (_bufferOffset == _buffer.Length)
            {
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                _buffer.AsSpan(0, _bufferOffset).CopyTo(newBuffer);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
            }

            int bytesRead = _stream.Read(_buffer, _bufferOffset, _buffer.Length - _bufferOffset);
            if (bytesRead <= 0)
            {
                HasMoreData = false;
                if (_bufferOffset > 0)
                {
                    lineSpan = _buffer.AsSpan(0, _bufferOffset);
                    _bufferOffset = 0;
                    return true;
                }

                lineSpan = default;
                return false;
            }

            _bufferOffset += bytesRead;

            currentSpan = _buffer.AsSpan(0, _bufferOffset);
            newLineIndex = currentSpan.IndexOf((byte)'\n');

            if (newLineIndex >= 0)
            {
                lineSpan = currentSpan.Slice(0, newLineIndex);
                ShiftBuffer(newLineIndex + 1);
                return true;
            }

            lineSpan = default;
            return false;
        }

        private void ShiftBuffer(int consumedBytes)
        {
            int remaining = _bufferOffset - consumedBytes;
            if (remaining > 0)
                _buffer.AsSpan(consumedBytes, remaining).CopyTo(_buffer);
            _bufferOffset = remaining;
        }

        public void Dispose()
        {
            if (_buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }
    }

    public record Tool(string name, string description, ToolSchema inputSchema);

    public record ToolSchema(string type, Dictionary<string, Property> properties, string[] required = null);

    public record Property(string type);

}