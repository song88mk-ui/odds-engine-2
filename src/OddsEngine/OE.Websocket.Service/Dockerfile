FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/OddsEngine/OE.Shared OE.Shared
COPY src/OddsEngine/OE.Websocket.Service OE.Websocket.Service

WORKDIR /src/OE.Websocket.Service

ARG NUGET_PASSWORD

RUN dotnet nuget add source \
  --username FlyingCockatoo \
  --password ${NUGET_PASSWORD} \
  --store-password-in-clear-text \
  --name github "https://nuget.pkg.github.com/FlyingCockatoo/index.json"

RUN dotnet restore
RUN dotnet build \
  --no-restore \
  --configuration Release
RUN dotnet publish \
  -o /app --no-restore && \
  rm -f /app/appsettings.*.json

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update -qq && \
    apt-get install curl -y && \
    apt-get clean && \
    rm -fr /var/lib/apt/lists/*

COPY --from=build /app .
ENTRYPOINT ["dotnet", "OE.Websocket.Service.dll"]
