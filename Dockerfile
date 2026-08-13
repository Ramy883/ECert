# =========================================================
# ECert Training Center Management System
# Docker image for Render.com deployment
# =========================================================

# ===== Build stage =====
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file first and restore dependencies (better layer caching)
COPY ECert/ECert.csproj ECert/
RUN dotnet restore ECert/ECert.csproj

# Copy the rest of the source and publish a Release build
COPY ECert/ ECert/
RUN dotnet publish ECert/ECert.csproj -c Release -o /app/publish

# ===== Runtime stage =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Runtime folders for user uploads and QR codes
RUN mkdir -p /app/wwwroot/uploads /app/wwwroot/qrcodes

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=10000
ENV DOTNET_USE_POLLING_FILE_WATCHER=1
EXPOSE 10000

# Render injects the assigned port into the PORT env var
ENTRYPOINT ASPNETCORE_URLS="http://+:${PORT:-10000}" dotnet ECert.dll
