﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using AspNet.Security.OAuth.BeatLeader;
using Microsoft.AspNetCore.Authentication;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to add Patreon authentication capabilities to an HTTP application pipeline.
/// </summary>
public static class BeatLeaderAuthenticationExtensions
{
    /// <summary>
    /// Adds <see cref="BeatLeaderAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables Patreon authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBeatLeader([NotNull] this AuthenticationBuilder builder)
    {
        return builder.AddBeatLeader(BeatLeaderAuthenticationDefaults.AuthenticationScheme, options => { });
    }

    /// <summary>
    /// Adds <see cref="BeatLeaderAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables Patreon authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configuration">The delegate used to configure the OpenID 2.0 options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBeatLeader(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] Action<BeatLeaderAuthenticationOptions> configuration)
    {
        return builder.AddBeatLeader(BeatLeaderAuthenticationDefaults.AuthenticationScheme, configuration);
    }

    /// <summary>
    /// Adds <see cref="BeatLeaderAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables Patreon authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="scheme">The authentication scheme associated with this instance.</param>
    /// <param name="configuration">The delegate used to configure the Patreon options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBeatLeader(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] string scheme,
        [NotNull] Action<BeatLeaderAuthenticationOptions> configuration)
    {
        return builder.AddBeatLeader(scheme, BeatLeaderAuthenticationDefaults.DisplayName, configuration);
    }

    /// <summary>
    /// Adds <see cref="BeatLeaderAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables Patreon authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="scheme">The authentication scheme associated with this instance.</param>
    /// <param name="caption">The optional display name associated with this instance.</param>
    /// <param name="configuration">The delegate used to configure the Patreon options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBeatLeader(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] string scheme,
        [MaybeNull] string caption,
        [NotNull] Action<BeatLeaderAuthenticationOptions> configuration)
    {
        return builder.AddOAuth<BeatLeaderAuthenticationOptions, BeatLeaderAuthenticationHandler>(scheme, caption, configuration);
    }
}
