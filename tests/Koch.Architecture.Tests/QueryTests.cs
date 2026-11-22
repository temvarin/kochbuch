using Kusto.Language;
using Kusto.Language.Symbols;

namespace Koch.Architecture.Tests;

/// <summary>
/// The queries in infra/queries parse and use only tables and columns that exist. Log Analytics would otherwise say so
/// only when someone runs them, which is when something is already wrong (docs/adr/0009).
/// </summary>
public class QueryTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "queries");

    /// <summary>
    /// The workspace tables the queries read, with the columns they use (Azure Monitor reference, "AppRequests" etc.);
    /// a column added here must exist there.
    /// </summary>
    private static readonly GlobalState Workspace = GlobalState.Default.WithDatabase(new DatabaseSymbol("workspace",
        new TableSymbol("AppRequests", "(TimeGenerated:datetime, Id:string, Name:string, Url:string, Success:bool, "
            + "ResultCode:string, DurationMs:real, OperationId:string, ParentId:string, AppRoleName:string, Properties:dynamic)"),
        new TableSymbol("AppDependencies", "(TimeGenerated:datetime, Id:string, Name:string, Target:string, DependencyType:string, "
            + "Data:string, Success:bool, ResultCode:string, DurationMs:real, OperationId:string, ParentId:string, AppRoleName:string, "
            + "Properties:dynamic)"),
        new TableSymbol("AppTraces", "(TimeGenerated:datetime, Message:string, SeverityLevel:int, OperationId:string, "
            + "ParentId:string, AppRoleName:string, Properties:dynamic)"),
        new TableSymbol("AppExceptions", "(TimeGenerated:datetime, ExceptionType:string, OuterMessage:string, "
            + "OperationId:string, ParentId:string, AppRoleName:string, Properties:dynamic)"),
        new TableSymbol("Usage", "(TimeGenerated:datetime, DataType:string, Quantity:real, IsBillable:bool)")));

    public static TheoryData<string> Queries()
    {
        var queries = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Folder, "*.kql").Order(StringComparer.Ordinal)) queries.Add(Path.GetFileName(file));
        return queries;
    }

    [Fact]
    public void The_queries_are_there() => Assert.True(Queries().Count >= 6);

    [Theory, MemberData(nameof(Queries))]
    public void Each_query_parses_and_names_only_real_tables_and_columns(string name)
    {
        var code = KustoCode.ParseAndAnalyze(File.ReadAllText(Path.Combine(Folder, name)), Workspace);

        var errors = code.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Message} at {d.Start}").ToList();
        Assert.True(errors.Count == 0, $"{name}:\n{string.Join('\n', errors)}");
    }
}
