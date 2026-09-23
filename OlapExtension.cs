using DuckDB.ExtensionKit;
using DuckDB.ExtensionKit.Native;
using DuckDB.ExtensionKit.ScalarFunctions;
using DuckDB.ExtensionKit.TableFunctions;


[DuckDBExtension]
public static partial class OlapExtension
{
    private static void RegisterFunctions(DuckDBConnection connection)
    {
        // Диагностика: проверяет, что ADOMD.NET работоспособен в AOT-бинарнике.
        // Соединение не открываем — только создаём объект.
        connection.RegisterScalarFunction<string, string>(
            "olap_test_conn",
            (string connectionString) => OlapDiagnostics.TestAdomd(connectionString));

        // Основная функция: выполняет MDX/DAX-запрос и возвращает таблицу
        connection.RegisterTableFunction<string, string>(
            "query_olap",
            OlapTableFunction.Bind,
            OlapTableFunction.Map);
    }
}