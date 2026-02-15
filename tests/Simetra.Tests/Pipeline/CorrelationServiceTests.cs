using System.Net;
using FluentAssertions;
using Lextm.SharpSnmpLib;
using Simetra.Pipeline;
using Simetra.Pipeline.Middleware;

namespace Simetra.Tests.Pipeline;

public class CorrelationServiceTests
{
    // -------------------------------------------------------
    // RotatingCorrelationService
    // -------------------------------------------------------

    [Fact]
    public void CurrentCorrelationId_InitiallyEmpty()
    {
        var service = new RotatingCorrelationService();

        service.CurrentCorrelationId.Should().Be(string.Empty);
    }

    [Fact]
    public void SetCorrelationId_UpdatesCurrentValue()
    {
        var service = new RotatingCorrelationService();

        service.SetCorrelationId("abc-123");

        service.CurrentCorrelationId.Should().Be("abc-123");
    }

    [Fact]
    public void SetCorrelationId_OverwritesPreviousValue()
    {
        var service = new RotatingCorrelationService();

        service.SetCorrelationId("first");
        service.SetCorrelationId("second");

        service.CurrentCorrelationId.Should().Be("second");
    }

    // -------------------------------------------------------
    // CorrelationIdMiddleware
    // -------------------------------------------------------

    private static TrapContext MakeContext()
    {
        var envelope = new TrapEnvelope
        {
            Varbinds = new List<Variable>(),
            SenderAddress = IPAddress.Loopback,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        return new TrapContext { Envelope = envelope };
    }

    [Fact]
    public async Task InvokeAsync_StampsCorrelationIdOnEnvelope()
    {
        var correlationService = new RotatingCorrelationService();
        correlationService.SetCorrelationId("corr-456");
        var middleware = new CorrelationIdMiddleware(correlationService);
        var context = MakeContext();

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Envelope.CorrelationId.Should().Be("corr-456");
    }

    [Fact]
    public async Task InvokeAsync_CallsNextDelegate()
    {
        var correlationService = new RotatingCorrelationService();
        correlationService.SetCorrelationId("corr-789");
        var middleware = new CorrelationIdMiddleware(correlationService);
        var context = MakeContext();
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
    }
}
