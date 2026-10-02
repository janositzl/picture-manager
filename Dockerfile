# PictureManager.Api container image — API, web frontend and PostgreSQL client.
#
# Stages: build the web frontend (Vite/React) as static files, build the
# published API plus a self-contained EF Core migration bundle, then ship
# both from a slim runtime image. The API serves the built frontend directly
# from wwwroot (see Program.cs), and the migration bundle lets the container
# apply pending EF Core migrations on startup without carrying the .NET SDK
# or the `dotnet-ef` tool at runtime.

ARG DOTNET_VERSION=10.0
ARG NODE_VERSION=22-alpine
ARG BUFFALO_L_SHA256=80ffe37d8a5940d59a7384c201a2a38d4741f2f3c51eef46ebb28218a7b0ca2f

FROM node:${NODE_VERSION} AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ .
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore first so dependency layers cache independently of source changes.
COPY PictureManager.slnx ./
COPY src/PictureManager.Api/PictureManager.Api.csproj src/PictureManager.Api/
COPY src/PictureManager.Application/PictureManager.Application.csproj src/PictureManager.Application/
COPY src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj src/PictureManager.Infrastructure/
COPY src/PictureManager.Model/PictureManager.Model.csproj src/PictureManager.Model/
COPY src/PictureManager.Worker/PictureManager.Worker.csproj src/PictureManager.Worker/
RUN dotnet restore src/PictureManager.Api/PictureManager.Api.csproj

COPY src/ src/
RUN dotnet publish src/PictureManager.Api/PictureManager.Api.csproj \
    -c Release -o /app/publish --no-restore

# Self-contained migration bundle: applies pending migrations against
# whatever connection string it is given, then exits.
RUN dotnet tool install --global dotnet-ef --version 10.* \
    && export PATH="$PATH:/root/.dotnet/tools" \
    && dotnet ef migrations bundle \
        --project src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj \
        --startup-project src/PictureManager.Api/PictureManager.Api.csproj \
        --self-contained -r linux-x64 \
        -o /app/publish/efbundle --force

# InsightFace buffalo_l: SCRFD-10G detector + ArcFace R50 recognizer (non-commercial use only).
FROM alpine:3.20 AS models
ARG BUFFALO_L_SHA256
RUN apk add --no-cache curl unzip \
    && curl -fsSL -o /tmp/buffalo_l.zip https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip \
    && echo "${BUFFALO_L_SHA256}  /tmp/buffalo_l.zip" | sha256sum -c - \
    && mkdir -p /models/buffalo_l /tmp/buffalo_l \
    && unzip -q /tmp/buffalo_l.zip -d /tmp/buffalo_l \
    && find /tmp/buffalo_l -name det_10g.onnx -exec cp {} /models/buffalo_l/ \; \
    && find /tmp/buffalo_l -name w600k_r50.onnx -exec cp {} /models/buffalo_l/ \; \
    && test -f /models/buffalo_l/det_10g.onnx && test -f /models/buffalo_l/w600k_r50.onnx

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

RUN groupadd --system picturemanager && useradd --system --gid picturemanager picturemanager \
    && mkdir -p /data/images /data/thumbnail-cache /app/logs \
    && chown -R picturemanager:picturemanager /app /data

COPY --from=build /app/publish/ ./
COPY --from=web-build /web/dist/ ./wwwroot/
COPY --from=models /models/ ./models/
COPY docker/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh /app/efbundle

USER picturemanager
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/bundle \
    ASPNETCORE_ENVIRONMENT=Production \
    FaceRecognition__ModelDirectory=/app/models/buffalo_l
EXPOSE 8080

ENTRYPOINT ["/app/entrypoint.sh"]
