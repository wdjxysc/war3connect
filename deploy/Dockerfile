FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY Directory.Build.props NuGet.Config ./
COPY src/War3Connect.Core/ src/War3Connect.Core/
COPY src/War3Connect.Server/ src/War3Connect.Server/
RUN dotnet publish src/War3Connect.Server -c Release -o /app --configfile NuGet.Config -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DataDirectory=/app/data
RUN mkdir -p /app/data && chown $APP_UID /app/data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "War3Connect.Server.dll"]
