using Derail;
using Npgsql;

namespace Orders;

internal static class Transactions
{
    public static async Task<Result<T, TError>> InTransaction<T, TError>(
        this AsyncReaderResult<NpgsqlConnection, T, TError> workflow, NpgsqlDataSource db, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        // Npgsql runs every command on the connection's open transaction, so steps need only the connection.
        await using var transaction = await connection.BeginTransactionAsync(token);
        var result = await workflow.Run(connection, token);
        if (result.IsOk) await transaction.CommitAsync(token);
        return result;
    }
}
