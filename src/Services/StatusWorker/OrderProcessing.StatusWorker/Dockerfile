# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first (cached until a project file changes)
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Services/StatusWorker/OrderProcessing.StatusWorker/*.csproj src/Services/StatusWorker/OrderProcessing.StatusWorker/
RUN dotnet restore src/Services/StatusWorker/OrderProcessing.StatusWorker/OrderProcessing.StatusWorker.csproj

COPY src/Services/StatusWorker/ src/Services/StatusWorker/
RUN dotnet publish src/Services/StatusWorker/OrderProcessing.StatusWorker/OrderProcessing.StatusWorker.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# No web server needed: the plain .NET runtime image is enough.
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "OrderProcessing.StatusWorker.dll"]
