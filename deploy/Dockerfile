# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so dependency layers cache well.
COPY backend/WonderFleet.sln backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/WonderFleet.Domain/WonderFleet.Domain.csproj backend/src/WonderFleet.Domain/
COPY backend/src/WonderFleet.Application/WonderFleet.Application.csproj backend/src/WonderFleet.Application/
COPY backend/src/WonderFleet.Infrastructure/WonderFleet.Infrastructure.csproj backend/src/WonderFleet.Infrastructure/
COPY backend/src/WonderFleet.Api/WonderFleet.Api.csproj backend/src/WonderFleet.Api/
COPY backend/tests/WonderFleet.UnitTests/WonderFleet.UnitTests.csproj backend/tests/WonderFleet.UnitTests/
RUN dotnet restore backend/src/WonderFleet.Api/WonderFleet.Api.csproj

# The API also ships the hand-written OpenAPI documents, which live outside backend/.
COPY backend/ backend/
COPY docs/swagger/ docs/swagger/
RUN dotnet publish backend/src/WonderFleet.Api/WonderFleet.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# QuestPDF needs the native font stack for PDF reports; tzdata gives us Africa/Lagos.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 fontconfig fonts-dejavu-core tzdata curl \
 && rm -rf /var/lib/apt/lists/*

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Africa/Lagos

COPY --from=build /app/publish .

# Uploads live on the mounted disk (Render) or a docker volume.
RUN mkdir -p /var/data/wonderfleet && useradd -u 10001 -m wonderfleet && chown -R wonderfleet /var/data/wonderfleet /app
USER wonderfleet

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "WonderFleet.Api.dll"]
