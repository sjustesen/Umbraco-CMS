using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore;

using Umbraco.Cms.Persistence.EFCore.Migrations;

namespace Umbraco.Cms.Persistence.EFCore.MySql;

public class MySqlMigrationProviderSetup : IMigrationProviderSetup
{
    public string ProviderName => "MySql";


    /// <summary>
    ///     Provider Setup
    /// </summary>
    public void Setup(DbContextOptionsBuilder builder, string? connectionString)
    {
        builder.UseMySQL(connectionString!, x => x.MigrationsAssembly(GetType().Assembly.FullName));
    }
}
