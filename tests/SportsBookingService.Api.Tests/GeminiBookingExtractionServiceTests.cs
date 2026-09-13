using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

public class GeminiBookingExtractionServiceTests
{
    private const string ApiKey = "test-api-key";

    // Stubs the transport rather than the service, so the real request building, response
    // unwrapping, and JSON parsing all run — no network and no API key needed.
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Candidate(string text)
    {
        var payload = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text } } } } },
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }

    private static (GeminiBookingExtractionService Service, StubHandler Handler) NewService(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        params (string Key, string Value)[] extraConfig)
    {
        var handler = new StubHandler(respond);
        var configuration = TestServices.ConfigurationWith([("Gemini:ApiKey", ApiKey), .. extraConfig]);
        var service = new GeminiBookingExtractionService(
            new HttpClient(handler),
            configuration,
            NullLogger<GeminiBookingExtractionService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task Full_extraction_scores_full_confidence()
    {
        var (service, _) = NewService(_ => Candidate("""
            {"venueName":"Melbourne Sports Centre","date":"2026-10-02","startTime":"18:00","endTime":"20:00","cost":48.50,"currency":"AUD","bookedByName":"Alice"}
            """));

        var result = await service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed.");

        Assert.Equal("Melbourne Sports Centre", result.VenueName);
        Assert.Equal(new DateOnly(2026, 10, 2), result.Date);
        Assert.Equal("18:00", result.StartTime);
        Assert.Equal("20:00", result.EndTime);
        Assert.Equal(48.50m, result.Cost);
        Assert.Equal("AUD", result.Currency);
        Assert.Equal("Alice", result.BookedByName);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public async Task Partial_extraction_scores_lower_confidence()
    {
        var (service, _) = NewService(_ => Candidate("""
            {"venueName":"Melbourne Sports Centre","date":"2026-10-02","startTime":null,"endTime":null,"cost":null,"currency":null}
            """));

        var result = await service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed.");

        Assert.Equal("Melbourne Sports Centre", result.VenueName);
        Assert.Null(result.Cost);
        Assert.True(result.Confidence < 1.0);
    }

    [Fact]
    public async Task A_missing_booker_name_alone_drops_confidence_below_full()
    {
        var (service, _) = NewService(_ => Candidate("""
            {"venueName":"Melbourne Sports Centre","date":"2026-10-02","startTime":"18:00","endTime":"20:00","cost":48.50,"currency":"AUD","bookedByName":null}
            """));

        var result = await service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed.");

        Assert.Null(result.BookedByName);
        Assert.True(result.Confidence < 1.0);
    }

    [Fact]
    public async Task Unwraps_a_markdown_fenced_response()
    {
        var (service, _) = NewService(_ => Candidate("""
            ```json
            {"venueName":"Melbourne Sports Centre","date":"2026-10-02","startTime":"18:00","endTime":"20:00","cost":48.50,"currency":"AUD","bookedByName":"Alice"}
            ```
            """));

        var result = await service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed.");

        Assert.Equal("Melbourne Sports Centre", result.VenueName);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public async Task An_unparseable_date_is_treated_as_missing_rather_than_failing()
    {
        var (service, _) = NewService(_ => Candidate("""
            {"venueName":"Melbourne Sports Centre","date":"next Tuesday","startTime":"18:00","endTime":"20:00","cost":48.50,"currency":"AUD"}
            """));

        var result = await service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed.");

        Assert.Null(result.Date);
        Assert.Equal("Melbourne Sports Centre", result.VenueName);
        Assert.True(result.Confidence < 1.0);
    }

    [Fact]
    public async Task Sends_the_api_key_as_a_header_and_never_in_the_url()
    {
        var (service, handler) = NewService(_ => Candidate("""
            {"venueName":"Melbourne Sports Centre","date":null,"startTime":null,"endTime":null,"cost":null,"currency":null}
            """));

        await service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed.");

        Assert.Equal(ApiKey, Assert.Single(handler.LastRequest!.Headers.GetValues("x-goog-api-key")));
        Assert.DoesNotContain(ApiKey, handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task Uses_the_configured_model()
    {
        var (service, handler) = NewService(
            _ => Candidate("""{"venueName":"X","date":null,"startTime":null,"endTime":null,"cost":null,"currency":null}"""),
            ("Gemini:Model", "gemini-9-experimental"));

        await service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed.");

        Assert.Contains("gemini-9-experimental", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Throws_when_the_api_key_is_not_configured()
    {
        var service = new GeminiBookingExtractionService(
            new HttpClient(new StubHandler(_ => Candidate("{}"))),
            TestServices.EmptyConfiguration,
            NullLogger<GeminiBookingExtractionService>.Instance);

        await Assert.ThrowsAsync<BookingExtractionException>(
            () => service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed."));
    }

    [Fact]
    public async Task Throws_on_a_non_success_status()
    {
        var (service, _) = NewService(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<BookingExtractionException>(
            () => service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed."));
    }

    [Fact]
    public async Task Throws_when_the_transport_fails()
    {
        var (service, _) = NewService(_ => throw new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<BookingExtractionException>(
            () => service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed."));
    }

    [Fact]
    public async Task Throws_when_the_request_times_out()
    {
        // What HttpClient surfaces when its own Timeout elapses.
        var (service, _) = NewService(_ => throw new TaskCanceledException("The request timed out."));

        await Assert.ThrowsAsync<BookingExtractionException>(
            () => service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed."));
    }

    [Fact]
    public async Task Throws_when_the_envelope_is_not_the_expected_shape()
    {
        var (service, _) = NewService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"promptFeedback":{"blockReason":"SAFETY"}}""", Encoding.UTF8, "application/json"),
        });

        await Assert.ThrowsAsync<BookingExtractionException>(
            () => service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed."));
    }

    [Fact]
    public async Task Throws_when_the_candidate_text_is_not_json()
    {
        var (service, _) = NewService(_ => Candidate("I could not find any booking details."));

        await Assert.ThrowsAsync<BookingExtractionException>(
            () => service.ExtractAsync("noreply@yepbooking.com", "Booking confirmed."));
    }

    [Fact]
    public async Task Throws_when_every_field_comes_back_empty()
    {
        var (service, _) = NewService(_ => Candidate("""
            {"venueName":null,"date":null,"startTime":null,"endTime":null,"cost":null,"currency":null}
            """));

        await Assert.ThrowsAsync<BookingExtractionException>(
            () => service.ExtractAsync("noreply@yepbooking.com", "Not a booking email at all."));
    }
}
