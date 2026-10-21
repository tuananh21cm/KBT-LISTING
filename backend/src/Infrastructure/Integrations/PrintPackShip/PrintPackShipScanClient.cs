using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Interfaces;
using Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Integrations.PrintPackShip;

/// <summary>
/// Typed HttpClient for PrintPackShip track-scan batches.
/// Does not retry Send — ambiguous failures must surface as Unknown.
/// </summary>
public sealed class PrintPackShipScanClient : IPrintPackShipScanClient
{
    private readonly HttpClient _httpClient;
    private readonly PrintPackShipOptions _settings;
    private readonly ILogger<PrintPackShipScanClient> _logger;

    /// <summary>Creates the client.</summary>
    public PrintPackShipScanClient(
        HttpClient httpClient,
        IOptions<PrintPackShipOptions> options,
        ILogger<PrintPackShipScanClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PrintPackShipBatchCreateResult> CreateTrackingBatchAsync(
        IReadOnlyList<string> trackingNumbers,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            tracking_numbers = trackingNumbers,
            notes = notes ?? string.Empty,
        };

        var (kind, status, body, error) = await PostAsync(
            "batches/tracking",
            payload,
            cancellationToken);

        if (kind == PrintPackShipCallKind.Unknown)
        {
            return new PrintPackShipBatchCreateResult
            {
                Kind = PrintPackShipCallKind.Unknown,
                HttpStatus = status,
                RawJson = body,
                Error = error,
                Message = error,
            };
        }

        if (!TryParseEnvelope(body, out var root, out var parseError))
        {
            return new PrintPackShipBatchCreateResult
            {
                Kind = PrintPackShipCallKind.Unknown,
                HttpStatus = status,
                RawJson = body,
                Error = parseError,
                Message = parseError,
            };
        }

        var success = ReadBool(root, "success");
        var message = ReadString(root, "message");
        long? batchId = null;
        string? remoteStatus = null;
        string? processingStatus = null;
        int? trackingCount = null;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            batchId = ReadLong(data, "batch_id");
            remoteStatus = ReadString(data, "status");
            processingStatus = ReadString(data, "processing_status");
            trackingCount = ReadInt(data, "tracking_count");
        }

        var clearFail = kind == PrintPackShipCallKind.Failed || !success;
        if (clearFail)
        {
            _logger.LogWarning(
                "PrintPackShip create batch failed (HTTP {Status}): {Message}",
                status,
                message ?? error);
        }

        return new PrintPackShipBatchCreateResult
        {
            Kind = clearFail ? PrintPackShipCallKind.Failed : PrintPackShipCallKind.Ok,
            Success = success,
            Message = message,
            BatchId = batchId,
            Status = remoteStatus,
            ProcessingStatus = processingStatus,
            TrackingCount = trackingCount,
            RawJson = body,
            HttpStatus = status,
            Error = clearFail ? (message ?? error) : null,
        };
    }

    /// <inheritdoc />
    public async Task<PrintPackShipBatchSendResult> SendBatchAsync(
        long batchId,
        CancellationToken cancellationToken = default)
    {
        var (kind, status, body, error) = await PostAsync(
            $"batches/{batchId}/send",
            payload: null,
            cancellationToken);

        if (kind == PrintPackShipCallKind.Unknown)
        {
            return new PrintPackShipBatchSendResult
            {
                Kind = PrintPackShipCallKind.Unknown,
                HttpStatus = status,
                RawJson = body,
                Error = error,
                Message = error,
            };
        }

        if (!TryParseEnvelope(body, out var root, out var parseError))
        {
            // Ambiguous after a chargeable call — treat as Unknown.
            return new PrintPackShipBatchSendResult
            {
                Kind = PrintPackShipCallKind.Unknown,
                HttpStatus = status,
                RawJson = body,
                Error = parseError,
                Message = parseError,
            };
        }

        var success = ReadBool(root, "success");
        var message = ReadString(root, "message");
        long? responseBatchId = null;
        string? remoteStatus = null;
        int? pagesTotal = null;
        int? pagesSuccess = null;
        int? pagesFailed = null;
        decimal? pricePerLabel = null;
        decimal? scanFee = null;
        int? paidPages = null;
        decimal? balancePrevious = null;
        decimal? balanceCurrent = null;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            responseBatchId = ReadLong(data, "batch_id");
            remoteStatus = ReadString(data, "status");

            if (data.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Object)
            {
                pagesTotal = ReadInt(pages, "total");
                pagesSuccess = ReadInt(pages, "success");
                pagesFailed = ReadInt(pages, "failed");
            }

            if (data.TryGetProperty("pricing", out var pricing) && pricing.ValueKind == JsonValueKind.Object)
            {
                pricePerLabel = ReadDecimal(pricing, "price_per_label");
                scanFee = ReadDecimal(pricing, "scan_fee");
                paidPages = ReadInt(pricing, "paid_pages");
            }

            if (data.TryGetProperty("balance", out var balance) && balance.ValueKind == JsonValueKind.Object)
            {
                balancePrevious = ReadDecimal(balance, "previous");
                balanceCurrent = ReadDecimal(balance, "current");
            }
        }

        if (responseBatchId.HasValue && responseBatchId.Value != batchId)
        {
            _logger.LogWarning(
                "PrintPackShip send response batch_id {ResponseId} differs from create id {CreateId}; keeping create id",
                responseBatchId.Value,
                batchId);
        }

        if (kind == PrintPackShipCallKind.Failed || !success)
        {
            _logger.LogWarning(
                "PrintPackShip send batch {BatchId} failed (HTTP {Status}): {Message}",
                batchId,
                status,
                message ?? error);

            return new PrintPackShipBatchSendResult
            {
                Kind = PrintPackShipCallKind.Failed,
                Success = false,
                Message = message,
                ResponseBatchId = responseBatchId,
                Status = remoteStatus,
                PagesTotal = pagesTotal,
                PagesSuccess = pagesSuccess,
                PagesFailed = pagesFailed,
                PricePerLabel = pricePerLabel,
                ScanFee = scanFee,
                PaidPages = paidPages,
                BalancePrevious = balancePrevious,
                BalanceCurrent = balanceCurrent,
                RawJson = body,
                HttpStatus = status,
                Error = message ?? error,
            };
        }

        return new PrintPackShipBatchSendResult
        {
            Kind = PrintPackShipCallKind.Ok,
            Success = true,
            Message = message,
            ResponseBatchId = responseBatchId,
            Status = remoteStatus,
            PagesTotal = pagesTotal,
            PagesSuccess = pagesSuccess,
            PagesFailed = pagesFailed,
            PricePerLabel = pricePerLabel,
            ScanFee = scanFee,
            PaidPages = paidPages,
            BalancePrevious = balancePrevious,
            BalanceCurrent = balanceCurrent,
            RawJson = body,
            HttpStatus = status,
        };
    }

    private async Task<(PrintPackShipCallKind Kind, int? Status, string? Body, string? Error)> PostAsync(
        string relativeUrl,
        object? payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.BearerToken))
        {
            return (PrintPackShipCallKind.Failed, null, null, "PrintPackShip BearerToken chưa cấu hình.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _settings.BearerToken.Trim());

            if (payload is not null)
            {
                var json = JsonSerializer.Serialize(payload);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return (PrintPackShipCallKind.Ok, code, body, null);
            }

            if (code >= 400 && code < 500)
            {
                return (PrintPackShipCallKind.Failed, code, body, $"HTTP {code}");
            }

            // 5xx / other — may have charged on send.
            _logger.LogWarning(
                "PrintPackShip {Url} returned HTTP {Status}",
                relativeUrl,
                code);
            return (PrintPackShipCallKind.Unknown, code, body, $"HTTP {code}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout — ambiguous for send.
            _logger.LogWarning("PrintPackShip {Url} timed out", relativeUrl);
            return (PrintPackShipCallKind.Unknown, null, null, "Timeout khi gọi PrintPackShip.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("PrintPackShip {Url} canceled/timed out", relativeUrl);
            return (PrintPackShipCallKind.Unknown, null, null, "Timeout khi gọi PrintPackShip.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "PrintPackShip {Url} transport error", relativeUrl);
            return (PrintPackShipCallKind.Unknown, null, null, ex.Message);
        }
    }

    private static bool TryParseEnvelope(string? body, out JsonElement root, out string? error)
    {
        root = default;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "PrintPackShip trả về body trống.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            error = "PrintPackShip trả về JSON không hợp lệ.";
            return false;
        }
    }

    private static bool ReadBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
        && p.ValueKind is JsonValueKind.True or JsonValueKind.False
        && p.GetBoolean();

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static long? ReadLong(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String
            && long.TryParse(p.GetString(), out var s))
        {
            return s;
        }

        return null;
    }

    private static int? ReadInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String
            && int.TryParse(p.GetString(), out var s))
        {
            return s;
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String
            && decimal.TryParse(p.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var s))
        {
            return s;
        }

        return null;
    }
}
