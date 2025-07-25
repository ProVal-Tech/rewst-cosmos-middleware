using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Proval.Rewst;

public partial class Workflow(ILogger<Workflow> logger) {
    private readonly ILogger<Workflow> _logger = logger;

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
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req) {
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
        await ListDocuments(baseUrl, databaseId, containerId, cosmosKey.ToString());
        return new OkObjectResult("Documents listed successfully.");
    }

    static async Task ListDocuments(string baseUrl, string databaseId, string containerId, string cosmosKey) {
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

        var httpResponse = await httpClient.SendAsync(httpRequest);
    }
}