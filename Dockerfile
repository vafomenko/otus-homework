ARG DOTNET_VERSION=10.0

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG TARGETARCH

WORKDIR /source

COPY global.json Directory.Build.props ./
COPY src/Blt.Users.Api/Blt.Users.Api.csproj src/Blt.Users.Api/

RUN dotnet restore src/Blt.Users.Api/Blt.Users.Api.csproj -a "$TARGETARCH"

COPY src/Blt.Users.Api/ src/Blt.Users.Api/

RUN dotnet publish src/Blt.Users.Api/Blt.Users.Api.csproj \
    --configuration Release \
    --no-restore \
    --arch "$TARGETARCH" \
    --self-contained false \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

WORKDIR /app

LABEL org.opencontainers.image.title="BLT User API" \
      org.opencontainers.image.description="RESTful CRUD service for users"

EXPOSE 8000

COPY --from=build /app/publish ./

USER app

ENTRYPOINT ["dotnet", "Blt.Users.Api.dll"]
