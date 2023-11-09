using MySql.Data.MySqlClient;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Umbraco.Cms.Persistence.MySql.Services;

public class MySqlDatabaseCreator : IDatabaseCreator
{
    public string ProviderName => Constants.ProviderName;

    public void Create(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);

        // Get connection string without database specific information
        var masterBuilder = new MySqlConnectionStringBuilder(builder.ConnectionString);
        var masterConnectionString = masterBuilder.ConnectionString;

        string fileName = string.Empty,
            database = builder.Database;

        // Create database
        if (!string.IsNullOrEmpty(fileName) && !File.Exists(fileName))
        {
            if (string.IsNullOrWhiteSpace(database))
            {
                // Use a temporary database name
                database = "Umbraco-" + Guid.NewGuid();
            }

            using var connection = new MySqlConnection(masterConnectionString);
            connection.Open();

            using var command = new MySqlCommand(
                $"CREATE DATABASE [{database}] ON (NAME='{database}', FILENAME='{fileName}');" +
                $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" +
                $"EXEC sp_detach_db @dbname='{database}';",
                connection);
            command.ExecuteNonQuery();

            connection.Close();
        }
        else if (!string.IsNullOrEmpty(database))
        {
            using var connection = new MySqlConnection(masterConnectionString);
            connection.Open();

            using var command = new MySqlCommand(
                $"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{database}') " +
                $"CREATE DATABASE {database};",
                connection);
            command.ExecuteNonQuery();

            connection.Close();
        }
    }
}
