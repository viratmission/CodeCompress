using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeCompass.Api.Services;

/// <summary>
/// Calls the IBM watsonx.ai Chat API.
/// Requires three environment variables / configuration keys:
///   WatsonX__ApiKey      — IBM Cloud API key
///   WatsonX__ProjectId   — watsonx project ID
///   WatsonX__Url         — e.g. https://us-south.ml.cloud.ibm.com
///   WatsonX__ModelId     — e.g. ibm/granite-3-8b-instruct  (optional, has default)
/// </summary>
public class WatsonxProvider
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WatsonxProvider> _logger;

    private const string IamTokenUrl    = "https://iam.cloud.ibm.com/identity/token";
    private const string GenerationVersion = "2023-05-29";
    private const string ChatApiVersion    = "2024-10-08";
    private const string DefaultModelId   = "ibm/granite-3-8b-instruct";

    // Simple in-memory token cache (per instance)
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public WatsonxProvider(IConfiguration config, IHttpClientFactory httpFactory, ILogger<WatsonxProvider> logger)
    {
        _config      = config;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    private string? GetSetting(string key)
    {
        var val = _config[key];
        if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

        var dblUnderscore = key.Replace(":", "__");
        val = _config[dblUnderscore]
           ?? Environment.GetEnvironmentVariable(dblUnderscore)
           ?? Environment.GetEnvironmentVariable(dblUnderscore, EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable(dblUnderscore, EnvironmentVariableTarget.Machine);

        if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

        var singleUnderscore = key.Replace(":", "_");
        val = _config[singleUnderscore]
            ?? Environment.GetEnvironmentVariable(singleUnderscore)
            ?? Environment.GetEnvironmentVariable(singleUnderscore, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(singleUnderscore, EnvironmentVariableTarget.Machine);

        return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
    }

    /// <summary>Returns null if credentials are not configured.</summary>
    public bool IsConfigured()
    {
        var key = GetSetting("WatsonX:ApiKey");
        var pid = GetSetting("WatsonX:ProjectId");
        var url = GetSetting("WatsonX:Url");
        return !string.IsNullOrWhiteSpace(key)
            && !string.IsNullOrWhiteSpace(pid)
            && !string.IsNullOrWhiteSpace(url);
    }

    public async Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        var apiKey    = GetSetting("WatsonX:ApiKey")    ?? throw new InvalidOperationException("WatsonX:ApiKey is not configured.");
        var projectId = GetSetting("WatsonX:ProjectId") ?? throw new InvalidOperationException("WatsonX:ProjectId is not configured.");
        var baseUrl   = (GetSetting("WatsonX:Url") ?? throw new InvalidOperationException("WatsonX:Url is not configured.")).TrimEnd('/');
        var modelId   = GetSetting("WatsonX:ModelId") ?? DefaultModelId;

        var token = await GetIamTokenAsync(apiKey, ct);
        var client = _httpFactory.CreateClient("watsonx");

        string hostName = "unknown";
        try { hostName = new Uri(baseUrl).Host; } catch { }

        // 1. Primary: POST /ml/v1/text/generation
        var genUrl = $"{baseUrl}/ml/v1/text/generation?version={GenerationVersion}";
        _logger.LogInformation("Calling watsonx text generation API: Host={Host}, Path=/ml/v1/text/generation, Model={ModelId}, ApiKeyConfigured={HasKey}, ProjectIdConfigured={HasPid}",
            hostName, modelId, !string.IsNullOrWhiteSpace(apiKey), !string.IsNullOrWhiteSpace(projectId));

        var promptInput = string.IsNullOrWhiteSpace(systemPrompt)
            ? userMessage
            : $"{systemPrompt}\n\nHuman: {userMessage}\n\nAssistant:";

        var genBody = new
        {
            input = promptInput,
            model_id = modelId,
            project_id = projectId,
            parameters = new
            {
                decoding_method = "greedy",
                max_new_tokens = 2500,
                min_new_tokens = 1,
                repetition_penalty = 1.05
            }
        };

        using (var genReq = new HttpRequestMessage(HttpMethod.Post, genUrl))
        {
            genReq.Content = new StringContent(JsonSerializer.Serialize(genBody), Encoding.UTF8, "application/json");
            genReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            genReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var genResp = await client.SendAsync(genReq, ct);
            _logger.LogInformation("watsonx text generation API returned HTTP status {StatusCode}", genResp.StatusCode);

            if (genResp.IsSuccessStatusCode)
            {
                var body = await genResp.Content.ReadAsStringAsync(ct);
                var parsed = JsonDocument.Parse(body);
                if (parsed.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var text = results[0].GetProperty("generated_text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
                return body;
            }

            var errBody = await genResp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("watsonx text generation error {Status}: {Body}", genResp.StatusCode, errBody);

            if (errBody.Contains("model_not_supported"))
            {
                var fallbackModel = await FindAlternativeModelAsync(client, baseUrl, token, ct);
                if (!string.IsNullOrWhiteSpace(fallbackModel) && fallbackModel != modelId)
                {
                    _logger.LogInformation("Retrying watsonx text generation with available regional model: {FallbackModel}", fallbackModel);
                    var altBody = new
                {
                    input = promptInput,
                    model_id = fallbackModel,
                    project_id = projectId,
                    parameters = new
                    {
                        decoding_method = "greedy",
                        max_new_tokens = 2500,
                        min_new_tokens = 1,
                        repetition_penalty = 1.05
                    }
                };

                using var altReq = new HttpRequestMessage(HttpMethod.Post, genUrl)
                {
                    Content = new StringContent(JsonSerializer.Serialize(altBody), Encoding.UTF8, "application/json")
                };
                altReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                altReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var altResp = await client.SendAsync(altReq, ct);
                var altText = await altResp.Content.ReadAsStringAsync(ct);
                _logger.LogInformation("watsonx text generation (with fallback model {Model}) returned {Status}", fallbackModel, altResp.StatusCode);

                if (altResp.IsSuccessStatusCode)
                {
                    var parsed = JsonDocument.Parse(altText);
                    if (parsed.RootElement.TryGetProperty("results", out var res) && res.GetArrayLength() > 0)
                    {
                        var text = res[0].GetProperty("generated_text").GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return text.Trim();
                    }
                    return altText;
                }

                _logger.LogWarning("watsonx text generation with fallback model {Model} failed: {Status} {Body}", fallbackModel, altResp.StatusCode, altText);
            }
        }
        }

        // 2. Secondary: POST /ml/v1/chat/completions (or /ml/v1/text/chat)
        var chatUrl = $"{baseUrl}/ml/v1/chat/completions?version=2024-05-01";
        _logger.LogInformation("Calling watsonx chat completions API: Host={Host}, Path=/ml/v1/chat/completions, Model={ModelId}", hostName, modelId);

        var chatBody = new
        {
            model_id   = modelId,
            project_id = projectId,
            messages   = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = new[] { new { type = "text", text = userMessage } } },
            },
            max_tokens = 2500
        };

        using (var chatReq = new HttpRequestMessage(HttpMethod.Post, chatUrl))
        {
            chatReq.Content = new StringContent(JsonSerializer.Serialize(chatBody), Encoding.UTF8, "application/json");
            chatReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            chatReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var chatResp = await client.SendAsync(chatReq, ct);
            _logger.LogInformation("watsonx chat completions API returned HTTP status {StatusCode}", chatResp.StatusCode);

            if (chatResp.IsSuccessStatusCode)
            {
                var body = await chatResp.Content.ReadAsStringAsync(ct);
                var parsed = JsonDocument.Parse(body);
                if (parsed.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                    return content ?? string.Empty;
                }
                return body;
            }

            var chatErr = await chatResp.Content.ReadAsStringAsync(ct);
            _logger.LogError("watsonx chat completions error {Status}: {Body}", chatResp.StatusCode, chatErr);

            throw new InvalidOperationException($"watsonx.ai returned {(int)chatResp.StatusCode}: {chatResp.ReasonPhrase}");
        }
    }

    private async Task<string?> FindAlternativeModelAsync(HttpClient client, string baseUrl, string token, CancellationToken ct)
    {
        try
        {
            var specsUrl = $"{baseUrl}/ml/v1/foundation_model_specs?version=2024-05-01&limit=50";
            using var req = new HttpRequestMessage(HttpMethod.Get, specsUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("resources", out var res)) return null;

            var models = new List<string>();
            foreach (var m in res.EnumerateArray())
            {
                if (m.TryGetProperty("model_id", out var mid))
                {
                    var name = mid.GetString();
                    if (!string.IsNullOrEmpty(name)) models.Add(name);
                }
            }

            _logger.LogInformation("watsonx available models in region: [{Models}]", string.Join(", ", models));

            // Preference:
            // 1. Any granite instruct
            var match = models.FirstOrDefault(m => m.Contains("granite") && m.Contains("instruct"));
            if (match != null) return match;

            // 2. Any instruct model (e.g. meta-llama/llama-3-3-70b-instruct, mistralai/mistral-small-3-1-24b-instruct-2503)
            match = models.FirstOrDefault(m => m.Contains("instruct"));
            if (match != null) return match;

            // 3. Any granite model
            match = models.FirstOrDefault(m => m.Contains("granite") && !m.Contains("embedding") && !m.Contains("ttm"));
            if (match != null) return match;

            return models.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query alternative models.");
            return null;
        }
    }

    // ── IAM token (cached for ~55 min) ────────────────────────────────────
    private async Task<string> GetIamTokenAsync(string apiKey, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;

            var client  = _httpFactory.CreateClient("iam");
            var payload = $"grant_type=urn:ibm:params:oauth:grant-type:apikey&apikey={Uri.EscapeDataString(apiKey)}";
            var content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded");

            _logger.LogInformation("Requesting IAM token from IBM Cloud for watsonx authentication...");
            var response = await client.PostAsync(IamTokenUrl, content, ct);
            _logger.LogInformation("IBM Cloud IAM token endpoint returned HTTP status {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Failed to obtain IAM token from IBM Cloud. Status: {Status}, Message: {Err}", response.StatusCode, err);
                throw new InvalidOperationException($"Failed to obtain IAM token from IBM Cloud: {response.StatusCode}");
            }

            var json   = await response.Content.ReadAsStringAsync(ct);
            var parsed = JsonDocument.Parse(json);
            var token  = parsed.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("IAM response did not contain access_token.");

            _cachedToken = token;
            _tokenExpiry = DateTime.UtcNow.AddMinutes(55); // tokens valid 60 min
            return _cachedToken;
        }
        finally { _tokenLock.Release(); }
    }
}
