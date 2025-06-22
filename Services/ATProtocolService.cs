using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BeatLeader_Server.Services
{
    public class ATProtocolService
    {
        private readonly ILogger<ATProtocolService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public ATProtocolService(
            ILogger<ATProtocolService> logger,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<(string did, string authServer)> ResolveHandleToAuthServer(string handle)
        {
            // Remove @ if present
            handle = handle.TrimStart('@');

            // Step 1: Convert handle to DID via DNS TXT record
            var did = await ResolveHandleToDid(handle);
            if (string.IsNullOrEmpty(did))
            {
                throw new Exception($"Could not resolve handle {handle} to DID");
            }

            // Step 2: Resolve DID to service endpoint
            var serviceEndpoint = await ResolveDidToServiceEndpoint(did);
            if (string.IsNullOrEmpty(serviceEndpoint))
            {
                throw new Exception($"Could not resolve DID {did} to service endpoint");
            }

            // Step 3: Get resource server metadata
            var resourceServer = await GetResourceServerMetadata(serviceEndpoint);
            if (string.IsNullOrEmpty(resourceServer))
            {
                throw new Exception($"Could not get resource server metadata for {serviceEndpoint}");
            }

            // Step 4: Get authorization server metadata
            var authServer = await GetAuthorizationServerMetadata(resourceServer);
            if (string.IsNullOrEmpty(authServer))
            {
                throw new Exception($"Could not get authorization server metadata for {resourceServer}");
            }

            return (did, authServer);
        }

        private async Task<string> ResolveHandleToDid(string handle)
        {
            try
            {
                var dnsQuery = $"_atproto.{handle}";
                var response = await DnsClient.Default
                 .ResolveAsync(DomainName.Parse(dnsQuery), RecordType.Txt);

                return response.AnswerRecords[0].ToString().Split("\"")[1].Substring(4);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving handle to DID: {Handle}", handle);
                return string.Empty;
            }
        }

        private async Task<string> ResolveDidToServiceEndpoint(string did)
        {
            try
            {
                var didMethod = did.Split(':')[1];
                var url = $"https://{didMethod}.directory/{did}";
                var response = await _httpClient.GetStringAsync(url);
                var doc = JsonDocument.Parse(response);
                var serviceEndpoint = doc.RootElement
                    .GetProperty("service")
                    .EnumerateArray()
                    .FirstOrDefault(s => s.GetProperty("type").GetString() == "AtprotoPersonalDataServer")
                    .GetProperty("serviceEndpoint")
                    .GetString();

                return serviceEndpoint ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving DID to service endpoint: {Did}", did);
                return string.Empty;
            }
        }

        private async Task<string> GetResourceServerMetadata(string serviceEndpoint)
        {
            try
            {
                var url = $"{serviceEndpoint.TrimEnd('/')}/.well-known/oauth-protected-resource";
                var response = await _httpClient.GetStringAsync(url);
                var doc = JsonDocument.Parse(response);
                var authServer = doc.RootElement
                    .GetProperty("authorization_servers")
                    .EnumerateArray()
                    .FirstOrDefault()
                    .GetString();

                return authServer ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting resource server metadata: {ServiceEndpoint}", serviceEndpoint);
                return string.Empty;
            }
        }

        private async Task<string> GetAuthorizationServerMetadata(string resourceServer)
        {
            try
            {
                var url = $"{resourceServer.TrimEnd('/')}/.well-known/oauth-authorization-server";
                var response = await _httpClient.GetStringAsync(url);
                var doc = JsonDocument.Parse(response);
                var issuer = doc.RootElement
                    .GetProperty("issuer")
                    .GetString();

                return issuer ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting authorization server metadata: {ResourceServer}", resourceServer);
                return string.Empty;
            }
        }
    }
} 