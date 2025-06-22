/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

namespace AspNet.Security.OAuth.BlueSky;

/// <summary>
/// Contains constants specific to the <see cref="BlueSkyAuthenticationHandler"/>.
/// </summary>
public static class BlueSkyAuthenticationConstants
{
    public static class Claims
    {
        public const string Did = "urn:bluesky:did";
        public const string Handle = "urn:bluesky:handle";
    }
}
