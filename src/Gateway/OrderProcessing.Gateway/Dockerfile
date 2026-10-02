# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first (cached until a project file changes)
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/OrderProcessing.BuildingBlocks/*.csproj src/BuildingBlocks/OrderProcessing.BuildingBlocks/
COPY src/Gateway/OrderProcessing.Gateway/*.csproj src/Gateway/OrderProcessing.Gateway/
RUN dotnet restore src/Gateway/OrderProcessing.Gateway/OrderProcessing.Gateway.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Gateway/ src/Gateway/
RUN dotnet publish src/Gateway/OrderProcessing.Gateway/OrderProcessing.Gateway.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderProcessing.Gateway.dll"]
