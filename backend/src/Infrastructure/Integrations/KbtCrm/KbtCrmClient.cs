using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.KbtCrm;
using Application.Interfaces;
using Domain.Exceptions;

namespace Infrastructure.Integrations.KbtCrm;

/// <inheritdoc />
public sealed class KbtCrmClient : IKbtCrmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;

    public KbtCrmClient(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public async Task<KbtCrmOrgDto> GetOrgAsync(string baseUrl, string agentSecret, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/agent/org");
        request.Headers.TryAddWithoutValidation("x-agent-secret", agentSecret);

        using var response = await _http.SendAsync(request, cancellationToken);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!response.IsSuccessStatusCode || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            // The prod SPA answers 200 text/html when the /api-proxy prefix is missing — surface that, not a JSON error.
            throw new BusinessException(
                $"CRM /agent/org trả {(int)response.StatusCode} {mediaType}. Kiểm tra kbtcrm.base_url (prod cần /api-proxy) và secret.");
        }

        var org = await response.Content.ReadFromJsonAsync<KbtCrmOrgDto>(JsonOptions, cancellationToken)
            ?? throw new BusinessException("CRM /agent/org trả body rỗng.");
        if (!org.Success)
        {
            throw new BusinessException("CRM /agent/org trả success=false.");
        }

        return org;
    }
}
