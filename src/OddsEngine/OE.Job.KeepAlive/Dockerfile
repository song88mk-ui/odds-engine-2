FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/OddsEngine/OE.Shared OE.Shared
COPY src/OddsEngine/OE.Job.KeepAlive OE.Job.KeepAlive

WORKDIR /src/OE.Job.KeepAlive

RUN dotnet restore
RUN dotnet build \
  --no-restore \
  --configuration Release
RUN dotnet publish \
  -o /app --no-restore && \
  rm -f /app/appsettings.*.json

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

COPY --from=build /app .
ENTRYPOINT ["dotnet", "OE.Job.KeepAlive.dll"]
