FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet restore src/Vaulta.Web.Api/Vaulta.Web.Api.csproj
RUN dotnet publish src/Vaulta.Web.Api/Vaulta.Web.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false
FROM base AS final
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Vaulta.Web.Api.dll"]
