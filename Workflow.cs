using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Proval.Rewst;

public class Workflow
{
    private readonly ILogger<Workflow> _logger;

    public Workflow(ILogger<Workflow> logger)
    {
        _logger = logger;
    }

    [Function("Workflow")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}