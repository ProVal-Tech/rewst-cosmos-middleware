using System.Drawing.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Proval.Rewst;

public partial class Workflow(ILogger<Workflow> logger) {
    private readonly ILogger<Workflow> _logger = logger;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip
    };

    public static string GetMasterKeySignature(string verb, string date, string resourceType, string resourceLink, string key) {
        var keyType = "master";
        var tokenVersion = "1.0";
        var payload = $"{verb.ToString().ToLowerInvariant()}\n{resourceType.ToString().ToLowerInvariant()}\n{resourceLink}\n{date.ToLowerInvariant()}\n\n";

        var hmacSha256 = new HMACSHA256 { Key = Convert.FromBase64String(key) };
        var hashPayload = hmacSha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToBase64String(hashPayload);
        var authSet = WebUtility.UrlEncode($"type={keyType}&ver={tokenVersion}&sig={signature}");

        return authSet;
    }

    [Function("Workflow")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function)] HttpRequest req) {
        try {
            if (!req.Headers.TryGetValue("CosmosKey", out StringValues cosmosKey)) {
                return new BadRequestObjectResult("Missing CosmosKey header.");
            }
            if (!req.Headers.TryGetValue("AccountName", out StringValues accountName)) {
                return new BadRequestObjectResult("Missing AccountName header.");
            }

            string baseUrl = $"https://{accountName}.documents.azure.com";
            string? targetFunction = req.Query["targetFunction"];
            if (string.IsNullOrEmpty(targetFunction)) {
                return new BadRequestObjectResult("Missing targetFunction query parameter.");
            }
            string? databaseId = req.Query["databaseId"];
            string? containerId = req.Query["containerId"];
            if (string.IsNullOrEmpty(databaseId) || string.IsNullOrEmpty(containerId)) {
                return new BadRequestObjectResult("Missing databaseId or containerId query parameters.");
            }
            string response;
            switch (targetFunction) {
                case "ListDocuments":
                    response = await ListDocuments(baseUrl, databaseId, containerId, cosmosKey.ToString());
                    break;
                default:
                    return new BadRequestObjectResult($"Unknown targetFunction: {targetFunction}");
            }
            return new OkObjectResult(response);
        } catch (Exception ex) {
            _logger.LogError(ex, "An error occurred while processing the request.");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
    private struct CosmosContinuationTokenResponse {
        public string Token { get; set; }
    }
    private struct CosmosListDocumentsResponse {
        public IEnumerable<JsonObject> Documents { get; set; }
    }
    async Task<string> ListDocuments(string baseUrl, string databaseId, string containerId, string cosmosKey) {
        string method = "GET";
        var resourceType = "docs";
        var resourceLink = $"dbs/{databaseId}/colls/{containerId}";
        var requestDateString = DateTime.UtcNow.ToString("r");
        var auth = GetMasterKeySignature(method, requestDateString, resourceType, resourceLink, cosmosKey);
        HttpClient httpClient = new();
        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        httpClient.DefaultRequestHeaders.Add("authorization", auth);
        httpClient.DefaultRequestHeaders.Add("x-ms-date", requestDateString);
        httpClient.DefaultRequestHeaders.Add("x-ms-version", "2018-12-31");

        var requestUri = new Uri($"{baseUrl}/{resourceLink}/docs");
        var httpRequest = new HttpRequestMessage { Method = HttpMethod.Parse(method), RequestUri = requestUri };

        _logger.LogInformation($"Requesting documents from {requestUri} with method {method}");
        var httpResponse = await httpClient.SendAsync(httpRequest);
        if (httpResponse.IsSuccessStatusCode) {
            _logger.LogInformation($"Successfully fetched documents from {requestUri}");
            _logger.LogInformation($"Attempting to read response content as CosmosListDocumentsResponse");
            var responseContent = await httpResponse.Content.ReadFromJsonAsync<CosmosListDocumentsResponse>();
            if (responseContent.Documents == null) {
                _logger.LogWarning("No documents found in response content");
                responseContent.Documents = [];
                return JsonSerializer.Serialize(responseContent);
            }
            _logger.LogInformation($"Successfully read response content, found {responseContent.Documents?.Count() ?? 0} documents");
            _logger.LogInformation($"Checking for continuation tokens in response headers");
            while (httpResponse.Headers.TryGetValues("x-ms-continuation", out var continuationValues)) {
                _logger.LogInformation($"Found continuation token: {string.Join(", ", continuationValues)}");
                var continuationTokenJson = continuationValues.FirstOrDefault();
                if (string.IsNullOrEmpty(continuationTokenJson)) {
                    _logger.LogInformation("No continuation token found, breaking out of loop");
                    break;
                }
                _logger.LogInformation($"Parsing continuation token: {continuationTokenJson}");
                var continuationToken = JsonSerializer.Deserialize<CosmosContinuationTokenResponse>(continuationTokenJson).Token;
                _logger.LogInformation($"Continuing request with token: {continuationToken}");
                httpClient.DefaultRequestHeaders.Remove("x-ms-continuation");
                httpClient.DefaultRequestHeaders.Add("x-ms-continuation", continuationToken);
                _logger.LogInformation($"Sending request with continuation token: {continuationToken}");
                // Create a new HttpRequestMessage for each request
                var continuationRequest = new HttpRequestMessage { Method = HttpMethod.Parse(method), RequestUri = requestUri };
                httpResponse = await httpClient.SendAsync(continuationRequest);
                if (!httpResponse.IsSuccessStatusCode) {
                    throw new Exception($"Error fetching continuation token: {httpResponse.StatusCode}");
                }
                _logger.LogInformation($"Successfully fetched continuation documents from {requestUri}");
                _logger.LogInformation($"Attempting to read continuation response content as CosmosListDocumentsResponse");
                var continuationContent = await httpResponse.Content.ReadFromJsonAsync<CosmosListDocumentsResponse>();
                _logger.LogInformation($"Successfully read continuation response content, found {continuationContent.Documents?.Count() ?? 0} documents");
                _logger.LogInformation($"Merging continuation documents into main response content");
                responseContent.Documents = (responseContent.Documents ?? []).Concat(continuationContent.Documents ?? []);
            }
            return JsonSerializer.Serialize(responseContent);
        } else {
            var errorContent = await httpResponse.Content.ReadAsStringAsync();
            throw new Exception($"Error listing documents: {httpResponse.StatusCode} - {errorContent}");
        }
    }
}