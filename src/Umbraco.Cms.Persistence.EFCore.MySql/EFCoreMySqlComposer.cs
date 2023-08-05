using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Persistence.EFCore.Migrations;

namespace Umbraco.Cms.Persistence.EFCore.MySql;

public class EFCoreMySqlComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IMigrationProvider, MySqlMigrationProvider>();
        builder.Services.AddSingleton<IMigrationProviderSetup, MySqlMigrationProviderSetup>();
    }
}
