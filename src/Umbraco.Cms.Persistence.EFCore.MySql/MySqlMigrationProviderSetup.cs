using MySql.EntityFrameworkCore;
using Umbraco.Cms.Persistence.EFCore.Migrations;

namespace Umbraco.Cms.Persistence.EFCore.MySql;

public class MySqlMigrationProviderSetup : IMigrationProviderSetup
{
    public string ProviderName => "MySql";

    public void Setup(DbContextOptionsBuilder builder, string? connectionString)
    {
        builder.UseMySqlServer(connectionString, x => x.MigrationsAssembly(GetType().Assembly.FullName));
    }
}
