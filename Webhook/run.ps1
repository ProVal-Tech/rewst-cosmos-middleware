using namespace System.Net

# Input bindings are passed in via param block.
param($Request, $TriggerMetadata)

# Write to the Azure Functions log stream.
Write-Information 'Recieved a webhook request'
$Request.Headers.Keys
$tokenType = $Request.Headers['TokenType']
$tokenVersion = $Request.Headers['TokenVersion']
$targetUri = $Request.Headers['TargetUri']
$targetPath = $targetUri -replace '^https?://[^/]+', ''
$resourceType = $Request.Headers['ResourceType']
$key = $Request.Headers['Key']
$utcNow = [DateTime]::UtcNow.ToString("R")
$authPayload = @"
$($Request.Method.ToString().ToLowerInvariant())
$($resourceType.ToString().ToLowerInvariant())
$($targetPath.ToString().ToLowerInvariant())
$($utcNow.ToString().ToLowerInvariant())


"@
$hmacSha256 = [System.Security.Cryptography.HMACSHA256]::new()
$hmacSha256.Key = [System.Convert]::FromBase64String($key)
$hashPayload = $hmacSha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($authPayload))
$signature = [System.Convert]::ToBase64String($hashPayload)
$authSet = [WebUtility]::UrlEncode("type=$tokenType&ver=$tokenVersion&sig=$signature")

$cosmosRequest = [System.Net.Http.HttpRequestMessage]::new($Request.Method, $targetUri)
$cosmosRequest.Content = [System.Net.Http.StringContent]::new($Request.Body, [System.Text.Encoding]::UTF8, 'application/json')
$cosmosRequest.Headers.Add('Accept', 'application/json')
$cosmosRequest.Headers.Add('Authorization', $authSet)
$cosmosRequest.Headers.Add('x-ms-date', $utcNow)
$cosmosRequest.Headers.Add('x-ms-version', '2018-12-31')

$httpClient = [System.Net.Http.HttpClient]::new()
try {
    $response = $httpClient.SendAsync($cosmosRequest).Result
    Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
        StatusCode = $response.StatusCode
        Body = $response.Content.ReadAsStringAsync().Result
    })
} catch {
    Write-Error "An error occurred while processing the webhook: $_"
    Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
        StatusCode = [HttpStatusCode]::InternalServerError
        Body = "An error occurred while processing the webhook: $_"
    })
}
