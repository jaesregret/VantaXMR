using System.Text.Json;

namespace Vanta.Network;

public static class StratumMessageParser
{
    public static bool TryParseShare(string message, out PoolShare? share)
    {
        share = null;
        if (!TryParseShareResult(message, out var result, out _))
        {
            return false;
        }

        if (result is not { Accepted: true })
        {
            return false;
        }

        share = new PoolShare
        {
            JobId = string.Empty,
            WorkerName = "vanta-worker",
            Nonce = string.Empty,
            HashHex = string.Empty
        };
        return true;
    }

    public static bool TryParseJob(string message, out PoolJob? job)
    {
        job = null;

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            JsonElement jobElement;

            if (root.TryGetProperty("method", out var method) &&
                method.ValueKind == JsonValueKind.String &&
                string.Equals(method.GetString(), "job", StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("params", out var parameters))
            {
                jobElement = parameters;
            }
            else if (root.TryGetProperty("result", out var result) &&
                     result.ValueKind == JsonValueKind.Object &&
                     result.TryGetProperty("job", out var resultJob))
            {
                jobElement = resultJob;
            }
            else
            {
                return false;
            }

            var blob = GetString(jobElement, "blob");
            var jobId = GetString(jobElement, "job_id");
            var target = GetString(jobElement, "target");
            var seedHash = GetString(jobElement, "seed_hash") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(blob) || string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(target) ||
                !IsHex(blob, minimumBytes: 43, maximumBytes: 408) ||
                !IsHex(target, minimumBytes: 4, maximumBytes: 8) ||
                !IsHex(seedHash, minimumBytes: 32, maximumBytes: 32))
            {
                return false;
            }

            job = new PoolJob
            {
                JobId = jobId,
                Blob = blob,
                Target = target,
                Difficulty = GetUInt64(jobElement, "difficulty") ?? 0,
                SeedHash = seedHash,
                Height = GetUInt64(jobElement, "height") ?? 0
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseShareResult(string message, out ShareResult? result, out int? responseId)
    {
        result = null;
        responseId = null;

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            responseId = GetInt(root, "id");
            if (!root.TryGetProperty("result", out var responseResult) &&
                !root.TryGetProperty("error", out _))
            {
                return false;
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            {
                result = new ShareResult
                {
                    Accepted = false,
                    Error = error.ToString()
                };
                return true;
            }

            result = new ShareResult
            {
                Accepted = responseResult.ValueKind == JsonValueKind.Object &&
                            (!responseResult.TryGetProperty("status", out var status) ||
                             (status.ValueKind == JsonValueKind.String &&
                              string.Equals(status.GetString(), "OK", StringComparison.OrdinalIgnoreCase)))
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsLoginSuccess(string message, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
            {
                error = errorElement.ToString();
                return false;
            }

            return root.TryGetProperty("result", out var result) &&
                   result.ValueKind == JsonValueKind.Object &&
                   result.TryGetProperty("status", out var status) &&
                   status.ValueKind == JsonValueKind.String &&
                   string.Equals(status.GetString(), "OK", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static int? GetResponseId(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            return GetInt(document.RootElement, "id");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static ulong? GetUInt64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.TryGetUInt64(out var parsed)
            ? parsed
            : null;

    private static bool IsHex(string value, int minimumBytes, int maximumBytes) =>
        value.Length % 2 == 0 && value.Length / 2 >= minimumBytes && value.Length / 2 <= maximumBytes &&
        value.All(Uri.IsHexDigit);

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Number &&
        element.TryGetInt32(out var parsed)
            ? parsed
            : element.ValueKind == JsonValueKind.Object &&
              element.TryGetProperty(name, out var value) &&
              value.TryGetInt32(out parsed)
                ? parsed
                : null;
}
