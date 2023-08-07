using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbraco.Cms.Persistence.MySql;

/// <summary>
///     Automatically adds SQL Server support to Umbraco when this project is referenced.
/// </summary>
public class MySqlComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
        => builder.AddUmbracoMySqlSupport();
}
