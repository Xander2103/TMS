using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// Counts the INSERT / UPDATE / DELETE statements a context sends, so a test can prove that a
/// path is read-only or writes exactly the rows it should. EF Core on SQLite executes writes
/// through ExecuteReader (RETURNING) as well as ExecuteNonQuery and batches several statements
/// into one command, so every executing hook is inspected and the command text is counted per
/// statement. Pass it as an extra interceptor to <see cref="SqliteTestDbContext"/>.
/// </summary>
public sealed class WriteCommandCounter : DbCommandInterceptor
{
    private static readonly Regex WriteStatement = new(
        @"^\s*(INSERT|UPDATE|DELETE)\b", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<string> _statements = [];

    public int Inserts { get; private set; }
    public int Updates { get; private set; }
    public int Deletes { get; private set; }
    public int Writes => Inserts + Updates + Deletes;

    /// <summary>First line of every write statement seen since the last reset, for failure messages.</summary>
    public IReadOnlyList<string> Statements => _statements;

    public void Reset()
    {
        Inserts = Updates = Deletes = 0;
        _statements.Clear();
    }

    private void Inspect(DbCommand command)
    {
        var text = command.CommandText;
        foreach (Match match in WriteStatement.Matches(text))
        {
            switch (match.Groups[1].Value.ToUpperInvariant())
            {
                case "INSERT": Inserts++; break;
                case "UPDATE": Updates++; break;
                default: Deletes++; break;
            }

            var lineEnd = text.IndexOf('\n', match.Index);
            _statements.Add(text[match.Index..(lineEnd < 0 ? text.Length : lineEnd)].Trim());
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Inspect(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Inspect(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Inspect(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Inspect(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Inspect(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Inspect(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
