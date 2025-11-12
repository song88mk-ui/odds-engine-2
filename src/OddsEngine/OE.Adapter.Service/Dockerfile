FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/OddsEngine/OE.Shared OE.Shared
COPY src/OddsEngine/OE.Adapter.Service OE.Adapter.Service

WORKDIR /src/OE.Adapter.Service

RUN dotnet restore
RUN dotnet build --configuration Release
RUN dotnet publish -o /source/app --no-restore && \
  cd /source/app && rm -f appsettings.*.json

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update -qq && \
    apt-get install curl -y && \
    apt-get clean && \
    rm -fr /var/lib/apt/lists/*
COPY --from=build /source/app .
ENTRYPOINT ["dotnet", "OE.Adapter.Service.dll"]
