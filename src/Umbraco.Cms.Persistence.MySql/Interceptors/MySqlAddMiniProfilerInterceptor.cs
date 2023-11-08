using System.Data.Common;
using NPoco;
using StackExchange.Profiling;
using StackExchange.Profiling.Data;

namespace Umbraco.Cms.Persistence.MySql.Interceptors;

public class MySqlAddMiniProfilerInterceptor : MySqlConnectionInterceptor
{
    public override DbConnection OnConnectionOpened(IDatabase database, DbConnection conn)
        => new ProfiledDbConnection(conn, MiniProfiler.Current);
}
