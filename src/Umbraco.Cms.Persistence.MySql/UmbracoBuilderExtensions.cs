using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySql.Data.MySqlClient;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.DistributedLocking;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;
using Umbraco.Cms.Persistence.MySql.Interceptors;
using Umbraco.Cms.Persistence.MySql.Services;

namespace Umbraco.Cms.Persistence.MySql;

/// <summary>
///     SQLite support extensions for IUmbracoBuilder.
/// </summary>
public static class UmbracoBuilderExtensions
{
    /// <summary>
    ///     Add required services for SQL Server support.
    /// </summary>
    public static IUmbracoBuilder AddUmbracoMySqlSupport(this IUmbracoBuilder builder)
    {
        builder.Services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IProviderSpecificMapperFactory, MySqlSpecificMapperFactory>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISqlSyntaxProvider, MySqlSyntaxProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IBulkSqlInsertProvider, MySqlBulkSqlInsertProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseCreator, MySqlDatabaseCreator>());

        builder.Services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IDatabaseProviderMetadata, MySqlDatabaseProviderMetadata>());

        builder.Services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IDistributedLockingMechanism, MySqlDistributedLockingMechanism>());

        builder.Services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IProviderSpecificInterceptor, MySqlAddMiniProfilerInterceptor>());
        builder.Services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IProviderSpecificInterceptor, MySqlAddRetryPolicyInterceptor>());

        DbProviderFactories.UnregisterFactory(Constants.ProviderName);
        DbProviderFactories.RegisterFactory(Constants.ProviderName, MySqlClientFactory.Instance);

        NPocoMySqlDatabaseExtensions.ConfigureNPocoBulkExtensions();

        // Support provider name set by the configuration API for connection string environment variables
        builder.Services.ConfigureAll<ConnectionStrings>(options =>
        {
            if (options.ProviderName == "MySql")
            {
                options.ProviderName = Constants.ProviderName;
            }
        });

        return builder;
    }
}
