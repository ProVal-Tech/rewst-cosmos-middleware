using namespace System.Net

# Input bindings are passed in via param block.
param($Request, $TriggerMetadata)

# Write to the Azure Functions log stream.
Write-Information 'Recieved a webhook request'
$Request.Body.Keys | ForEach-Object { Write-Information "$_ : $($Request.Body.$_)" }

Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
        StatusCode = [HttpStatusCode]::OK
        Body = 'Webhook received successfully.'
    })