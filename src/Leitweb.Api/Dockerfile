FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY Leitweb.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
RUN groupadd --system --gid 10001 leitweb \
    && useradd --system --uid 10001 --gid leitweb --no-create-home leitweb
COPY --from=build /app/publish .

ARG LEITWEB_BUILD_REVISION
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Build__Revision=${LEITWEB_BUILD_REVISION} \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
USER 10001:10001
ENTRYPOINT ["dotnet", "Leitweb.Api.dll"]
