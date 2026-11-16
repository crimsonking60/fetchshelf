using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Hosting;

/// <summary>The database, and the files that live next to it: the form keys and the instance id.</summary>
public static class DataServices
{
    public static IServiceCollection AddFetchshelfData(this IServiceCollection services, FetchshelfPaths paths)
    {
        // Keep the keys that encrypt antiforgery cookies next to the database: the default location is inside the
        // container, so a new container (every image update) would otherwise reject the cookie browsers still hold.
        services.AddDataProtection()
            .SetApplicationName("Fetchshelf")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.DataFolder, "keys")));

        // The two cookies those keys encrypt are named after this installation, so a second Fetchshelf on the same host
        // (browsers share cookies across ports) can't overwrite them and fail every form with HTTP 400.
        var instanceId = InstanceId.LoadOrCreate(paths.DataFolder);
        services.AddAntiforgery(options => options.Cookie.Name = $".Fetchshelf.Antiforgery.{instanceId}");
        services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(
            options => options.Cookie.Name = $".Fetchshelf.TempData.{instanceId}");

        services.AddSingleton<IJobRunStore, JobRunStore>();

        // A single AddDbContextFactory registration backs both the scoped FetchshelfDbContext (for
        // EventStore/LinkRunner/Cleaner) and the short-lived contexts DatabaseHealthCheck creates itself -
        // registering AddDbContext separately as well would conflict over DbContextOptions' lifetime.
        services.AddDbContextFactory<FetchshelfDbContext>(options => options.UseFetchshelfSqlite(paths.DataFile));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FetchshelfDbContext>>().CreateDbContext());
        services.AddScoped<EventStore>();
        services.AddScoped<LibraryQueries>();
        return services;
    }
}
