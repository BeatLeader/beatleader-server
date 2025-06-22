/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using AspNet.Security.OAuth.BlueSky;
using BeatLeader_Server.Auth.BlueSky;
using Microsoft.AspNetCore.Authentication;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to add BlueSky authentication capabilities to an HTTP application pipeline.
/// </summary>
public static class BlueSkyAuthenticationExtensions
{
    /// <summary>
    /// Adds <see cref="BlueSkyAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables BlueSky authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBlueSky([NotNull] this AuthenticationBuilder builder)
    {
        return builder.AddBlueSky(BlueSkyAuthenticationDefaults.AuthenticationScheme, options => { });
    }

    /// <summary>
    /// Adds <see cref="BlueSkyAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables BlueSky authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configuration">The delegate used to configure the BlueSky options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBlueSky(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] Action<BlueSkyAuthenticationOptions> configuration)
    {
        return builder.AddBlueSky(BlueSkyAuthenticationDefaults.AuthenticationScheme, configuration);
    }

    /// <summary>
    /// Adds <see cref="BlueSkyAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables BlueSky authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="scheme">The authentication scheme associated with this instance.</param>
    /// <param name="configuration">The delegate used to configure the BlueSky options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBlueSky(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] string scheme,
        [NotNull] Action<BlueSkyAuthenticationOptions> configuration)
    {
        return builder.AddBlueSky(scheme, BlueSkyAuthenticationDefaults.DisplayName, configuration);
    }

    /// <summary>
    /// Adds <see cref="BlueSkyAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables BlueSky authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="scheme">The authentication scheme associated with this instance.</param>
    /// <param name="caption">The optional display name associated with this instance.</param>
    /// <param name="configuration">The delegate used to configure the BlueSky options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBlueSky(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] string scheme,
        [MaybeNull] string caption,
        [NotNull] Action<BlueSkyAuthenticationOptions> configuration)
    {
        return builder.AddOAuth<BlueSkyAuthenticationOptions, BlueSkyAuthenticationHandler>(scheme, caption, configuration);
    }
}
