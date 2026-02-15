using System.Net;
using FluentAssertions;
using Lextm.SharpSnmpLib;
using Simetra.Pipeline;

namespace Simetra.Tests.Pipeline;

public class TrapPipelineBuilderTests
{
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
    public async Task Build_WithNoMiddleware_ReturnsNoOpDelegate()
    {
        var builder = new TrapPipelineBuilder();
        var pipeline = builder.Build();
        var context = MakeContext();

        var act = () => pipeline(context);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Build_MiddlewareExecutesInRegistrationOrder()
    {
        var order = new List<int>();
        var builder = new TrapPipelineBuilder();

        builder.Use(next => async ctx => { order.Add(1); await next(ctx); });
        builder.Use(next => async ctx => { order.Add(2); await next(ctx); });
        builder.Use(next => async ctx => { order.Add(3); await next(ctx); });

        var pipeline = builder.Build();
        await pipeline(MakeContext());

        order.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Build_MiddlewareCanShortCircuit()
    {
        var order = new List<int>();
        var builder = new TrapPipelineBuilder();

        builder.Use(next => async ctx => { order.Add(1); await next(ctx); });
        // Short-circuit: does NOT call next
        builder.Use(_ => ctx => { order.Add(2); return Task.CompletedTask; });
        builder.Use(next => async ctx => { order.Add(3); await next(ctx); });

        var pipeline = builder.Build();
        await pipeline(MakeContext());

        order.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Use_ITrapMiddleware_Overload_Works()
    {
        var executed = false;
        var middleware = new TestMiddleware(() => executed = true);
        var builder = new TrapPipelineBuilder();

        builder.Use(middleware);

        var pipeline = builder.Build();
        await pipeline(MakeContext());

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task Build_TerminalDelegateIsNoOp()
    {
        var builder = new TrapPipelineBuilder();

        // Middleware that calls next (terminal) -- should not throw
        builder.Use(next => async ctx => { await next(ctx); });

        var pipeline = builder.Build();
        var act = () => pipeline(MakeContext());

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Simple ITrapMiddleware implementation for testing the interface overload.
    /// </summary>
    private sealed class TestMiddleware : ITrapMiddleware
    {
        private readonly Action _onExecute;

        public TestMiddleware(Action onExecute)
        {
            _onExecute = onExecute;
        }

        public async Task InvokeAsync(TrapContext context, TrapMiddlewareDelegate next)
        {
            _onExecute();
            await next(context);
        }
    }
}
