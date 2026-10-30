FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:9c7b6b263d9d4c2e4718e16dae6d75cd8f4b5ccf206f1e7d48a98eb429981b03 AS build
ARG JSM_GIT_SHA=unknown
WORKDIR /src

COPY global.json Directory.Build.props JobSearchManager.csproj packages.lock.json ./
RUN dotnet restore JobSearchManager.csproj --locked-mode

COPY . ./
RUN dotnet publish JobSearchManager.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false \
    /p:SourceRevisionId=${JSM_GIT_SHA}

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble@sha256:43cbcf11fd6f88c0375c9c0f7fe59af619abeb323befe5f57dc1502b5a05d8c3 AS runtime
ARG JSM_GIT_SHA=unknown
WORKDIR /app

COPY --from=build /out ./

ENV JOBSEARCHMANAGER_COMMIT_SHA=${JSM_GIT_SHA}
LABEL org.opencontainers.image.revision=${JSM_GIT_SHA}

EXPOSE 8080
USER 1001:1001

HEALTHCHECK --interval=15s --timeout=10s --start-period=10s --retries=4 \
    CMD ["dotnet", "JobSearchManager.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "JobSearchManager.dll"]
