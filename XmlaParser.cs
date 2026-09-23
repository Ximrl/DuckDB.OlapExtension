using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DuckDB.ExtensionKit.TableFunctions;

/// <summary>
/// Парсер XMLA-ответов формата "rowset" (типичный для DAX-запросов к табличным моделям Analysis Services).
/// Использует только System.Xml.Linq и source-generated Regex — полностью совместим с Native AOT.
/// </summary>
public static partial class XmlaParser
{
    // Стандартные XML namespace, определённые W3C и Microsoft.
    // Значения фиксированы спецификациями и не могут меняться между серверами.
    private static readonly XNamespace XsdNamespace = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace SqlNamespace = "urn:schemas-microsoft-com:xml-sql";

    /// <summary>
    /// Source-generated regex для декодирования экранированных XML-имён.
    /// В Native AOT RegexOptions.Compiled игнорируется ->
    /// используется генератор, который создаёт оптимизированный код на этапе компиляции.
    /// </summary>
    [GeneratedRegex(@"_x([0-9A-Fa-f]{4})_")]
    private static partial Regex XmlEncodedNameRegex();

    public record TableData(IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<object?[]> Rows);

    /// <summary>
    /// Разбирает XMLA-ответ формата "rowset" (DAX к табличным моделям).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Если ответ пустой, не содержит namespace "rowset" или схему колонок.
    /// </exception>
    public static TableData ParseRowset(XDocument doc)
    {
        var root = doc.Root
            ?? throw new InvalidOperationException("Empty XMLA response.");

        var rootNs = root.GetDefaultNamespace();

        if (!rootNs.NamespaceName.Contains("rowset"))
            throw new InvalidOperationException(
                $"Unsupported XMLA response format: {rootNs.NamespaceName}. " +
                "Only 'rowset' (DAX) is supported by this function.");

        var columns = ExtractColumns(root);
        if (columns.Count == 0)
            throw new InvalidOperationException(
                "XMLA response contains no column definitions. " +
                "The schema section is missing or malformed.");

        var rows = ExtractRows(root, rootNs, columns);
        return new TableData(columns, rows);
    }

    private static List<ColumnInfo> ExtractColumns(XElement root)
    {
        var columns = new List<ColumnInfo>();
        var rowType = root.Descendants(XsdNamespace + "complexType")
            .FirstOrDefault(ct => ct.Attribute("name")?.Value == "row");

        if (rowType == null) return columns;

        foreach (var element in rowType.Descendants(XsdNamespace + "element"))
        {
            var sqlField = element.Attribute(SqlNamespace + "field")?.Value;
            var elemName = element.Attribute("name")?.Value ?? "";
            var xsdType = element.Attribute("type")?.Value ?? "xsd:string";

            columns.Add(new ColumnInfo(
                sqlField ?? DecodeXmlName(elemName),
                MapXsdType(xsdType)));
        }

        return columns;
    }

    private static List<object?[]> ExtractRows(
        XElement root,
        XNamespace rootNs,
        IReadOnlyList<ColumnInfo> columns)
    {
        var rows = new List<object?[]>();

        foreach (var rowElem in root.Elements(rootNs + "row"))
        {
            var row = new object?[columns.Count];
            int colIdx = 0;

            foreach (var cell in rowElem.Elements())
            {
                if (colIdx >= columns.Count) break;
                row[colIdx] = ConvertValue(cell.Value, columns[colIdx].Type);
                colIdx++;
            }

            // Недостающие ячейки (если сервер вернул их меньше, чем колонок)
            // остаются null. DuckDB воспримет это как NULL.
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Отображает XSD-тип в .NET-тип. Отрезает namespace-префикс,
    /// чтобы корректно обрабатывать варианты "xsd:string", "xs:string", "string".
    /// </summary>
    private static Type MapXsdType(string xsdType)
    {
        var local = xsdType.Contains(':')
            ? xsdType[(xsdType.IndexOf(':') + 1)..]
            : xsdType;

        return local switch
        {
            "string" => typeof(string),
            "boolean" => typeof(bool),

            "byte" => typeof(sbyte),
            "unsignedByte" => typeof(byte),
            "short" => typeof(short),
            "unsignedShort" => typeof(ushort),
            "int" or "integer" => typeof(int),
            "unsignedInt" => typeof(uint),
            "long" => typeof(long),
            "unsignedLong" => typeof(ulong),

            "double" or "float" => typeof(double),
            "decimal" => typeof(decimal),

            // DateTimeOffset сохраняет информацию о часовом поясе,
            // которую DateTime теряет (например, "2024-01-15T10:30:00+05:00").
            "dateTime" => typeof(DateTimeOffset),
            "date" => typeof(DateOnly),
            "time" => typeof(TimeOnly),

            // Прочие XSD-типы приводим к строке, чтобы не терять данные.
            "anyURI" or "QName" or "duration" or "base64Binary" or "hexBinary"
                or "gYear" or "gYearMonth" or "gMonth" or "gMonthDay" or "gDay"
                => typeof(string),

            _ => typeof(string)
        };
    }

    private static object? ConvertValue(string raw, Type type)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (type == typeof(string)) return raw;

        // Целые типы со знаком и без.
        if (type == typeof(sbyte)
            && sbyte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbyteVal))
            return sbyteVal;

        if (type == typeof(byte)
            && byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteVal))
            return byteVal;

        if (type == typeof(short)
            && short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortVal))
            return shortVal;

        if (type == typeof(ushort)
            && ushort.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ushortVal))
            return ushortVal;

        if (type == typeof(int)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
            return intVal;

        if (type == typeof(uint)
            && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintVal))
            return uintVal;

        if (type == typeof(long)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
            return longVal;

        if (type == typeof(ulong)
            && ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongVal))
            return ulongVal;

        if (type == typeof(double)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
            return doubleVal;

        if (type == typeof(decimal)
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalVal))
            return decimalVal;

        // Логические значения. XMLA обычно присылает "true"/"false",
        // но на всякий случай принимаем "1"/"0".
        if (type == typeof(bool))
        {
            if (bool.TryParse(raw, out var boolVal)) return boolVal;
            if (raw == "1") return true;
            if (raw == "0") return false;
        }

        // Даты и время.
        if (type == typeof(DateTimeOffset)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var dtoVal))
            return dtoVal;

        if (type == typeof(DateOnly)
            && DateOnly.TryParse(raw, CultureInfo.InvariantCulture,
                                 DateTimeStyles.None, out var dateVal))
            return dateVal;

        if (type == typeof(TimeOnly)
            && TimeOnly.TryParse(raw, CultureInfo.InvariantCulture,
                                 DateTimeStyles.None, out var timeVal))
            return timeVal;

        return raw;
    }

    /// <summary>
    /// Декодирует XML-имена вида "_x005B_" в символы
    /// Согласно спецификации XSD, символы, недопустимые в XML-именах, заменяются на "_xXXXX_", где XXXX — Unicode-код в hex.
    /// </summary>
    private static string DecodeXmlName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        return XmlEncodedNameRegex().Replace(
            name,
            m => int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber,
                              CultureInfo.InvariantCulture, out var code)
                 ? ((char)code).ToString()
                 : m.Value);
    }
}
