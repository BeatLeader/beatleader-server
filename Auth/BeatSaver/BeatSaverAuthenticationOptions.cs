/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using System.Security.Claims;
using static AspNet.Security.OAuth.BeatSaver.BeatSaverAuthenticationConstants;

namespace AspNet.Security.OAuth.BeatSaver;

/// <summary>
/// Defines a set of options used by <see cref="BeatSaverAuthenticationHandler"/>.
/// </summary>
public class BeatSaverAuthenticationOptions : OAuthOptions
{
    public BeatSaverAuthenticationOptions()
    {
        ClaimsIssuer = BeatSaverAuthenticationDefaults.Issuer;
        CallbackPath = BeatSaverAuthenticationDefaults.CallbackPath;

        AuthorizationEndpoint = BeatSaverAuthenticationDefaults.AuthorizationEndpoint;
        TokenEndpoint = BeatSaverAuthenticationDefaults.TokenEndpoint;
        UserInformationEndpoint = BeatSaverAuthenticationDefaults.UserInformationEndpoint;

        Scope.Add("identity");

        ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
    }

    /// <summary>
    /// Gets the list of fields to retrieve from the user information endpoint.
    /// </summary>
    public ISet<string> Fields { get; } = new HashSet<string>();

    /// <summary>
    /// Gets the list of related data to include from the user information endpoint.
    /// </summary>
    public ISet<string> Includes { get; } = new HashSet<string>();
}
