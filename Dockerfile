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
# The aspnet runtime image does not ship curl, so probe with the runtime that is already present.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD ["dotnet", "RoutePacer.Server.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "RoutePacer.Server.dll"]
