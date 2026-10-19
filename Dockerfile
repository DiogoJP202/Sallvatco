# Bases fixadas por versão e digest; atualizar em conjunto com testes e auditoria.
FROM node:24.19.0-bookworm-slim@sha256:a9f5f7c91a432850b2a8a7797adf5eadb6c733ceed61167806cee7ea7fbc29df AS frontend
WORKDIR /source
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts
COPY src/Sallvat.Web/ ./src/Sallvat.Web/
RUN npm run css:build

FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510 AS build
WORKDIR /source
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ ./src/
RUN dotnet restore src/Sallvat.Web/Sallvat.Web.csproj --locked-mode
COPY --from=frontend /source/src/Sallvat.Web/wwwroot/css/app.css ./src/Sallvat.Web/wwwroot/css/app.css
RUN dotnet publish src/Sallvat.Web/Sallvat.Web.csproj -c Release --no-restore --self-contained false -p:UseAppHost=false -p:PrebuiltTailwind=true -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble@sha256:011bb5f30180717b1c8b65822ff2c99bcb96bc65af0164589751b83c7b4949f7 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/var/lib/sallvat/keys \
    ImageStorage__RootPath=/var/lib/sallvat/images \
    DOTNET_EnableDiagnostics=0
RUN mkdir -p /var/lib/sallvat/keys /var/lib/sallvat/images \
    && chown -R "${APP_UID}:${APP_UID}" /var/lib/sallvat \
    && chmod 700 /var/lib/sallvat/keys /var/lib/sallvat/images
COPY --from=build /out/ ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Sallvat.Web.dll"]
