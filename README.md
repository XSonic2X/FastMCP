# FastMCP

**Самый быстрый способ создать MCP-сервер на C#.**  
Никаких зависимостей, сложных настроек или асинхронности. Просто напишите функции и запустите.

## Быстрый старт

Весь сервер описывается в 3 шага:
1. Опишите функции-обработчики.
2. Зарегистрируйте их (3 строки кода).
3. Запустите сервер (1 строка кода).

## Примеры использования

### Простой инструмент без аргументов

Верните строку — сервер сам обернёт её в правильный JSON-ответ.

```csharp
// Регистрация
HandlerMCP.CreateEmpty(GetTime, "GetTime", "Текущее время сервера");

// Реализация
static ToolResult GetTime(JsonElement id, JsonElement args) 
    => DateTime.Now.ToString("HH:mm:ss");
```

### Инструмент с аргументами и валидацией

Принимайте аргументы и возвращайте понятные ошибки для LLM через `ToolResult.Error`.

```csharp
// Регистрация: указываем имена и типы параметров
HandlerMCP.Create(Add, "Add", "Сложение двух чисел",
    ("a", HandlerMCP.Types.Integer.Str()),
    ("b", HandlerMCP.Types.Integer.Str())
);

// Реализация
static ToolResult Add(JsonElement id, JsonElement args)
{
    if (!args.TryGetProperty("a", out var a) || a.ValueKind != JsonValueKind.Number)
        return ToolResult.Error("Параметр 'a' должен быть числом.");
    
    if (!args.TryGetProperty("b", out var b) || b.ValueKind != JsonValueKind.Number)
        return ToolResult.Error("Параметр 'b' должен быть числом.");

    return (a.GetInt32() + b.GetInt32()).ToString();
}
```

### Работа со списками

Обрабатывайте массивы данных через `CreateList`.

```csharp
// Регистрация: указываем имя свойства-массива ("items") и структуру элемента
HandlerMCP.CreateList(ProcessBatch, "Batch", "Обработка списка", "items",
    ("id", HandlerMCP.Types.Integer.Str())
);

// Реализация
static ToolResult ProcessBatch(JsonElement id, JsonElement args)
{
    if (!args.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        return ToolResult.Error("Ожидается массив 'items'.");

    return $"Обработано {items.GetArrayLength()} элементов.";
}
```

### Полный пример запуска (Program.cs)

```csharp
using System;
using System.Text.Json;

internal class Program
{
    static void Main()
    {
        // 1. Регистрируем инструменты
        HandlerMCP.CreateEmpty(GetTime, "GetTime", "Текущее время");
        HandlerMCP.Create(Add, "Add", "Сложить числа", 
            ("a", HandlerMCP.Types.Integer.Str()), 
            ("b", HandlerMCP.Types.Integer.Str()));
        
        // 2. Запускаем сервер (чтение из stdin, запись в stdout)
        HandlerMCP.Start(Console.OpenStandardInput());
    }

    static ToolResult GetTime(JsonElement id, JsonElement args) 
        => DateTime.Now.ToString("HH:mm:ss");

    static ToolResult Add(JsonElement id, JsonElement args)
    {
        if (!args.TryGetProperty("a", out var a)) return ToolResult.Error("Нет параметра 'a'");
        if (!args.TryGetProperty("b", out var b)) return ToolResult.Error("Нет параметра 'b'");
        return (a.GetInt32() + b.GetInt32()).ToString();
    }
}
```

## Доступные методы

| Метод | Описание | Пример использования |
|-------|----------|----------------------|
| `CreateEmpty` | Инструмент без параметров | `GetTime`, `Ping` |
| `Create` | Инструмент с фиксированными параметрами | `Add(a, b)`, `Search(query)` |
| `CreateList` | Инструмент для обработки списков | `ProcessBatch(items)`, `Filter(ids)` |

## Типы данных

Используйте `.Str()` для указания типов в регистрационных методах:
- `HandlerMCP.Types.String.Str()` → `"string"`
- `HandlerMCP.Types.Integer.Str()` → `"integer"`
- `HandlerMCP.Types.Float.Str()` → `"float"`
- `HandlerMCP.Types.Boolean.Str()` → `"boolean"`
- `HandlerMCP.Types.Array.Str()` → `"array"`

## Обработка результатов

Ваша функция должна вернуть `ToolResult`:

1. **Успех**: Верните строку (автоматически конвертируется).
   ```csharp
   return "Готово!";
   ```
2. **Логическая ошибка**: Верните `ToolResult.Error(...)`. LLM увидит сообщение и попробует исправить запрос.
   ```csharp
   return ToolResult.Error("Неверный формат даты.");
   ```
3. **Критическая ошибка**: Верните `ToolResult.ProtocolError(...)`. Это вызовет ошибку JSON-RPC.
   ```csharp
   return ToolResult.ProtocolError("База данных недоступна.");
   ```

## Запуск

Просто передайте поток ввода в метод `Start`:

```csharp
HandlerMCP.Start(Console.OpenStandardInput());
```

Сервер автоматически начнёт читать команды из STDIN и писать ответы в STDOUT. Никаких дополнительных настроек транспорта.