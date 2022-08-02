/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using AspNet.Security.OAuth.BeatSaver;
using Microsoft.AspNetCore.Authentication;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to add Patreon authentication capabilities to an HTTP application pipeline.
/// </summary>
public static class BeatSaverAuthenticationExtensions
{
    /// <summary>
    /// Adds <see cref="BeatSaverAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables Patreon authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBeatSaver([NotNull] this AuthenticationBuilder builder)
    {
        return builder.AddBeatSaver(BeatSaverAuthenticationDefaults.AuthenticationScheme, options => { });
    }

    /// <summary>
    /// Adds <see cref="BeatSaverAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables Patreon authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configuration">The delegate used to configure the OpenID 2.0 options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBeatSaver(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] Action<BeatSaverAuthenticationOptions> configuration)
    {
        return builder.AddBeatSaver(BeatSaverAuthenticationDefaults.AuthenticationScheme, configuration);
    }

    /// <summary>
    /// Adds <see cref="BeatSaverAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables Patreon authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="scheme">The authentication scheme associated with this instance.</param>
    /// <param name="configuration">The delegate used to configure the Patreon options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBeatSaver(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] string scheme,
        [NotNull] Action<BeatSaverAuthenticationOptions> configuration)
    {
        return builder.AddBeatSaver(scheme, BeatSaverAuthenticationDefaults.DisplayName, configuration);
    }

    /// <summary>
    /// Adds <see cref="BeatSaverAuthenticationHandler"/> to the specified
    /// <see cref="AuthenticationBuilder"/>, which enables Patreon authentication capabilities.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="scheme">The authentication scheme associated with this instance.</param>
    /// <param name="caption">The optional display name associated with this instance.</param>
    /// <param name="configuration">The delegate used to configure the Patreon options.</param>
    /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddBeatSaver(
        [NotNull] this AuthenticationBuilder builder,
        [NotNull] string scheme,
        [MaybeNull] string caption,
        [NotNull] Action<BeatSaverAuthenticationOptions> configuration)
    {
        return builder.AddOAuth<BeatSaverAuthenticationOptions, BeatSaverAuthenticationHandler>(scheme, caption, configuration);
    }
}
