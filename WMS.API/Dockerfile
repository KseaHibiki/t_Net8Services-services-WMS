FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY services/WMS/src/WMS.Domain/WMS.Domain.csproj WMS.Domain/
COPY services/WMS/src/WMS.Application/WMS.Application.csproj WMS.Application/
COPY services/WMS/src/WMS.Infrastructure/WMS.Infrastructure.csproj WMS.Infrastructure/
COPY services/WMS/src/WMS.API/WMS.API.csproj WMS.API/
COPY shared/Shop.Events/Shop.Events.csproj Shop.Events/
RUN dotnet restore WMS.API/WMS.API.csproj

COPY services/WMS/src/ ./services/WMS/src/
COPY shared/Shop.Events/ ./shared/Shop.Events/
WORKDIR /src/services/WMS/src
RUN dotnet publish WMS.API/WMS.API.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WMS.API.dll"]