/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using BeatLeader_Server.Auth.BlueSky;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using System.Security.Claims;
using static AspNet.Security.OAuth.BlueSky.BlueSkyAuthenticationConstants;

namespace AspNet.Security.OAuth.BlueSky;

/// <summary>
/// Defines a set of options used by <see cref="BlueSkyAuthenticationHandler"/>.
/// </summary>
public class BlueSkyAuthenticationOptions : OAuthOptions
{
    public BlueSkyAuthenticationOptions()
    {
        ClaimsIssuer = BlueSkyAuthenticationDefaults.Issuer;
        CallbackPath = BlueSkyAuthenticationDefaults.CallbackPath;

        AuthorizationEndpoint = BlueSkyAuthenticationDefaults.AuthorizationEndpoint;
        TokenEndpoint = BlueSkyAuthenticationDefaults.TokenEndpoint;
        UserInformationEndpoint = BlueSkyAuthenticationDefaults.UserInformationEndpoint;

        ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "did");
        ClaimActions.MapJsonKey(ClaimTypes.Name, "handle");

        // Required for BlueSky OAuth
        TokenEndpointAuthMethod = "private_key_jwt";
        TokenEndpointAuthSigningAlgorithm = "ES256";
        UsePkce = true;
        UseDPoP = true;

        Scope.Add("atproto");
    }

    /// <summary>
    /// Gets the list of fields to retrieve from the user information endpoint.
    /// </summary>
    public ISet<string> Fields { get; } = new HashSet<string>();

    /// <summary>
    /// Gets the list of related data to include from the user information endpoint.
    /// </summary>
    public ISet<string> Includes { get; } = new HashSet<string>();
    public string TokenEndpointAuthMethod { get; }
    public string TokenEndpointAuthSigningAlgorithm { get; }
    public bool UseDPoP { get; }
}
