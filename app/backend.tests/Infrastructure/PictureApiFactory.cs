using LoginApp.Api.Features.GroceryList.Images;
using LoginApp.Api.Tests.Features.GroceryList.Images;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// A <see cref="LoginApiFactory"/> that can draw item pictures: it has an API key, and <see cref="Generator"/>
/// stands in for Gemini, so no test calls the real (paid) API.
/// </summary>
public sealed class PictureApiFactory : LoginApiFactory
{
    /// <summary>Gets the stand-in for Gemini.</summary>
    internal FakeImageGenerator Generator { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ImageGeneration:ApiKey", "test-api-key");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGroceryImageGenerator>();
            services.AddSingleton<IGroceryImageGenerator>(Generator);
        });
    }
}
