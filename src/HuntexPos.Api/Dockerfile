# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY HuntexPos.Api.csproj .
RUN dotnet restore HuntexPos.Api.csproj
COPY . .
RUN dotnet publish HuntexPos.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
# SQLite + PDFs. Official image runs as non-root user app (APP_UID, usually 1654).
# Bind-mounting /opt/mctactical:/app/data: chown that host dir to the same UID so the process can write.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data/pdfs \
    && chown -R $APP_UID:$APP_UID /app/data \
    && chmod -R u=rwx,g=rx,o= /app/data
COPY --chown=$APP_UID:$APP_UID --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=5 \
  CMD curl -fsS http://127.0.0.1:8080/api/health || exit 1
ENTRYPOINT ["dotnet", "HuntexPos.Api.dll"]
