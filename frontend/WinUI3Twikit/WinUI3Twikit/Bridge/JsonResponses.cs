using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinUI3Twikit.Bridge
{
    /// <summary>FastAPI と同じ形の JSON レスポンスを組み立てる。</summary>
    internal static class JsonResponses
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static HttpResponseMessage Json(HttpStatusCode status, JsonNode? body)
        {
            var text = body is null ? "null" : body.ToJsonString(Options);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(text, Encoding.UTF8, "application/json"),
            };
        }

        public static HttpResponseMessage Ok(JsonNode? body) => Json(HttpStatusCode.OK, body);

        /// <summary>FastAPI の <c>HTTPException</c> と同じ <c>{"detail": ...}</c>。</summary>
        public static HttpResponseMessage Error(HttpStatusCode status, string detail)
            => Json(status, new JsonObject { ["detail"] = detail });

        public static HttpResponseMessage File(byte[] bytes, string contentType)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

    /// <summary>クエリ文字列の最小限のパーサー（System.Web に依存しない）。</summary>
    internal static class QueryString
    {
        public static Dictionary<string, string> Parse(string? query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(query))
            {
                return result;
            }

            var text = query.StartsWith('?') ? query[1..] : query;
            foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var index = pair.IndexOf('=');
                var key = index < 0 ? pair : pair[..index];
                var value = index < 0 ? string.Empty : pair[(index + 1)..];
                result[Decode(key)] = Decode(value);
            }

            return result;
        }

        private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

        public static string? GetString(this Dictionary<string, string> query, string key)
            => query.TryGetValue(key, out var raw) && !string.IsNullOrEmpty(raw) ? raw : null;

        public static int GetInt(this Dictionary<string, string> query, string key, int fallback)
            => GetNullableInt(query, key) ?? fallback;

        public static int? GetNullableInt(this Dictionary<string, string> query, string key)
            => query.TryGetValue(key, out var raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        public static bool GetBool(this Dictionary<string, string> query, string key, bool fallback)
        {
            if (!query.TryGetValue(key, out var raw))
            {
                return fallback;
            }

            return raw.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" or "t" or "y" => true,
                "false" or "0" or "no" or "off" or "f" or "n" => false,
                _ => fallback,
            };
        }
    }
}
