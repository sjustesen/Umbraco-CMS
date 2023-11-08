using Umbraco.Cms.Core.Mapping;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Umbraco.Cms.Persistence.MySql.Services;

public class MySqlSpecificMapperFactory : IProviderSpecificMapperFactory
{
    public string ProviderName => Constants.ProviderName;

    public NPocoMapperCollection Mappers => new(() => new[] { new UmbracoDefaultMapper() });
}
