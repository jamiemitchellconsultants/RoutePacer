using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Server.Handoffs;

public static partial class HandoffEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    public static IEndpointRouteBuilder MapHandoffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/handoffs", async (HttpContext context, HandoffUploadService service, UploadCredentialVerifier verifier, IOptions<HandoffRelayOptions> options, CancellationToken cancellationToken) =>
        {
            if (!options.Value.UploadsEnabled) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!verifier.IsValid(context.Request.Headers.Authorization.ToString())) return Results.Unauthorized();
            if (!string.Equals(context.Request.ContentType, "application/gpx+xml", StringComparison.Ordinal)) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            if (context.Request.ContentLength is > 52_428_800) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            try
            {
                var content = await LimitedRequestBodyReader.ReadAsync(context.Request.Body, 52_428_800, cancellationToken);
                var response = await service.CreateAsync(content, cancellationToken);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Json(response, statusCode: StatusCodes.Status201Created);
            }
            catch (PayloadTooLargeException) { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }
            catch (InvalidDataException) { return Results.BadRequest(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return Results.StatusCode(StatusCodes.Status500InternalServerError); }
        }).RequireRateLimiting("handoff-upload");

        endpoints.MapGet("/api/handoffs/{token}", async (string token, HttpResponse response, IHandoffStore store, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            if (!TokenPattern().IsMatch(token)) return NotFound(response);
            byte[] hash;
            try { hash = HandoffToken.Hash(token); } catch (FormatException) { return NotFound(response); }
            var content = await store.ConsumeAsync(hash, clock.GetUtcNow(), cancellationToken);
            if (content is null) return NotFound(response);
            response.Headers.CacheControl = "no-store";
            response.Headers.Pragma = "no-cache";
            response.Headers.XContentTypeOptions = "nosniff";
            return Results.Bytes(content, "application/gpx+xml", enableRangeProcessing: false);
        });
        return endpoints;
    }

    private static IResult NotFound(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status404NotFound;
        response.ContentType = "application/gpx+xml";
        response.ContentLength = 0;
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers.XContentTypeOptions = "nosniff";
        return Results.Empty;
    }
}
