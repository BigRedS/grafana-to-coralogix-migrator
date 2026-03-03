namespace GrafanaToCx.Core.Converter;

public interface ILogqlToLuceneTranslator
{
    string Convert(string logqlExpr);
    IReadOnlyList<string> ExtractGroupByFields(string logqlExpr);
}

public sealed class LogqlToLuceneTranslator : ILogqlToLuceneTranslator
{
    public string Convert(string logqlExpr) => LogqlToLuceneConverter.Convert(logqlExpr);

    public IReadOnlyList<string> ExtractGroupByFields(string logqlExpr) =>
        LogqlToLuceneConverter.ExtractGroupByFields(logqlExpr);
}
