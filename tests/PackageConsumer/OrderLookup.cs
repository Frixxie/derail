using System.Data.Common;
using Derail;

namespace PackageConsumer;

internal enum LookupError { InvalidId, NotFound }

// The caller owns the open connection. orders.status is a non-null text column.
internal static class OrderLookup
{
    internal static AsyncReaderResult<DbConnection, string, LookupError> Status(long id) =>
        AsyncReaderResult.Pure<DbConnection, long, LookupError>(id)
            .Ensure(value => value > 0, _ => LookupError.InvalidId)
            .Bind(ReadStatus);

    private static AsyncReaderResult<DbConnection, string, LookupError> ReadStatus(long id) =>
        new(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM orders WHERE id = @id";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = id;
            command.Parameters.Add(parameter);

            var status = (string?)await command.ExecuteScalarAsync(token);
            return Option.FromNullable(status).ToResult(() => LookupError.NotFound);
        });
}
