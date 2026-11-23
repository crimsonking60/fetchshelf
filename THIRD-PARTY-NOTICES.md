# Third-party notices

Fetchshelf itself is released under the [MIT License](LICENSE). The application, and every Docker image built from
this repository, also contains the software below, each under its own license. All of them are permissive licenses:
none needs a license key, a paid license or registration to build, run, change or redistribute Fetchshelf, for any
user, commercial or not. A fork that redistributes Fetchshelf, as source or as an image, must keep this file and
the licenses it points to, and must keep the `NOTICE` files of the Apache-2.0 packages that have one (they are in
the packages themselves).

This file lists what ships with the application, not what is only used to build or test it (such as
`Microsoft.EntityFrameworkCore.Design`, xUnit and the test SDK). `ThirdPartyNoticesTests` fails when a package
referenced by a project under `src/` is missing below.

## NuGet packages

Referenced by the projects under `src/`, with the packages they bring in.

| Package | Version | License | Used for |
|---|---|---|---|
| CoreJ2K | 2.4.1.108 | BSD-3-Clause, and the JJ2000 copyright notice (see below) | Decoding JPEG 2000 pictures in PDFs, for their covers |
| FuzzySharp | 2.0.2 | MIT | Matching titles and names loosely |
| Microsoft.EntityFrameworkCore.Sqlite (with Microsoft.EntityFrameworkCore, .Relational, .Abstractions, .Sqlite.Core and Microsoft.Data.Sqlite.Core) | 10.0.15 | MIT | The database |
| Microsoft.Extensions.Http, Microsoft.Extensions.Logging.Abstractions (with the other Microsoft.Extensions packages they bring in) | 11.0.0 | MIT | HTTP clients and logging |
| PdfPig | 0.1.16 | Apache-2.0 | Reading a PDF's cover |
| Serilog.AspNetCore, Serilog.Sinks.Async, Serilog.Sinks.Console, Serilog.Sinks.File (with Serilog and the other Serilog packages they bring in) | 10.0.0, 2.1.0, 6.1.1, 7.0.0 | Apache-2.0 | Logging |
| SharpCompress | 0.50.11 | MIT | Reading CBR comics |
| SkiaSharp, SkiaSharp.NativeAssets.Linux.NoDependencies (with SkiaSharp.NativeAssets.Win32 and .macOS) | 4.155.0 | MIT | Joining a PDF cover cut into strips |
| SQLitePCLRaw.bundle_e_sqlite3 (with SQLitePCLRaw.core, .lib.e_sqlite3 and .provider.e_sqlite3) | 2.1.13 | Apache-2.0 | The database |
| Tomlyn | 2.11.1 | BSD-2-Clause | Reading `config.toml` |
| YamlDotNet | 18.1.0 | MIT | Reading `config.toml` |

Native code inside these packages:

- **SkiaSharp**'s native library (`libSkiaSharp`) is built from Google's [Skia](https://skia.org) (BSD-3-Clause)
  and the libraries Skia builds in, such as libjpeg-turbo, libpng, libwebp, giflib, zlib, FreeType, Expat and the
  Adobe DNG SDK, each under its own license; all allow free use and redistribution with their notices kept. Their
  notices are in `THIRD-PARTY-NOTICES.txt` in the `SkiaSharp.NativeAssets.Linux.NoDependencies` package, which every
  image carries as `/app/THIRD-PARTY-NOTICES-SkiaSharp.txt`.
- **CoreJ2K** is based on JJ2000, the JPEG 2000 reference implementation, and carries its copyright notice
  (`COPYRIGHT-JJ2000-5.1` in the package, `/app/COPYRIGHT-JJ2000.txt` in every image). It must be included in every
  copy, grants use only in products that conform to the JPEG 2000 standard, which a JPEG 2000 decoder does, and
  warns that using it may infringe patents: the core JPEG 2000 standard (Part 1), the only part a decoder of PDF
  pictures needs, was published with its patent holders' agreement to license it royalty-free. CoreJ2K's own
  license is in `/app/LICENSE-CoreJ2K.txt`.
- **SQLitePCLRaw.lib.e_sqlite3** contains [SQLite](https://sqlite.org/copyright.html), which is in the public domain.

## Files in the web pages

| Component | Version | License | Where |
|---|---|---|---|
| [htmx](https://htmx.org) | 2.0.11 | 0BSD | `src/Fetchshelf.Web/wwwroot/lib/htmx/`, with its `LICENSE` |
| [IBM Plex Sans and Plex Mono](https://github.com/IBM/plex) | | SIL Open Font License 1.1 | `src/Fetchshelf.Web/wwwroot/lib/fonts/`, with `LICENSE-IBM-Plex-OFL.txt` |

## Docker images

The images are built on Microsoft's [`mcr.microsoft.com/dotnet/aspnet`](https://hub.docker.com/r/microsoft/dotnet-aspnet)
image: the .NET runtime (MIT) on Debian, whose packages are under their own licenses, listed in each package's
`/usr/share/doc/*/copyright` inside the image. This file, Fetchshelf's `LICENSE`, SkiaSharp's notices and CoreJ2K's
license and JJ2000 notice are in `/app` in every image.

## Choosing a new dependency

A dependency is added only when its license is permissive (MIT, Apache-2.0, BSD, 0BSD, ISC, zlib, OFL for fonts or
similar), lets anyone build and use Fetchshelf without a key, account or fee, and is added to this file in the same
pull request. One that fails this is not used, however useful: for example **SixLabors.ImageSharp** was considered
for joining PDF covers and left out, as its Six Labors Split License is Apache-2.0 only for some users (open source,
non-profits, companies under 1M USD revenue) and, from version 3.2 on, its package refuses to make a Release build
without a Six Labors license key.
