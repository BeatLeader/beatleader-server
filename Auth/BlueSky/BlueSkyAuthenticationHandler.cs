/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using BeatLeader_Server.Services;
using AspNet.Security.OAuth.BlueSky;

namespace BeatLeader_Server.Auth.BlueSky;

public partial class BlueSkyAuthenticationHandler : OAuthHandler<BlueSkyAuthenticationOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BlueSkyAuthenticationHandler> _logger;
    private readonly ATProtocolService _atProtocolService;
    private static readonly Dictionary<string, string?> _dpopNonces = new();
    private static string? _codeVerifier;

    public BlueSkyAuthenticationHandler(
        [NotNull] IOptionsMonitor<BlueSkyAuthenticationOptions> options,
        [NotNull] ILoggerFactory logger,
        [NotNull] UrlEncoder encoder,
        [NotNull] ISystemClock clock,
        [NotNull] IConfiguration configuration,
        [NotNull] ATProtocolService atProtocolService)
        : base(options, logger, encoder, clock)
    {
        _configuration = configuration;
        _logger = logger.CreateLogger<BlueSkyAuthenticationHandler>();
        _atProtocolService = atProtocolService;
    }

    protected override async Task<AuthenticationTicket> CreateTicketAsync(
        [NotNull] ClaimsIdentity identity,
        [NotNull] AuthenticationProperties properties,
        [NotNull] OAuthTokenResponse tokens)
    {
        string endpoint = Options.UserInformationEndpoint;
        var did = properties.Items["sub_did"];

        endpoint = QueryHelpers.AddQueryString(endpoint, "actor", did);
        endpoint = QueryHelpers.AddQueryString(endpoint, "rkey", "self");

        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", tokens.AccessToken);
        request.Headers.Add("DPoP", GenerateDpopToken("GET", endpoint));

        var response = await Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Context.RequestAborted);

        // If the request failed but the server provided a nonce, retry once.
        if (!response.IsSuccessStatusCode && response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
        {
            var uri = new Uri(endpoint);
            _dpopNonces[uri.Host] = nonceValues.FirstOrDefault();
            
            var retryRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            retryRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("DPoP", tokens.AccessToken);
            retryRequest.Headers.Add("DPoP", GenerateDpopToken("GET", endpoint));

            response = await Backchannel.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, Context.RequestAborted);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            await Log.UserProfileErrorAsync(Logger, response, Context.RequestAborted);
            throw new HttpRequestException("An error occurred while retrieving the user profile.");
        }

        // Store DPoP nonce if provided for future requests to this host.
        if (response.Headers.TryGetValues("DPoP-Nonce", out var finalNonceValues))
        {
            var uri = new Uri(endpoint);
            _dpopNonces[uri.Host] = finalNonceValues.FirstOrDefault();
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));

        var principal = new ClaimsPrincipal(identity);
        var context = new OAuthCreatingTicketContext(principal, properties, Context, Scheme, Options, Backchannel, tokens, payload.RootElement);
        context.RunClaimActions(payload.RootElement);

        await Events.CreatingTicket(context);
        return new AuthenticationTicket(context.Principal!, context.Properties, Scheme.Name);
    }

    protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        var result = await base.HandleRemoteAuthenticateAsync();
        if (!result.Succeeded)
        {
            return result;
        }
        
        // Get the DPoP nonce from the response headers
        if (Context.Response.Headers.TryGetValue("DPoP-Nonce", out var nonce))
        {
            // A better key might be the authorization server's host if it's known here.
            var authServerHost = new Uri(Options.AuthorizationEndpoint).Host;
            _dpopNonces[authServerHost] = nonce.ToString();
        }

        return result;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (string.IsNullOrEmpty(properties.RedirectUri))
        {
            properties.RedirectUri = OriginalPathBase + OriginalPath + Request.QueryString;
        }

        // 1. Generate correlation cookie and add correlation ID to properties.
        GenerateCorrelationId(properties);

        // Resolve the handle to get the authorization server
        var (did, authServer) = await _atProtocolService.ResolveHandleToAuthServer("beatleader.com");
        
        var parEndpoint = $"{authServer}/oauth/par";
        var tokenEndpoint = $"{authServer}/oauth/token";
        var authorizationEndpoint = $"{authServer}/oauth/authorize";

        properties.Items["auth_server"] = authServer;
        properties.Items["authorization_endpoint"] = authorizationEndpoint;
        
        // Store the DID in the properties
        properties.Items["did"] = did;

        // Generate PKCE code verifier and challenge
        _codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(_codeVerifier);

        // Store the code verifier in the properties
        properties.Items["code_verifier"] = _codeVerifier;

        // 2. Now protect the properties (which include the correlation ID) into the state parameter.
        var state = Options.StateDataFormat.Protect(properties);

        // Create the PAR request
        var parRequest = new Dictionary<string, string>
        {
            { "client_id", Options.ClientId },
            { "scope", FormatScope() },
            { "response_type", "code" },
            { "redirect_uri", "https://api.beatleader.com/signin-bluesky" },
            { "state", state },
            { "code_challenge", codeChallenge },
            { "code_challenge_method", "S256" }
        };

        // Add client assertion for confidential clients
        if (Options.TokenEndpointAuthMethod == "private_key_jwt")
        {
            var privateKey = _configuration.GetValue<string>("BlueSkySecret");
            if (string.IsNullOrEmpty(privateKey))
            {
                throw new InvalidOperationException("BlueSky private key not configured");
            }

            var clientAssertion = GenerateClientAssertion(privateKey, authServer);
            parRequest.Add("client_assertion_type", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
            parRequest.Add("client_assertion", clientAssertion);
        }

        // Make the PAR request
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, parEndpoint);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        requestMessage.Content = new FormUrlEncodedContent(parRequest);
        
        // Add DPoP header
        requestMessage.Headers.Add("DPoP", GenerateDpopToken("POST", parEndpoint));

        var response = await Backchannel.SendAsync(requestMessage, Context.RequestAborted);

        // If the request failed but the server provided a nonce, retry once.
        if (!response.IsSuccessStatusCode && response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
        {
            var uri = new Uri(parEndpoint);
            _dpopNonces[uri.Host] = nonceValues.FirstOrDefault();
            
            var retryRequestMessage = new HttpRequestMessage(HttpMethod.Post, parEndpoint);
            retryRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            retryRequestMessage.Content = new FormUrlEncodedContent(parRequest);
            retryRequestMessage.Headers.Add("DPoP", GenerateDpopToken("POST", parEndpoint));
            
            response = await Backchannel.SendAsync(retryRequestMessage, Context.RequestAborted);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return;
        }

        // If the final response was successful, store any nonce for the next stage (e.g., token exchange).
        if (response.Headers.TryGetValues("DPoP-Nonce", out var finalNonceValues))
        {
            var uri = new Uri(parEndpoint);
            _dpopNonces[uri.Host] = finalNonceValues.FirstOrDefault();
        }

        var parResponse = await response.Content.ReadAsStringAsync();
        var parData = JsonDocument.Parse(parResponse);
        var requestUri = parData.RootElement.GetProperty("request_uri").GetString();

        // Store the request_uri in the properties
        properties.Items["request_uri"] = requestUri;

        // 3. Manually redirect to the authorization endpoint.
        var challengeUrl = BuildChallengeUrl(properties, "https://api.beatleader.com/signin-bluesky");
        var redirectContext = new RedirectContext<OAuthOptions>(Context, Scheme, Options, properties, challengeUrl);
        await Events.RedirectToAuthorizationEndpoint(redirectContext);
    }

    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri)
    {
        // Get the request_uri from the properties
        if (!properties.Items.TryGetValue("request_uri", out var requestUri) ||
            !properties.Items.TryGetValue("authorization_endpoint", out var authorizationEndpoint) ||
            string.IsNullOrEmpty(authorizationEndpoint))
        {
            throw new InvalidOperationException("Required authentication properties not found");
        }

        // Return the authorization URL with just the client_id and request_uri
        return $"{authorizationEndpoint}?client_id={Options.ClientId}&request_uri={Uri.EscapeDataString(requestUri)}";
    }

    protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
    {
        string tokenEndpoint;
        string authServerOrigin;
        if (context.Properties.Items.TryGetValue("auth_server", out var authServer) && authServer != null) {
            tokenEndpoint = $"{authServer}/oauth/token";
            authServerOrigin = authServer;
        } else {
            // Fallback, though auth_server should always be present from HandleChallengeAsync
            authServerOrigin = new Uri(Options.AuthorizationEndpoint).GetLeftPart(UriPartial.Authority);
            tokenEndpoint = authServerOrigin + "/oauth/token";
        }

        var tokenRequestParameters = new Dictionary<string, string>
        {
            { "client_id", Options.ClientId },
            { "redirect_uri", "https://api.beatleader.com/signin-bluesky" },
            { "code", context.Code },
            { "grant_type", "authorization_code" }
        };

        // Add PKCE code verifier
        if (_codeVerifier != null)
        {
            tokenRequestParameters.Add("code_verifier", _codeVerifier);
        }

        // Add client assertion for confidential clients
        if (Options.TokenEndpointAuthMethod == "private_key_jwt")
        {
            var privateKey = _configuration.GetValue<string>("BlueSkySecret");
            if (string.IsNullOrEmpty(privateKey))
            {
                throw new InvalidOperationException("BlueSky private key not configured");
            }

            var clientAssertion = GenerateClientAssertion(privateKey, authServerOrigin);
            tokenRequestParameters.Add("client_assertion_type", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
            tokenRequestParameters.Add("client_assertion", clientAssertion);
        }

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Add DPoP header
        requestMessage.Headers.Add("DPoP", GenerateDpopToken("POST", tokenEndpoint));

        requestMessage.Content = new FormUrlEncodedContent(tokenRequestParameters);

        var response = await Backchannel.SendAsync(requestMessage, Context.RequestAborted);

        // If the request failed but the server provided a nonce, retry once.
        if (!response.IsSuccessStatusCode && response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
        {
            var uri = new Uri(tokenEndpoint);
            _dpopNonces[uri.Host] = nonceValues.FirstOrDefault();

            var retryRequestMessage = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
            retryRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            retryRequestMessage.Content = new FormUrlEncodedContent(tokenRequestParameters);
            retryRequestMessage.Headers.Add("DPoP", GenerateDpopToken("POST", tokenEndpoint));

            response = await Backchannel.SendAsync(retryRequestMessage, Context.RequestAborted);
        }

        if (response.IsSuccessStatusCode)
        {
            // If the final response was successful, store any nonce for the next stage.
            if (response.Headers.TryGetValues("DPoP-Nonce", out var finalNonceValues))
            {
                var uri = new Uri(tokenEndpoint);
                _dpopNonces[uri.Host] = finalNonceValues.FirstOrDefault();
            }
            var payload = await response.Content.ReadAsStringAsync();
            var responseContent = JsonDocument.Parse(payload);
            context.Properties.Items.Add("sub_did", responseContent.RootElement.GetProperty("sub").GetString());

            return OAuthTokenResponse.Success(responseContent);
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            return OAuthTokenResponse.Failed(new Exception(error));
        }
    }

    private string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Base64UrlEncode(bytes);
    }

    private string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Base64UrlEncode(challengeBytes);
    }

    private string GenerateDpopToken(string method, string url)
    {
        var keyData = JsonSerializer.Deserialize<JsonElement>(_configuration.GetValue<string>("BlueSkySecret"));
        var d = Base64UrlDecode(keyData.GetProperty("d").GetString());
        var x = Base64UrlDecode(keyData.GetProperty("x").GetString());
        var y = Base64UrlDecode(keyData.GetProperty("y").GetString());

        var ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = d,
            Q = new ECPoint { X = x, Y = y }
        });

        var securityKey = new ECDsaSecurityKey(ecdsa);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        // Create the JWK header parameter
        var jwk = new Dictionary<string, object>
        {
            { "kty", "EC" },
            { "crv", "P-256" },
            { "x", Base64UrlEncode(x) },
            { "y", Base64UrlEncode(y) }
        };

        var header = new JwtHeader(credentials);
        header.Remove("typ");
        header.Add("typ", "dpop+jwt");
        header.Add("jwk", jwk);
        
        var payload = new JwtPayload
        {
            { "jti", Guid.NewGuid().ToString() },
            { "htm", method },
            { "htu", url },
            { "iat", DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds() },
            { "exp", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() },
            { "nbf", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds() }
        };

        var uri = new Uri(url);
        _dpopNonces.TryGetValue(uri.Host, out var nonce);

        if (!string.IsNullOrEmpty(nonce))
        {
            payload.Add("nonce", nonce);
        }

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateClientAssertion(string privateKeyJson, string audience)
    {
        var keyData = JsonSerializer.Deserialize<JsonElement>(privateKeyJson);
        var d = Base64UrlDecode(keyData.GetProperty("d").GetString());
        var x = Base64UrlDecode(keyData.GetProperty("x").GetString());
        var y = Base64UrlDecode(keyData.GetProperty("y").GetString());

        if (!keyData.TryGetProperty("kid", out var kidElement) || kidElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("The private key in configuration is missing the 'kid' (Key ID) string property.");
        }
        var kid = kidElement.GetString();

        var ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = d,
            Q = new ECPoint { X = x, Y = y }
        });

        var securityKey = new ECDsaSecurityKey(ecdsa);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        var header = new JwtHeader(credentials);
        header.Add("kid", kid);

        var payload = new JwtPayload
        {
            { "iss", Options.ClientId },
            { "sub", Options.ClientId },
            { "aud", audience },
            { "jti", Guid.NewGuid().ToString() },
            { "iat", DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds() },
            { "exp", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() },
            { "nbf", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds() }
        };

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input;
        output = output.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 0: break;
            case 2: output += "=="; break;
            case 3: output += "="; break;
            default: throw new FormatException("Illegal base64url string!");
        }
        return Convert.FromBase64String(output);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static partial class Log
    {
        internal static async Task UserProfileErrorAsync(ILogger logger, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            UserProfileError(
                logger,
                response.StatusCode,
                response.Headers.ToString(),
                await response.Content.ReadAsStringAsync(cancellationToken));
        }

        [LoggerMessage(1, LogLevel.Error, "An error occurred while retrieving the user profile: the remote server returned a {Status} response with the following payload: {Headers} {Body}.")]
        private static partial void UserProfileError(
            ILogger logger,
            System.Net.HttpStatusCode status,
            string headers,
            string body);

        [LoggerMessage(1, LogLevel.Information, "BlueSky authentication was not authenticated. Failure message: {Message}")]
        public static partial void BlueSkyNotAuthenticated(ILogger logger, string message);

        [LoggerMessage(2, LogLevel.Trace, "BlueSky authentication was skipped because request was not authenticated.")]
        public static partial void BlueSkySkipped(ILogger logger);

        [LoggerMessage(3, LogLevel.Information, "BlueSky authentication was forbidden. Failure message: {Message}")]
        public static partial void BlueSkyForbidden(ILogger logger, string message);
    }
}
