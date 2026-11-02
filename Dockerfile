# syntax=docker/dockerfile:1
# The build stage always runs on the build machine's own architecture and cross-targets the image's architecture
# with `dotnet publish -a`, so building images for other architectures needs no emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

# A release version (YYYY.MM.N) becomes the assembly version. Anything else, such as develop-<commit>, is not a
# valid assembly version, so it only sets the informational version. The argument must not be called VERSION:
# build arguments are environment variables here, and MSBuild would read that as its own Version property.
ARG FETCHSHELF_VERSION=
# Set by BuildKit: amd64, arm64 or arm, which `dotnet publish -a` understands as is.
ARG TARGETARCH
RUN case "$FETCHSHELF_VERSION" in \
        "") set -- ;; \
        [0-9]*) set -- "-p:Version=$FETCHSHELF_VERSION" ;; \
        *) set -- "-p:InformationalVersion=$FETCHSHELF_VERSION" ;; \
    esac \
    && dotnet publish src/Fetchshelf.Web/Fetchshelf.Web.csproj -c Release -a "$TARGETARCH" -o /app "$@" \
    && mkdir -p /rootfs/config /rootfs/database

# No RUN steps from here on: they would run as the target architecture, which needs emulation on other machines.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV FETCHSHELF_CONFIG_FILE=/config/config.toml
ENV FETCHSHELF_DATA_FILE=/database/data.db
ENV FETCHSHELF_LOG_DIR=""
ENV PUID=1000
ENV PGID=1000

COPY --from=build /rootfs/ /
COPY --from=build /app .
COPY --chmod=755 docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

EXPOSE 3157
VOLUME ["/config", "/database"]

# Asks the running instance's /health endpoint, on the port configured by web_port.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD ["dotnet", "/app/Fetchshelf.Web.dll", "--healthcheck"]

ENTRYPOINT ["docker-entrypoint.sh"]
