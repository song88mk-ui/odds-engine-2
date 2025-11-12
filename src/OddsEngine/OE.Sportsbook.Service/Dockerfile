FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/OddsEngine/OE.Shared OE.Shared
COPY src/OddsEngine/OE.Sportsbook.Service OE.Sportsbook.Service

WORKDIR /src/OE.Sportsbook.Service

RUN dotnet restore
RUN dotnet build --configuration Release
RUN dotnet publish -o /source/app --no-restore && \
  cd /source/app && rm -f appsettings.*.json

FROM mcr.microsoft.com/dotnet/aspnet:8.0
EXPOSE 80 8080
WORKDIR /app
COPY --from=build /source/app .
ENTRYPOINT ["dotnet", "OE.Sportsbook.Service.dll"]
