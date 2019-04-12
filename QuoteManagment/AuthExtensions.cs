using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace QuoteManagment
{
    public static class AzureAdAuthenticationBuilderExtensions
    {
        public static AuthenticationBuilder AddAzureAd(this AuthenticationBuilder builder, IConfiguration config)
        =>  builder.AddAzureAd(_ => { });
        

        public static AuthenticationBuilder AddAzureAd(this AuthenticationBuilder builder, Action<AzureAdOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureAzureOptions>();
            builder.AddOpenIdConnect();
            return builder;
        }

        private class ConfigureAzureOptions : IConfigureNamedOptions<OpenIdConnectOptions>
        {
            private static AzureAdOptions _azureOptions;

            public ConfigureAzureOptions(IConfiguration configuration)
            {
                _azureOptions =  new AzureAdOptions()
                {
                    AuthClientId = configuration["AuthClientId"],
                    AuthTenantId = configuration["AuthTenantId"],
                    AuthInstance = configuration["AuthInstance"],
                };
            }

            public void Configure(string name, OpenIdConnectOptions options)
            {
                options.ClientId = _azureOptions.AuthClientId;
                options.Authority = $"{_azureOptions.AuthInstance}{_azureOptions.AuthTenantId}/v2.0";   // V2 specific
                options.UseTokenLifetime = true;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters.ValidateIssuer = false;     // accept several tenants
            }

            public void Configure(OpenIdConnectOptions options)
            {
                Configure(Options.DefaultName, options);
            }
        }
    }
}
