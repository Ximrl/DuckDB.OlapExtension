using Microsoft.AnalysisServices.AdomdClient;

internal static class OlapDiagnostics
{
    public static string TestAdomd(string connectionString)
    {
        try
        {
            using var conn = new AdomdConnection(connectionString);
            return $"ADOMD OK. Type: {conn.GetType().FullName}. State: {conn.State}";
        }
        catch (Exception ex)
        {
            return $"ADOMD FAILED: {ex.GetType().Name}: {ex.Message}";
        }
    }
}