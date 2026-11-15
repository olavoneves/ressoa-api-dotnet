# Imagem da API de telemetria do Ressoa (build multi-stage).
# Build a partir da raiz do repositório:
#   docker build -f dockerfiles/Dockerfile -t ressoa-api-dotnet .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Ressoa.Telemetria.Api/Ressoa.Telemetria.Api.csproj src/Ressoa.Telemetria.Api/
RUN dotnet restore src/Ressoa.Telemetria.Api/Ressoa.Telemetria.Api.csproj
COPY src/ src/
RUN dotnet publish src/Ressoa.Telemetria.Api/Ressoa.Telemetria.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Usuário não root já incluso na imagem oficial
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ressoa.Telemetria.Api.dll"]
