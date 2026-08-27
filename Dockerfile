FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore RoutePacer.slnx
RUN dotnet publish src/RoutePacer.Server/RoutePacer.Server.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK CMD curl -f http://127.0.0.1:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "RoutePacer.Server.dll"]
