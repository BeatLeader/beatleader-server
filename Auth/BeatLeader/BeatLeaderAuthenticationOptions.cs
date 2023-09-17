/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using System.Security.Claims;
using static AspNet.Security.OAuth.BeatLeader.BeatLeaderAuthenticationConstants;

namespace AspNet.Security.OAuth.BeatLeader;

/// <summary>
/// Defines a set of options used by <see cref="BeatLeaderAuthenticationHandler"/>.
/// </summary>
public class BeatLeaderAuthenticationOptions : OAuthOptions
{
    public BeatLeaderAuthenticationOptions()
    {
        ClaimsIssuer = BeatLeaderAuthenticationDefaults.Issuer;
        CallbackPath = BeatLeaderAuthenticationDefaults.CallbackPath;

        AuthorizationEndpoint = BeatLeaderAuthenticationDefaults.AuthorizationEndpoint;
        TokenEndpoint = BeatLeaderAuthenticationDefaults.TokenEndpoint;
        UserInformationEndpoint = BeatLeaderAuthenticationDefaults.UserInformationEndpoint;

        Scope.Add("profile");
        Scope.Add(CustomScopes.Clan);

        ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
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
