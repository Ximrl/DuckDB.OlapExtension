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
    private static readonly XNamespace XsdNamespace = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace SqlNamespace = "urn:schemas-microsoft-com:xml-sql";

    [GeneratedRegex(@"_x([0-9A-Fa-f]{4})_")]
    private static partial Regex XmlEncodedNameRegex();

    public record TableData(IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<object?[]> Rows);

    public static TableData ParseRowset(XDocument doc)
    {
        var root = doc.Root
            ?? throw new InvalidOperationException("Empty XMLA response.");

        var rootNs = root.GetDefaultNamespace();

        if (!rootNs.NamespaceName.Contains("rowset"))
            throw new InvalidOperationException(
                $"Unsupported XMLA response format: {rootNs.NamespaceName}. " +
                "Only 'rowset' (DAX) is supported by this function.");

        var (columns, xmlNames) = ExtractColumns(root);
        if (columns.Count == 0)
            throw new InvalidOperationException(
                "XMLA response contains no column definitions. " +
                "The schema section is missing or malformed.");

        var rows = ExtractRows(root, rootNs, columns, xmlNames);
        return new TableData(columns, rows);
    }

    private static (List<ColumnInfo> Columns, string[] XmlNames) ExtractColumns(XElement root)
    {
        var columns = new List<ColumnInfo>();
        var xmlNames = new List<string>();

        var rowType = root.Descendants(XsdNamespace + "complexType")
            .FirstOrDefault(ct => ct.Attribute("name")?.Value == "row");

        if (rowType == null) return (columns, xmlNames.ToArray());

        foreach (var element in rowType.Descendants(XsdNamespace + "element"))
        {
            var sqlField = element.Attribute(SqlNamespace + "field")?.Value;
            var elemName = element.Attribute("name")?.Value ?? "";
            var xsdType = element.Attribute("type")?.Value ?? "xsd:string";

            columns.Add(new ColumnInfo(
                sqlField ?? DecodeXmlName(elemName),
                MapXsdType(xsdType)));
            xmlNames.Add(elemName);
        }

        return (columns, xmlNames.ToArray());
    }

    private static List<object?[]> ExtractRows(
        XElement root,
        XNamespace rootNs,
        IReadOnlyList<ColumnInfo> columns,
        string[] xmlNames)
    {
        var nameToIndex = new Dictionary<string, int>(columns.Count, StringComparer.Ordinal);
        for (int i = 0; i < columns.Count; i++)
            nameToIndex[xmlNames[i]] = i;

        var rows = new List<object?[]>();

        foreach (var rowElem in root.Elements(rootNs + "row"))
        {
            // По умолчанию для всех столбцов, включая отсутствующие, устанавливается значение null.
            var row = new object?[columns.Count];

            foreach (var cell in rowElem.Elements())
            {
                if (!nameToIndex.TryGetValue(cell.Name.LocalName, out var idx))
                    // Unknown element — skip.
                    continue;

                row[idx] = ConvertValue(cell, columns[idx]);
            }

            rows.Add(row);
        }

        return rows;
    }

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

            "dateTime" => typeof(DateTime),
            "date" => typeof(DateOnly),
            "time" => typeof(TimeOnly),

            "anyURI" or "QName" or "duration" or "base64Binary" or "hexBinary"
                or "gYear" or "gYearMonth" or "gMonth" or "gMonthDay" or "gDay"
                => typeof(string),

            _ => typeof(string)
        };
    }

    private static object? ConvertValue(XElement cell, ColumnInfo column)
    {
        var type = column.Type;

        // Explicit xsi:nil="true" → NULL.
        var nilAttr = cell.Attribute(XsiNamespace + "nil");
        if (nilAttr?.Value == "true") return null;

        var raw = cell.Value;

        // Empty content: for string it's "", for everything else → NULL.
        // Distinguishes <A/> from <A xsi:nil="true"/> and from <A>value</A>.
        if (raw.Length == 0)
            return type == typeof(string) ? "" : null;

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

        // Date/time. .NET's DateTimeOffset.TryParse без указания зоны молча предлагает локальную
        if (type == typeof(DateTime))
        {
            if (HasExplicitOffset(raw))
            {
                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                                            DateTimeStyles.None, out var dto))
                    return dto.UtcDateTime;   // DateTimeKind.Utc
            }
            else
            {
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out var dt))
                    return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
            }
        }

        if (type == typeof(DateOnly)
            && DateOnly.TryParse(raw, CultureInfo.InvariantCulture,
                                 DateTimeStyles.None, out var dateVal))
            return dateVal;

        if (type == typeof(TimeOnly)
            && TimeOnly.TryParse(raw, CultureInfo.InvariantCulture,
                                 DateTimeStyles.None, out var timeVal))
            return timeVal;

        // Вызов происходит только в том случае, если значение не удалось преобразовать в объявленный тип.
        // Использование исключения в данном случае позволяет избежать скрытой записи строки в числовой столбец.
        throw new InvalidOperationException(
            $"Failed to parse value '{raw}' in column '{column.Name}' " +
            $"as {type.Name}. The value exceeds the declared XSD type " +
            $"or has an unexpected format.");
    }

    /// <summary>
    /// True если XSD dateTime содержит смещение часового пояса
    /// ("Z" или "+HH:MM" / "-HH:MM").
    /// </summary>
    private static bool HasExplicitOffset(string s)
    {
        if (s.Length == 0) return false;
        if (s[^1] == 'Z' || s[^1] == 'z') return true;

        var tIndex = s.IndexOf('T');
        if (tIndex < 0) return false;

        for (int i = tIndex + 1; i < s.Length; i++)
        {
            if (s[i] == '+' || s[i] == '-') return true;
        }
        return false;
    }

    /// <summary>
    /// Декодирует XML-имена вида "_x005B_" в символы.
    /// Согласно спецификации XSD, символы, недопустимые в XML-именах,
    /// заменяются на "_xXXXX_", где XXXX — Unicode-код в hex.
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