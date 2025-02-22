using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Pororoca.Domain.Features.Common;
using static Pororoca.Domain.Features.Common.JsonConfiguration;

namespace Pororoca.Domain.Features.Entities.Pororoca.Http;

public sealed class PororocaHttpResponse
{
    public TimeSpan ElapsedTime { get; }

    public DateTimeOffset ReceivedAt { get; }

    public Exception? Exception { get; }

    public bool Successful { get; }

    public HttpStatusCode? StatusCode { get; }

    public IEnumerable<KeyValuePair<string, string>>? Headers { get; }

    public IEnumerable<KeyValuePair<string, string>>? Trailers { get; }

    private readonly byte[]? binaryBody;

    public bool WasCancelled =>
        Exception is TaskCanceledException;

    public bool FailedDueToTlsVerification =>
        Exception?.InnerException is AuthenticationException aex
        && aex.Message.Contains("remote certificate is invalid", StringComparison.InvariantCultureIgnoreCase);

    public bool HasBody =>
        this.binaryBody?.Length > 0;

    public string? ContentType
    {
        get
        {
            var contentTypeHeaders = Headers?.FirstOrDefault(h => h.Key == "Content-Type");
            return contentTypeHeaders?.Value;
        }
    }

    public bool CanDisplayTextBody
    {
        get
        {
            string? contentType = ContentType;
            // Optimistic behavior, considering that if content type is not present, then probably is text
            return contentType == null || MimeTypesDetector.IsTextContent(contentType);
        }
    }

    public string? GetBodyAsText(string? nonUtf8BodyMessageToShow)
    {
        if (this.binaryBody == null || this.binaryBody.Length == 0)
        {
            return null;
        }
        else
        {
            try
            {
                string bodyStr = Encoding.UTF8.GetString(this.binaryBody);
                string? contentType = ContentType;
                if (contentType == null || !MimeTypesDetector.IsJsonContent(contentType))
                {
                    return bodyStr;
                }
                else
                {
                    try
                    {
                        dynamic? jsonObj = JsonSerializer.Deserialize<dynamic>(bodyStr);
                        string prettyPrintJson = JsonSerializer.Serialize(jsonObj, options: ViewJsonResponseOptions);
                        return prettyPrintJson;
                    }
                    catch
                    {
                        return bodyStr;
                    }
                }
            }
            catch
            {
                if (nonUtf8BodyMessageToShow is not null)
                {
                    try
                    {
                        return string.Format(nonUtf8BodyMessageToShow, GetBodyAsBinary()!.Length);
                    }
                    catch
                    {
                        return nonUtf8BodyMessageToShow;
                    }
                }
                else
                {
                    return null;
                }
            }
        }
    }

    public byte[]? GetBodyAsBinary() =>
        this.binaryBody;

    public T? GetJsonBodyAs<T>() =>
        GetJsonBodyAs<T>(MinifyingOptions);

    public T? GetJsonBodyAs<T>(JsonSerializerOptions jsonOptions) =>
        JsonSerializer.Deserialize<T>(this.binaryBody, jsonOptions);

    public string? GetContentDispositionFileName()
    {
        var contentDispositionHeader = Headers?.FirstOrDefault(h => h.Key == "Content-Disposition");
        string? contentDispositionValue = contentDispositionHeader?.Value;
        if (contentDispositionValue != null)
        {
            string[] contentDispositionParts = contentDispositionValue.Split("; ", StringSplitOptions.RemoveEmptyEntries);
            string? fileNamePart = contentDispositionParts.FirstOrDefault(p => p.StartsWith("filename"));
            if (fileNamePart != null)
            {
                string[] fileNamePartKv = fileNamePart.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (fileNamePartKv.Length == 2)
                {
                    return fileNamePartKv[1].Replace("\"", string.Empty);
                }
            }
        }
        return null;
    }

    public static async Task<PororocaHttpResponse> SuccessfulAsync(TimeSpan elapsedTime, HttpResponseMessage responseMessage)
    {
        byte[] binaryBody = await responseMessage.Content.ReadAsByteArrayAsync();
        return new(elapsedTime, responseMessage, binaryBody);
    }

    public static PororocaHttpResponse Failed(TimeSpan elapsedTime, Exception ex) =>
        new(elapsedTime, ex);

    private PororocaHttpResponse(TimeSpan elapsedTime, HttpResponseMessage responseMessage, byte[] binaryBody)
    {
        static KeyValuePair<string, string> ConvertHeaderToKeyValuePair(KeyValuePair<string, IEnumerable<string>> header) =>
            new(header.Key, string.Join(';', header.Value));

        ElapsedTime = elapsedTime;
        ReceivedAt = DateTimeOffset.Now;
        Successful = true;
        StatusCode = responseMessage.StatusCode;

        HttpHeaders nonContentHeaders = responseMessage.Headers;
        HttpHeaders contentHeaders = responseMessage.Content.Headers;
        HttpHeaders trailingHeaders = responseMessage.TrailingHeaders;

        Headers = nonContentHeaders.Concat(contentHeaders).Select(ConvertHeaderToKeyValuePair);
        Trailers = trailingHeaders.Select(ConvertHeaderToKeyValuePair);

        this.binaryBody = binaryBody;
    }

    private PororocaHttpResponse(TimeSpan elapsedTime, Exception exception)
    {
        ElapsedTime = elapsedTime;
        Successful = false;
        Exception = exception;
    }
}