FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:0e53453ccfc8ff2d51319fe80c678971c6d0f8008dff3565fa88e15840b69854 AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble@sha256:1dcd9841b075d1d1013caa170b86ae58b8a8a563de9a3e319fd46a45e7ecc130 AS runtime
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
