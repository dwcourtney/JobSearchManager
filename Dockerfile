FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94 AS runtime
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
