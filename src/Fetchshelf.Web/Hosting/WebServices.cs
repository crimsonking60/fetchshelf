using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Opds;
using Fetchshelf.Data.Statistics;
using Fetchshelf.Web.Catalog;
using Fetchshelf.Web.Metrics;

namespace Fetchshelf.Web.Hosting;

/// <summary>What the web interface itself needs: Razor Pages and its routes, the health check, the Account page's summary, and the book catalog and cover caches.</summary>
public static class WebServices
{
    /// <summary>The most values a posted form may have: enough for every row of a large library ticked at once.</summary>
    public const int MaxFormValues = 100_000;

    public static IServiceCollection AddFetchshelfWeb(this IServiceCollection services, FetchshelfSettings settings, FetchshelfPaths paths)
    {
        // The catalog and its covers serve both the OPDS feeds and the Books page, so they are there without `[opds]` too.
        services.AddSingleton<BookCatalogCache>();
        // Covers read out of the books live next to the database, like the form keys, so they survive a new container.
        services.AddSingleton(sp => new OpdsCoverCache(
            Path.Combine(paths.DataFolder, "covers"), sp.GetRequiredService<ILogger<OpdsCoverCache>>()));

        // A ticked box is one form value, and Select all on the Library check, Errors, Torrents or Following page can tick
        // thousands: ASP.NET Core's default of 1,024 values answered 400 Bad Request. The size limit still caps a form.
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options => options.ValueCountLimit = MaxFormValues);

        services.AddScoped<Pages.Account.AccountSummaryBuilder>();
        services.AddScoped<LibraryStatistics>();
        services.AddMemoryCache();
        if (settings.Metrics)
        {
            services.AddScoped<MetricsCollector>();
        }

        services.AddRazorPages(options =>
        {
            options.Conventions.AddPageRoute("/Torrents/Detail", "torrents/{id}");
            options.Conventions.AddPageRoute("/Torrents/Edit", "torrents/{id}/edit");
            options.Conventions.AddPageRoute("/Search/Detail", "search/{mamId:long}");
            options.Conventions.AddPageRoute("/Books/Detail", "books/{id}");
            options.Conventions.AddPageRoute("/Following/Detail", "following/{id:int}");
            options.Conventions.AddPageRoute("/Following/ToLookAt", "following/to-look-at");
        });
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return services;
    }
}
