# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first (cached until a project file changes)
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/OrderProcessing.BuildingBlocks/*.csproj src/BuildingBlocks/OrderProcessing.BuildingBlocks/
COPY src/Services/Orders/OrderProcessing.Orders.Domain/*.csproj src/Services/Orders/OrderProcessing.Orders.Domain/
COPY src/Services/Orders/OrderProcessing.Orders.Application/*.csproj src/Services/Orders/OrderProcessing.Orders.Application/
COPY src/Services/Orders/OrderProcessing.Orders.Infrastructure/*.csproj src/Services/Orders/OrderProcessing.Orders.Infrastructure/
COPY src/Services/Orders/OrderProcessing.Orders.Api/*.csproj src/Services/Orders/OrderProcessing.Orders.Api/
RUN dotnet restore src/Services/Orders/OrderProcessing.Orders.Api/OrderProcessing.Orders.Api.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Orders/ src/Services/Orders/
RUN dotnet publish src/Services/Orders/OrderProcessing.Orders.Api/OrderProcessing.Orders.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderProcessing.Orders.Api.dll"]
