/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using System.ComponentModel;
using AngleSharp.Html.Parser;
using AspNet.Security.OpenId;
using AspNet.Security.Oculus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Contains the methods required to ensure that the configuration used by
/// the OpenID 2.0 generic handler is in a consistent and valid state.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class OculusAuthenticationInitializer<TOptions, THandler> : IPostConfigureOptions<TOptions>
    where TOptions : OculusAuthenticationOptions, new()
    where THandler : OculusAuthenticationHandler<TOptions>
{
    private readonly IDataProtectionProvider _dataProtectionProvider;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenIdAuthenticationInitializer{TOptions, THandler}"/> class.
    /// </summary>
    public OculusAuthenticationInitializer(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    /// <summary>
    /// Populates the default OpenID 2.0 handler options and ensure
    /// that the configuration is in a consistent and valid state.
    /// </summary>
    /// <param name="name">The authentication scheme associated with the handler instance.</param>
    /// <param name="options">The options instance to initialize.</param>
    public void PostConfigure(string name, TOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The options instance name cannot be null or empty.", nameof(name));
        }

        if (options.DataProtectionProvider == null)
        {
            options.DataProtectionProvider = _dataProtectionProvider;
        }

        if (options.Backchannel == null)
        {
#pragma warning disable CA2000
            options.Backchannel = new HttpClient(options.BackchannelHttpHandler ?? new HttpClientHandler());
#pragma warning disable CA2000
            options.Backchannel.DefaultRequestHeaders.UserAgent.ParseAdd("ASP.NET Core OpenID 2.0 middleware");
            options.Backchannel.Timeout = options.BackchannelTimeout;
            options.Backchannel.MaxResponseContentBufferSize = 1024 * 1024 * 10; // 10 MB
        }

        
    }
}
