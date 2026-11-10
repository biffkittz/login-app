using System.Globalization;
using LoginApp.Api.Features.GroceryList.CostEstimates;
using LoginApp.Api.Features.GroceryList.Images;
using LoginApp.Api.Tests.Features.GroceryList.CostEstimates;
using LoginApp.Api.Tests.Features.GroceryList.Images;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// A <see cref="LoginApiFactory"/> that can estimate the list's cost: it has a Gemini API key, and
/// <see cref="Estimator"/> stands in for Gemini, so no test calls the real (paid) API.
/// </summary>
public class CostEstimateApiFactory : LoginApiFactory
{
    /// <summary>Gets the stand-in for Gemini.</summary>
    internal FakeCostEstimator Estimator { get; } = new();

    /// <summary>Gets the day's allowance of estimates (<c>CostEstimates:MaxEstimatesPerDay</c>).</summary>
    protected virtual int MaxEstimatesPerDay => 500;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ImageGeneration:ApiKey", "test-api-key");
        builder.UseSetting("CostEstimates:MaxEstimatesPerDay", MaxEstimatesPerDay.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICostEstimator>();
            services.AddSingleton<ICostEstimator>(Estimator);

            // The key turns AI pictures on as well; a stand-in keeps those from calling Gemini too.
            services.RemoveAll<IGroceryImageGenerator>();
            services.AddSingleton<IGroceryImageGenerator>(new FakeImageGenerator());
        });
    }
}

/// <summary>A <see cref="CostEstimateApiFactory"/> that allows only two estimates a day.</summary>
public sealed class TwoEstimatesADayApiFactory : CostEstimateApiFactory
{
    /// <inheritdoc />
    protected override int MaxEstimatesPerDay => 2;
}
