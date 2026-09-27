using Microsoft.AspNetCore.Mvc;
using CodeCompass.Api.Services;

namespace CodeCompass.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromServices] WatsonxProvider watsonx)
    {
        var isWatsonx = watsonx.IsConfigured();
        return Ok(new
        {
            status = "healthy",
            service = "CodeCompass API",
            watsonxConfigured = isWatsonx,
            aiProvider = isWatsonx ? "IBM watsonx • Granite" : "Repository-grounded mode"
        });
    }
}
