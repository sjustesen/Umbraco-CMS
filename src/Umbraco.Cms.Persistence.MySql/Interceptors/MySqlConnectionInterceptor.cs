using System.Data.Common;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Umbraco.Cms.Persistence.MySql.Interceptors;

public abstract class MySqlConnectionInterceptor : IProviderSpecificConnectionInterceptor
{
    public string ProviderName => Constants.ProviderName;

    public abstract DbConnection OnConnectionOpened(IDatabase database, DbConnection conn);

    public virtual void OnConnectionClosing(IDatabase database, DbConnection conn)
    {
    }
}
