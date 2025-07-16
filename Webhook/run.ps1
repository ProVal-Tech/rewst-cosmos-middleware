using namespace System.Net

# Input bindings are passed in via param block.
param($Request, $TriggerMetadata)

function New-MasterKeySignature() {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)][string]$Verb,
        [Parameter(Mandatory)][string]$Date,
        [Parameter(Mandatory)][string]$ResourceType,
        [Parameter(Mandatory)][string]$ResourceLink,
        [Parameter(Mandatory)][string]$Key
    )
    $keyType = "master";
    $tokenVersion = "1.0";
    $authPayload = "$($Verb.ToLowerInvariant())`n$($ResourceType.ToLowerInvariant())`n$ResourceLink`n$($Date.ToLowerInvariant())`n`n"
    $hmacSha256 = [System.Security.Cryptography.HMACSHA256]::new()
    $hmacSha256.Key = [System.Convert]::FromBase64String($key)
    $hashPayload = $hmacSha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($authPayload))
    $signature = [System.Convert]::ToBase64String($hashPayload)
    $authSet = [WebUtility]::UrlEncode("type=$keyType&ver=$tokenVersion&sig=$signature")
    return $authSet
}

# Write to the Azure Functions log stream.
Write-Information 'Recieved a webhook request'
$Request.Headers.Keys | ForEach-Object { Write-Information "$_ : $($Request.Headers[$_])" }
$tu = $Request.Headers['TargetUri']
$tp = ($targetUri -replace '^https?:\/\/[^\/]+', '').TrimStart('/').TrimEnd('/docs')
Write-Information "Target Path: $targetPath"
$rt = $Request.Headers['ResourceType']
$key = $Request.Headers['Key']
$utcNow = [DateTime]::UtcNow.ToString('r')
$auth = New-MasterKeySignature -Verb $Request.Method -Date $utcNow -ResourceType $rt -ResourceLink $tp -Key $key
$cosmosRequest = [System.Net.Http.HttpRequestMessage]::new($Request.Method, $tu)
$cosmosRequest.Content = [System.Net.Http.StringContent]::new($Request.Body, [System.Text.Encoding]::UTF8, 'application/json')
$cosmosRequest.Headers.Add('Accept', 'application/json')
$cosmosRequest.Headers.Add('authorization', $auth)
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
