FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY src/OddsEngine/OE.Job.Settlement OE.Job.Settlement
COPY src/OddsEngine/OE.Shared OE.Shared

WORKDIR /src/OE.Job.Settlement

ARG NUGET_PASSWORD

RUN dotnet nuget add source \
  --username FlyingCockatoo \
  --password ${NUGET_PASSWORD} \
  --store-password-in-clear-text \
  --name github "https://nuget.pkg.github.com/FlyingCockatoo/index.json"

RUN  dotnet restore

RUN dotnet build \
  --no-restore \
  --configuration Release

RUN dotnet publish \
  -o publish \
  --no-restore && \
  cd publish && \
  rm -f appsettings.*.json

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /src/OE.Job.Settlement/publish .
CMD ["/usr/bin/dotnet", "OE.Job.Settlement.dll"]
