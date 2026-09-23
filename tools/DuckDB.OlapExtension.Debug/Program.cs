using Microsoft.AnalysisServices.AdomdClient;
using System.Xml.Linq;

var connectionString = "Data Source=localhost;Initial Catalog=qOLAP;Integrated Security=SSPI;";
var query = "EVALUATE VALUES('DataSources'[Code])";

try
{
    using var connection = new AdomdConnection(connectionString);
    connection.Open();
    Console.WriteLine("Connection opened.");

    using var command = new AdomdCommand(query, connection);
    using var xmlReader = command.ExecuteXmlReader();

    var doc = XDocument.Load(xmlReader);
    var raw = doc.ToString();
    Console.WriteLine("=== RAW XML (first 1500 chars) ===");
    Console.WriteLine(raw.Substring(0, Math.Min(1500, raw.Length)));
    Console.WriteLine();

    var root = doc.Root!;
    Console.WriteLine($"Root namespace: {root.GetDefaultNamespace().NamespaceName}");
    Console.WriteLine();

    // Вызываем наш реальный парсер
    var parsed = XmlaParser.ParseRowset(doc);

    Console.WriteLine($"=== PARSED ({parsed.Columns.Count} columns, {parsed.Rows.Count} rows) ===");
    Console.WriteLine("Columns: " + string.Join(", ", parsed.Columns.Select(c => $"{c.Name} ({c.Type.Name})")));
    Console.WriteLine();

    foreach (var row in parsed.Rows)
    {
        Console.Write("  ");
        for (int i = 0; i < row.Length; i++)
        {
            Console.Write($"{parsed.Columns[i].Name}={row[i] ?? "<null>"}  ");
        }
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.GetType().FullName}: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}

Console.WriteLine();
Console.WriteLine("Press Enter to exit...");
Console.ReadLine();