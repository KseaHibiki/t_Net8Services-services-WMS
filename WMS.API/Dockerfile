FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY shared/Shop.Events/Shop.Events.csproj shared/Shop.Events/
COPY services/WMS/src/WMS.Domain/WMS.Domain.csproj services/WMS/src/WMS.Domain/
COPY services/WMS/src/WMS.Application/WMS.Application.csproj services/WMS/src/WMS.Application/
COPY services/WMS/src/WMS.Infrastructure/WMS.Infrastructure.csproj services/WMS/src/WMS.Infrastructure/
COPY services/WMS/src/WMS.API/WMS.API.csproj services/WMS/src/WMS.API/

RUN dotnet restore services/WMS/src/WMS.API/WMS.API.csproj

COPY shared/Shop.Events/ shared/Shop.Events/
COPY services/WMS/src/WMS.Domain/ services/WMS/src/WMS.Domain/
COPY services/WMS/src/WMS.Application/ services/WMS/src/WMS.Application/
COPY services/WMS/src/WMS.Infrastructure/ services/WMS/src/WMS.Infrastructure/
COPY services/WMS/src/WMS.API/ services/WMS/src/WMS.API/

RUN dotnet publish services/WMS/src/WMS.API/WMS.API.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WMS.API.dll"]