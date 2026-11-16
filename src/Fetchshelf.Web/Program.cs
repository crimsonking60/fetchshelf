using Fetchshelf.Web.Hosting;

return await FetchshelfApp.RunAsync(args);

// Exposed so Fetchshelf.Web.Tests can reference WebApplicationFactory<Program>.
public partial class Program;
