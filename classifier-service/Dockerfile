FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:0e53453ccfc8ff2d51319fe80c678971c6d0f8008dff3565fa88e15840b69854 AS build
ARG CLASSIFIER_GIT_SHA=unknown
WORKDIR /src
COPY ClassifierService.csproj packages.lock.json ./
RUN dotnet restore ClassifierService.csproj --locked-mode
COPY Program.cs ./
RUN dotnet publish ClassifierService.csproj --configuration Release --no-restore \
    --output /out /p:UseAppHost=false /p:SourceRevisionId=${CLASSIFIER_GIT_SHA}

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble@sha256:1dcd9841b075d1d1013caa170b86ae58b8a8a563de9a3e319fd46a45e7ecc130
ARG CLASSIFIER_GIT_SHA=unknown
WORKDIR /app
COPY --from=build /out ./
ENV CLASSIFIER_GIT_SHA=${CLASSIFIER_GIT_SHA}
LABEL org.opencontainers.image.revision=${CLASSIFIER_GIT_SHA}
EXPOSE 8081
USER 65532:65532
HEALTHCHECK --interval=15s --timeout=5s --start-period=5s --retries=4 \
    CMD ["dotnet", "JsmClassifier.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "JsmClassifier.dll"]
