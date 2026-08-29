FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble AS build
WORKDIR /src

COPY JobSearchManager.csproj ./
RUN dotnet restore JobSearchManager.csproj

COPY . ./
RUN dotnet publish JobSearchManager.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble AS runtime
WORKDIR /app

COPY --from=build /out ./

EXPOSE 8080
USER 1001:1001

HEALTHCHECK --interval=15s --timeout=10s --start-period=10s --retries=4 \
    CMD ["dotnet", "JobSearchManager.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "JobSearchManager.dll"]
