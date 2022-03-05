/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using AspNet.Security.Oculus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class OculusAuthenticationExtensions
{
   public static AuthenticationBuilder AddOculus(
         this AuthenticationBuilder builder,
         Action<OculusAuthenticationOptions> configuration)
    {
        return builder.AddOculus<OculusAuthenticationOptions, OculusAuthenticationHandler<OculusAuthenticationOptions>>(configuration);
    }
    public static AuthenticationBuilder AddOculus<TOptions, THandler>(
        this AuthenticationBuilder builder,
        Action<TOptions> configuration)
        where TOptions : OculusAuthenticationOptions, new()
        where THandler : OculusAuthenticationHandler<TOptions>
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        // Note: TryAddEnumerable() is used here to ensure the initializer is only registered once.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<TOptions>,
                                        OculusAuthenticationInitializer<TOptions, THandler>>());

        return builder.AddScheme<TOptions, THandler>("oculus", "Oculus token", configuration);
    }
}
