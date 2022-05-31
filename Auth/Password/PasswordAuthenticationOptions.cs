/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols;

public class PasswordAuthenticationOptions : RemoteAuthenticationOptions
{
    public PasswordAuthenticationOptions()
    {
        ApplicationID = "";
        Key = "";
        CallbackPath = "/id";
        ForwardAuthenticate = null;
    }

    /// <summary>
    /// Gets or sets the absolute URL of the OpenID 2.0 authentication server.
    /// Note: this property is ignored when <see cref="Configuration"/>
    /// or <see cref="ConfigurationManager"/> are set.
    /// </summary>
    public String ApplicationID { get; set; }

    /// <summary>
    /// Gets or sets the URL of the OpenID 2.0 authentication XRDS discovery document.
    /// When the URL is relative, <see cref="Authority"/> must be set and absolute.
    /// Note: this property is ignored when <see cref="Configuration"/>
    /// or <see cref="ConfigurationManager"/> are set.
    /// </summary>
    public String Key { get; set; }
}
