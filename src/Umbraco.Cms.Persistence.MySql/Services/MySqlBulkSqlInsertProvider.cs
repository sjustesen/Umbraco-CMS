using System.Data;
using System.Data.Common;
using MySqlConnector;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.MySql.Services;

/// <summary>
///     A bulk sql insert provider for Sql Server
/// </summary>
public class MySqlBulkSqlInsertProvider : IBulkSqlInsertProvider
{
    public string ProviderName => Constants.ProviderName;

    public int BulkInsertRecords<T>(IUmbracoDatabase database, IEnumerable<T> records)
    {
        T[] recordsA = records.ToArray();
        if (recordsA.Length == 0)
        {
            return 0;
        }

        PocoData? pocoData = database.PocoDataFactory.ForType(typeof(T));
        if (pocoData == null)
        {
            throw new InvalidOperationException("Could not find PocoData for " + typeof(T));
        }

        return BulkInsertRecordsMySql(database, pocoData, recordsA);
    }

    /// <summary>
    ///     Bulk-insert records using MySql BulkCopy method.
    /// </summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="database">The database.</param>
    /// <param name="pocoData">The PocoData object corresponding to the record's type.</param>
    /// <param name="records">The records.</param>
    /// <returns>The number of records that were inserted.</returns>
    private int BulkInsertRecordsMySql<T>(IUmbracoDatabase database, PocoData pocoData, IEnumerable<T> records)
    {
        // TODO: The main reason this exists is because the NPoco InsertBulk method doesn't return the number of items.
        // It is worth investigating the performance of this vs NPoco's because we use a custom BulkDataReader
        // which in theory should be more efficient than NPocos way of building up an in-memory DataTable.

        // create command against the original database.Connection
        using (DbCommand command = database.CreateCommand(database.Connection, CommandType.Text, string.Empty))
        {
            // use typed connection and transaction or SqlBulkCopy
            var tConnection = NPocoDatabaseExtensions.GetTypedConnection<MySqlConnector.MySqlConnection>(database.Connection);
            var tTransaction = NPocoDatabaseExtensions.GetTypedTransaction<MySqlConnector.MySqlTransaction>(command.Transaction);
            var tableName = pocoData.TableInfo.TableName;

            if (database.SqlContext.SqlSyntax is not MySqlSyntaxProvider syntax)
            {
                throw new NotSupportedException("SqlSyntax must be MySqlSyntaxProvider.");
            }

            var copy = new MySqlBulkCopy(tConnection, tTransaction)
            {
                // 0 = no bulk copy timeout. If a timeout occurs it will be an connection/command timeout.
                BulkCopyTimeout = 0,
                DestinationTableName = tableName,
                // be consistent with NPoco: https://github.com/schotime/NPoco/blob/5117a55fde57547e928246c044fd40bd00b2d7d1/src/NPoco.MySql/SqlBulkCopyHelper.cs#L50
            };
            using (var bulkReader = new MySql.Services.PocoDataDataReader<T, MySqlSyntaxProvider>(records, pocoData, syntax))
            {
                // we need to add column mappings here because otherwise columns will be matched by their order and if the order of them are different in the DB compared
                // to the order in which they are declared in the model then this will not work, so instead we will add column mappings by name so that this explicitly uses
                // the names instead of their ordering.
                foreach (MySqlBulkCopyColumnMapping col in bulkReader.ColumnMappings)
                {
                    copy.ColumnMappings.Add(col);
                }

                copy.WriteToServer(bulkReader);
                return bulkReader.RecordsAffected;
            }
        }
    }
}
