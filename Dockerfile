ARG DOTNET_VERSION=10.0

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG TARGETARCH

WORKDIR /source

COPY global.json Directory.Build.props ./
COPY src/Blt.Orders.Api/Blt.Orders.Api.csproj src/Blt.Orders.Api/

RUN dotnet restore src/Blt.Orders.Api/Blt.Orders.Api.csproj -a "$TARGETARCH"

COPY src/Blt.Orders.Api/ src/Blt.Orders.Api/

RUN dotnet publish src/Blt.Orders.Api/Blt.Orders.Api.csproj \
    --configuration Release \
    --no-restore \
    --arch "$TARGETARCH" \
    --self-contained false \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

WORKDIR /app

LABEL org.opencontainers.image.title="BLT Orders API" \
      org.opencontainers.image.description="Walking skeleton of the BLT Order Service"

EXPOSE 8000

COPY --from=build /app/publish ./

USER app

ENTRYPOINT ["dotnet", "Blt.Orders.Api.dll"]
