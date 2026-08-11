using System.Collections.Generic;
using System.Linq;

namespace FastMCP;

public static partial class ToolBuilder
{

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
        "undefined",
        ];

    public static object Create(string name_, string desc, params (string propName, string type)[] props)
        => new
        {
            name = name_,
            description = desc,
            inputSchema = new
            {
                type = Types.Object.Str(),
                properties = Dictionary(props),
                required = SelectToArray(props)
            }
        };

    public static object CreateList(string name_, string desc, string listPropertyName, params (string propName, string type)[] itemProps)
        => new
        {
            name = name_,
            description = desc,
            inputSchema = new
            {
                type = Types.Object.Str(),
                properties = new Dictionary<string, object>
                {
                    [listPropertyName] = new
                    {
                        type = Types.Array.Str(),
                        items = new
                        {
                            type = Types.Object.Str(),
                            properties = Dictionary(itemProps),
                            required = SelectToArray(itemProps)
                        }
                    }
                },
                required = new [] 
                { 
                    listPropertyName 
                }
            }
        };

    public static object CreateEmpty(string name, string desc)
        => Create(name, desc);

    public static string Str(this Types t)
        => typesString[(int)t];

    private static Dictionary<string, object> Dictionary((string propName, string type)[] p)
        => p.ToDictionary(
            static p => p.propName,
            static p => (object)new 
            { 
                type = p.type 
            });

    private static string[] SelectToArray((string propName, string type)[] p)
        => p.Select(static p => p.propName).ToArray();

}
partial class ToolBuilder
{

    public enum Types : byte
    {
        String,
        /// <summary>
        /// Целое число
        /// </summary>
        Integer,
        Float,
        Double,
        Boolean,
        Byte,
        Array,
        Object,
        Dictionary,
        /// <summary>
        /// Любой тип данных
        /// </summary>
        Any,
        Null,
        /// <summary>
        /// Неопределенное значение
        /// </summary>
        Undefined
    }

    public record Tool(string name, string description, ToolSchema inputSchema);

    public record ToolSchema(string type, Dictionary<string, Property> properties, string[] required = null);

    public record Property(string type);

}
