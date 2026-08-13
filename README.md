# FastMCP

Синхронный C#-класс для создания серверов по протоколу Model Context Protocol (MCP) over Stdio.

## Быстрый старт

```csharp
using System;
using System.Text.Json;

internal class Program
{
    static void Main()
    {
        // 1. Инструмент без аргументов
        HandlerMCP.CreateEmpty(GetTime, "GetTime", "Получить время сервера");

        // 2. Инструмент с фиксированными свойствами
        HandlerMCP.Create(Add, "Add", "Сложение двух чисел",
            ("a", HandlerMCP.Types.Integer.Str()),
            ("b", HandlerMCP.Types.Integer.Str())
        );

        // 3. Инструмент для работы со списками
        HandlerMCP.CreateList(ProcessBatch, "Batch", "Обработка списка", "items",
            ("id", HandlerMCP.Types.Integer.Str())
        );

        // Запуск сервера
        HandlerMCP.Start(Console.OpenStandardInput());
    }

    // 1. Успешный ответ (неявное приведение строки)
    static ToolResult GetTime(JsonElement id, JsonElement args) 
        => DateTime.Now.ToString("HH:mm:ss");

    // 2. Пример возврата ошибки для нейросети (ToolResult.Error)
    static ToolResult Add(JsonElement id, JsonElement args)
    {
        if (!args.TryGetProperty("a", out var aProp) || aProp.ValueKind != JsonValueKind.Number)
            return ToolResult.Error("Параметр 'a' обязателен и должен быть числом.");

        if (!args.TryGetProperty("b", out var bProp) || bProp.ValueKind != JsonValueKind.Number)
            return ToolResult.Error("Параметр 'b' обязателен и должен быть числом.");

        return (aProp.GetInt32() + bProp.GetInt32()).ToString();
    }

    // 3. Пример возврата ошибки протокола (ToolResult.ProtocolError)
    static ToolResult ProcessBatch(JsonElement id, JsonElement args)
    {
        if (!args.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return ToolResult.ProtocolError("Неверный формат: отсутствует массив 'items'.");

        int count = items.GetArrayLength();
        if (count == 0)
            return ToolResult.Error("Массив 'items' не может быть пустым.");

        return $"Обработано элементов: {count}";
    }
}
```

## Методы регистрации инструментов

Все инструменты регистрируются до вызова `HandlerMCP.Start()`.

### CreateEmpty
Регистрирует инструмент, не принимающий входных аргументов.

```csharp
HandlerMCP.CreateEmpty(ToolHandler handler, string name, string description);
```

### Create
Регистрирует инструмент с фиксированным набором свойств (ключ-значение).

```csharp
HandlerMCP.Create(ToolHandler handler, string name, string description, params (string propName, string type)[] props);
```

### CreateList
Регистрирует инструмент, принимающий массив объектов в указанном свойстве.

```csharp
HandlerMCP.CreateList(ToolHandler handler, string name, string description, string listPropertyName, params (string propName, string type)[] itemProps);
```

## Типы данных (`HandlerMCP.Types`)

Для построения типов аргументов в методах `Create` и `CreateList` используется метод расширения `.Str()`:

* `HandlerMCP.Types.String.Str()` — `"string"`
* `HandlerMCP.Types.Integer.Str()` — `"integer"`
* `HandlerMCP.Types.Float.Str()` — `"float"`
* `HandlerMCP.Types.Double.Str()` — `"double"`
* `HandlerMCP.Types.Boolean.Str()` — `"boolean"`
* `HandlerMCP.Types.Array.Str()` — `"array"`
* `HandlerMCP.Types.Object.Str()` — `"object"`

## Обработка результатов и ошибок (`ToolResult`)

Делегат обработчика имеет сигнатуру:
```csharp
public delegate ToolResult ToolHandler(JsonElement id, JsonElement args);
```

Возвращаемое значение `ToolResult` поддерживает три сценария ответа:

### 1. Успешное выполнение
Возвращается обычная строка (автоматически приводится к `ToolResult`).

```csharp
return "Результат вычислений";
```

### 2. Ошибка исполнения инструмента (`ToolResult.Error`)
Формирует ответ с флагом `isError: true`. Используется, когда аргументы неверны или логика не может быть выполнена. Нейросеть считывает этот текст и пытается скорректировать параметры при повторном вызове.

```csharp
return ToolResult.Error("Параметр 'a' должен быть целым числом.");
```

### 3. Ошибка протокола (`ToolResult.ProtocolError`)
Формирует ответ ошибки JSON-RPC (`"error": { ... }`). Используется для вызова сбоя на стороне MCP-клиента.

```csharp
return ToolResult.ProtocolError("Критический сбой базы данных.");
```

Если внутри обработчика происходит необработанное исключение (`Exception`), сервер автоматически формирует ошибку протокола.