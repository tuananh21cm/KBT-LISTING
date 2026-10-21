using Application.Interfaces;

namespace Infrastructure.Storage;

/// <summary>
/// Bunny CDN storage HTTP client implementation.
/// </summary>
public class BunnyStorageService : IBunnyStorageService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingProvider _settingProvider;

    /// <summary>
    /// Initializes the Bunny storage service.
    /// </summary>
    public BunnyStorageService(HttpClient httpClient, ISettingProvider settingProvider)
    {
        _httpClient = httpClient;
        _settingProvider = settingProvider;
    }

    /// <inheritdoc />
    public async Task UploadAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var options = await BunnyConfigResolver.ResolveAsync(_settingProvider, cancellationToken);
        var url = BuildStorageUrl(options, storagePath);
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Add("AccessKey", options.AccessKey);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is not (System.Net.HttpStatusCode.Created or System.Net.HttpStatusCode.OK))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Bunny upload failed ({(int)response.StatusCode}): {body}");
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var options = await BunnyConfigResolver.ResolveAsync(_settingProvider, cancellationToken);
        var url = BuildStorageUrl(options, storagePath);
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("AccessKey", options.AccessKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is not (System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.NotFound))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Bunny delete failed ({(int)response.StatusCode}): {body}");
        }
    }

    /// <inheritdoc />
    public async Task<string?> BuildPublicUrlAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var options = await BunnyConfigResolver.ResolveAsync(_settingProvider, cancellationToken);
        return BuildPublicUrl(options, storagePath);
    }

    private static string? BuildPublicUrl(BunnyStorageConfig options, string storagePath)
    {
        if (string.IsNullOrWhiteSpace(options.CdnBaseUrl))
        {
            return null;
        }

        var baseUrl = options.CdnBaseUrl.TrimEnd('/');
        var path = storagePath.TrimStart('/');
        return $"{baseUrl}/{path}";
    }

    private static string BuildStorageUrl(BunnyStorageConfig options, string storagePath)
    {
        var hostname = options.Hostname.TrimEnd('/');
        var zone = options.StorageZoneName.Trim('/');
        var path = storagePath.TrimStart('/');
        return $"https://{hostname}/{zone}/{path}";
    }
}
