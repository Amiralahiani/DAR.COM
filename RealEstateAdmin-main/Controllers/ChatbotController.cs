using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Net.Http.Json;

namespace RealEstateAdmin.Controllers;

[Authorize]
public class ChatbotController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatbotController> _logger;

    public ChatbotController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ChatbotController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost("/chat")]
    [EnableRateLimiting("chatbot")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { response = "Message invalide." });
        }

        if (request.Message.Length > 2000)
        {
            return BadRequest(new { response = "Message trop long." });
        }

        var chatbotBaseUrl = _configuration["ChatbotApi:BaseUrl"] ?? "http://127.0.0.1:8000";
        var timeoutSeconds = Math.Clamp(_configuration.GetValue<int?>("ChatbotApi:TimeoutSeconds") ?? 20, 5, 120);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var shopBaseUrl = $"{Request.Scheme}://{Request.Host.Value}";

            using var response = await client.PostAsJsonAsync(
                $"{chatbotBaseUrl.TrimEnd('/')}/chat",
                new
                {
                    message = request.Message.Trim(),
                    user_id = userId,
                    shop_base_url = shopBaseUrl
                });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Le backend chatbot a répondu avec le code {StatusCode}.", response.StatusCode);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { response = "Service chatbot indisponible." });
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatResponse>();
            if (payload == null || string.IsNullOrWhiteSpace(payload.Response))
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { response = "Réponse chatbot invalide." });
            }

            return Ok(new { response = payload.Response });
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timeout lors de l'appel au backend chatbot.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { response = "Service chatbot indisponible." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'appel au backend chatbot.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { response = "Service chatbot indisponible." });
        }
    }

    public sealed class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        public string? Response { get; set; }
    }
}
