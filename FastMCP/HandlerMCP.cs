using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using static FastMCP.HandlerMCP;

namespace FastMCP;

public delegate object Tool(JsonElement id, JsonElement args);

public static partial class HandlerMCP
{

    static HandlerMCP()
    {
        _transportOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };
        resultInitialize = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { tools = new { } },
            serverInfo = new { name = "PCS", version = "3.1" }
        };
        tools = new ();
        list = new ();
    }

    static readonly JsonSerializerOptions _transportOptions;

    private static readonly object resultInitialize;

    private static readonly Dictionary<string, Tool> tools;
    private static List<object> list;
    private static object _toolsList;

    public static void Create(Tool tool, string name_, string desc, params (string propName, string type)[] props)
    {
        tools.Add(name_, tool);
        list.Add(ToolBuilder.Create(name_, desc, props));
    }

    public static void CreateList(Tool tool, string name_, string desc, string listPropertyName, params (string propName, string type)[] itemProps)
    { 
        tools.Add(name_, tool);
        list.Add(ToolBuilder.CreateList(name_, desc, listPropertyName, itemProps));
    }

    public static void CreateEmpty(Tool tool, string name, string desc)
    {
        tools.Add(name, tool);
        list.Add(ToolBuilder.CreateEmpty(name, desc));
    }

    public static void Start(StreamReader reader)
    {
        _toolsList = list.ToArray();
    Beginning:
        string? line = reader.ReadLine();
        if (line is null) 
            return;

        if (string.IsNullOrWhiteSpace(line)) 
            goto Beginning;

        string cleanLine = line?.Replace("\0", "").Trim();
        if (string.IsNullOrEmpty(cleanLine)) 
            return;

        Request? r = null;
        try
        {
            r = JsonSerializer.Deserialize<Request>(cleanLine, _transportOptions);
            if (r is null
                || string.IsNullOrEmpty(r.Value.Method)
                || r.Value.Id.ValueKind is JsonValueKind.Undefined
                || Menu(r.Value, out string jR)) 
                goto Beginning;
            Console.WriteLine(jR);
            Console.Out.Flush();
        }
        catch (Exception ex) 
        {
            Console.WriteLine(JsonSerializer.Serialize(CreateErrorResponse(r.Value.Id, $"Error: {ex.Message}"), _transportOptions));
            Console.Out.Flush();
        }
        goto Beginning;
    }

    private static bool Menu(in Request r, out string jsonResponse)
        => (jsonResponse = (r.Id is JsonElement el && el.ValueKind is JsonValueKind.Undefined) ?
            string.Empty :
            JsonSerializer.Serialize(
                r.Method switch
                {
                    "initialize" => new { jsonrpc = "2.0", id = r.Id, result = resultInitialize },
                    "tools/list" => new { jsonrpc = "2.0", id = r.Id, result = _toolsList },
                    "tools/call" => ToolsCall(r),
                    _ => CreateErrorResponse(r.Id, "Метод не найден.", -32601)
                },
                _transportOptions)) == string.Empty;

    private static object ToolsCall(in Request r)
    {
        using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(r.Params));
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("name", out var toolNameElement))
            return CreateErrorResponse(r.Id, "Нет названия инструмента. Убедитесь что правильно написали.");

        string toolName = toolNameElement.GetString();
        JsonElement args = root.GetProperty("arguments");

        return tools.TryGetValue(toolName, out Tool tool) ?
            tool(r.Id, args) :
            CreateErrorResponse(r.Id, $"Unknown tool: {toolName}");
    }

    public static object CreateErrorResponse(JsonElement id_, string message_, int code_ = -32602)
        => new
        {
            jsonrpc = "2.0",
            id = id_,
            error = new
            {
                code = code_,
                message = $"[{GetFormattedDateTime()}]\r\n{message_}"
            }
        };

    private static string GetFormattedDateTime()
    {
        DateTime now = DateTime.Now;
        return @$"date={now:yyyy-MM-dd} time={now:HH:mm:ss}";
    }

}
partial class HandlerMCP
{
    public struct Request
    {
        public string? Jsonrpc { get; set; }
        public string? Method { get; set; }
        public object? Params { get; set; }
        public JsonElement Id { get; set; }
    }
}