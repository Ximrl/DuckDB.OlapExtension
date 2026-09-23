using System.Xml.Linq;
using DuckDB.ExtensionKit.DataChunk.Writer;
using DuckDB.ExtensionKit.Native;
using DuckDB.ExtensionKit.TableFunctions;
using Microsoft.AnalysisServices.AdomdClient;
using static XmlaParser;

internal static class OlapTableFunction
{
    public static TableFunction Bind(IReadOnlyList<IDuckDBValueReader> args)
    {
        var connectionString = args[0].GetValue<string>();
        var mdxQuery = args[1].GetValue<string>();

        var parsed = Fetch(connectionString, mdxQuery);
        return new TableFunction(parsed.Columns, parsed.Rows);
    }

    public static void Map(object? item, IDuckDBDataWriter[] writers, ulong rowIndex)
    {
        var row = (object?[])item!;
        for (int i = 0; i < row.Length && i < writers.Length; i++)
            WriteDynamic(writers[i], row[i], rowIndex);
    }

    private static TableData Fetch(string connectionString, string mdxQuery)
    {
        using var connection = new AdomdConnection(connectionString);
        connection.Open();

        using var command = new AdomdCommand(mdxQuery, connection);
        using var xmlReader = command.ExecuteXmlReader();

        var doc = XDocument.Load(xmlReader);
        return XmlaParser.ParseRowset(doc);
    }

    /// <summary>
    /// AOT-safe запись значения в вектор DuckDB
    /// </summary>
    private static void WriteDynamic(IDuckDBDataWriter writer, object? value, ulong rowIndex)
    {
        if (value is null) { writer.WriteNull(rowIndex); return; }

        switch (value)
        {
            case string v: writer.WriteValue(v, rowIndex); break;
            case bool v: writer.WriteValue(v, rowIndex); break;

            case sbyte v: writer.WriteValue(v, rowIndex); break;
            case byte v: writer.WriteValue(v, rowIndex); break;
            case short v: writer.WriteValue(v, rowIndex); break;
            case ushort v: writer.WriteValue(v, rowIndex); break;
            case int v: writer.WriteValue(v, rowIndex); break;
            case uint v: writer.WriteValue(v, rowIndex); break;
            case long v: writer.WriteValue(v, rowIndex); break;
            case ulong v: writer.WriteValue(v, rowIndex); break;

            case double v: writer.WriteValue(v, rowIndex); break;
            case float v: writer.WriteValue((double)v, rowIndex); break;
            case decimal v: writer.WriteValue(v, rowIndex); break;

            case DateTime v: writer.WriteValue(v, rowIndex); break;
            case DateTimeOffset v: writer.WriteValue(v, rowIndex); break;
            case DateOnly v: writer.WriteValue(v, rowIndex); break;
            case TimeOnly v: writer.WriteValue(v, rowIndex); break;

            default:
                writer.WriteValue(value.ToString() ?? string.Empty, rowIndex);
                break;
        }
    }
}