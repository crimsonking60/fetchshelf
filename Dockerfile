# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

# A release version (YYYY.MM.N) becomes the assembly version. Anything else, such as develop-<commit>, is not a
# valid assembly version, so it only sets the informational version. The argument must not be called VERSION:
# build arguments are environment variables here, and MSBuild would read that as its own Version property.
ARG FETCHSHELF_VERSION=
RUN case "$FETCHSHELF_VERSION" in \
        "") set -- ;; \
        [0-9]*) set -- "-p:Version=$FETCHSHELF_VERSION" ;; \
        *) set -- "-p:InformationalVersion=$FETCHSHELF_VERSION" ;; \
    esac \
    && dotnet publish src/Fetchshelf.Web/Fetchshelf.Web.csproj -c Release -o /app "$@"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV FETCHSHELF_CONFIG_FILE=/config/config.toml
ENV FETCHSHELF_DATA_FILE=/data/data.db
ENV FETCHSHELF_LOG_DIR=""
ENV PUID=1000
ENV PGID=1000

RUN mkdir -p /config /data
COPY --from=build /app .
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

EXPOSE 3157
VOLUME ["/config", "/data"]

# Asks the running instance's /health endpoint, on the port configured by web_port.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD ["dotnet", "/app/Fetchshelf.Web.dll", "--healthcheck"]

ENTRYPOINT ["docker-entrypoint.sh"]
