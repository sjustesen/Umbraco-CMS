using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Constants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Cms.Web.BackOffice.Security
{

    /// <summary>
    /// Antiforgery implementation for the Umbraco back office
    /// </summary>
    /// <remarks>
    /// This is a wrapper around the global/default <see cref="IAntiforgery"/> .net service. Because this service is a single/global
    /// object and all of it is internal we don't have the flexibility to create our own segregated service so we have to work around
    /// that limitation by wrapping the default and doing a few tricks to have this segregated for the Back office only.
    /// </remarks>
    public class BackOfficeAntiforgery : IBackOfficeAntiforgery
    {
        private readonly IAntiforgery _internalAntiForgery;
        private GlobalSettings _globalSettings;

        public BackOfficeAntiforgery(IOptionsMonitor<GlobalSettings> globalSettings)
        {
            // NOTE: This is the only way to create a separate IAntiForgery service :(
            // Everything in netcore is internal. I have logged an issue here https://github.com/dotnet/aspnetcore/issues/22217
            // but it will not be handled so we have to revert to this.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAntiforgery(x =>
            {
                x.HeaderName = Constants.Web.AngularHeadername;
                x.Cookie.Name = Constants.Web.CsrfValidationCookieName;
            });
            ServiceProvider container = services.BuildServiceProvider();
            _internalAntiForgery = container.GetRequiredService<IAntiforgery>();
            _globalSettings = globalSettings.CurrentValue;
            globalSettings.OnChange(x => {
                _globalSettings = x;
            });
        }

        /// <inheritdoc />
        public async Task<Attempt<string>> ValidateRequestAsync(HttpContext httpContext)
        {
            try
            {
                await _internalAntiForgery.ValidateRequestAsync(httpContext);
                return Attempt<string>.Succeed();
            }
            catch (Exception ex)
            {
                return Attempt.Fail(ex.Message);
            }
        }

        /// <inheritdoc />
        public void GetAndStoreTokens(HttpContext httpContext)
        {
            AntiforgeryTokenSet set = _internalAntiForgery.GetAndStoreTokens(httpContext);

            if (set.RequestToken == null)
            {
                throw new InvalidOperationException("Could not resolve a request token.");
            }

            // We need to set 2 cookies:
            // The cookie value that angular will use to set a header value on each request - we need to manually set this here
            // The validation cookie value generated by the anti-forgery helper that we validate the header token against - set above in GetAndStoreTokens
            httpContext.Response.Cookies.Append(
                Constants.Web.AngularCookieName,
                set.RequestToken,
                new CookieOptions
                {
                    Path = "/",
                    //must be js readable
                    HttpOnly = false,
                    Secure = _globalSettings.UseHttps
                });
        }

    }
}
