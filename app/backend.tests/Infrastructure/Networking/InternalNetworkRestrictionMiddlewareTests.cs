using System.Net;
using LoginApp.Api.Infrastructure.Networking;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Tests.Infrastructure.Networking;

/// <summary>
/// Unit tests for <see cref="InternalNetworkRestrictionMiddleware"/>.
/// </summary>
public sealed class InternalNetworkRestrictionMiddlewareTests
{
    private static readonly NetworkOptions s_options = new()
    {
        InternalNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128"],
    };

    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.4")] // IPv4 peer as seen on Kestrel's dual-stack socket
    public async Task InvokeAsync_FromInternalAddress_PassesRequestOn(string address)
    {
        // Arrange
        bool nextCalled = false;
        InternalNetworkRestrictionMiddleware middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        DefaultHttpContext context = CreateContext(IPAddress.Parse(address));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")] // just outside 172.16.0.0/12
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:8.8.8.8")]
    public async Task InvokeAsync_FromExternalAddress_RespondsForbiddenWithoutCallingNext(string address)
    {
        // Arrange
        bool nextCalled = false;
        InternalNetworkRestrictionMiddleware middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        DefaultHttpContext context = CreateContext(IPAddress.Parse(address));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WithoutPeerAddress_FailsClosed()
    {
        // Arrange
        bool nextCalled = false;
        InternalNetworkRestrictionMiddleware middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        DefaultHttpContext context = CreateContext(remoteAddress: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private static InternalNetworkRestrictionMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, Options.Create(s_options), NullLogger<InternalNetworkRestrictionMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(IPAddress? remoteAddress)
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = remoteAddress;
        return context;
    }
}
