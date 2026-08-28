FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

COPY src/Leitweb.Api/Leitweb.Api.csproj src/Leitweb.Api/
RUN dotnet restore src/Leitweb.Api/Leitweb.Api.csproj

COPY src/Leitweb.Api/ src/Leitweb.Api/
RUN dotnet publish src/Leitweb.Api/Leitweb.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS runtime
WORKDIR /app

RUN groupadd --system --gid 10001 leitweb \
    && useradd --system --uid 10001 --gid leitweb --no-create-home leitweb
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
USER leitweb
ENTRYPOINT ["dotnet", "Leitweb.Api.dll"]
