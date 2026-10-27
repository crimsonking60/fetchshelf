# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish src/Fetchshelf.Web/Fetchshelf.Web.csproj -c Release -o /app

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

ENTRYPOINT ["docker-entrypoint.sh"]
