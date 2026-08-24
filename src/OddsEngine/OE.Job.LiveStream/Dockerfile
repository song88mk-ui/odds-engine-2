FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/OddsEngine/OE.Shared OE.Shared
COPY src/OddsEngine/OE.Job.LiveStream OE.Job.LiveStream

WORKDIR /src/OE.Job.LiveStream

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# We need the cert to connect to the DocumentDB cluster
RUN apt-get update && \
    apt-get install curl -y && \
    curl -o /usr/local/share/ca-certificates/aws-global-bundle.crt https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem && \
    update-ca-certificates

COPY --from=build /app .
ENTRYPOINT ["dotnet", "OE.Job.LiveStream.dll"]
