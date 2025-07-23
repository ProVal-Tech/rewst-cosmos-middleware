using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Proval.Rewst;

public partial class Workflow {
    private readonly ILogger<Workflow> _logger;

    public Workflow(ILogger<Workflow> logger) {
        _logger = logger;
    }

    public string GetMasterKeySignature(string verb, string date, string resourceType, string resourceLink, string masterKey) {
        string keyType = "master";
        string tokenVersion = "1.0";
        string authPayload = $"{verb.ToLowerInvariant()}\n{resourceType.ToLowerInvariant()}\n{resourceLink}\n{date.ToLowerInvariant()}\n\n";
        HMACSHA256 hmac = new(Convert.FromBase64String(masterKey));
        byte[] hashPayload = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(authPayload));
        string signature = Convert.ToBase64String(hashPayload);
        string authSet = WebUtility.UrlEncode($"type={keyType}&ver={tokenVersion}&sig={signature}");
        return authSet;
    }

    [Function("Workflow")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req) {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        req.Headers.Keys.ToList().ForEach(k => _logger.LogInformation($"Header: {k} = {req.Headers[k]}"));
        if (!req.Headers.TryGetValue("TargetUri", out StringValues targetUri)) {
            return new BadRequestObjectResult("Missing TargetUri header.");
        }
        if (!req.Headers.TryGetValue("ResourceType", out StringValues resourceType)) {
            return new BadRequestObjectResult("Missing ResourceType header.");
        }
        if (!req.Headers.TryGetValue("Key", out StringValues key)) {
            return new BadRequestObjectResult("Missing Key header.");
        }
        string targetPath = UriBeginningRegex().Replace(targetUri.ToString(), string.Empty).TrimStart('/');
        _logger.LogInformation("Target Path: {TargetPath}", targetPath);
        string date = DateTime.UtcNow.ToString("r");
        _logger.LogInformation("Date: {Date}", date);
        string auth = GetMasterKeySignature(
            req.Method,
            date,
            resourceType.ToString(),
            targetPath,
            key.ToString()
        );
        _logger.LogInformation("Authorization: {Auth}", auth);
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    [GeneratedRegex(@"^https?:\/\/[^\/]+")]
    private static partial Regex UriBeginningRegex();
}