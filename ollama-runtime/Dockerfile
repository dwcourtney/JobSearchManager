FROM golang:1.26.6-trixie@sha256:23fdfd3a6abc97c81e32a724cdd1cf541c06c416eb04d717815f4ed7c75623d0 AS build
ARG OLLAMA_SOURCE_REVISION=f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a
WORKDIR /src
RUN git init \
    && git remote add origin https://github.com/ollama/ollama.git \
    && git fetch --depth=1 origin ${OLLAMA_SOURCE_REVISION} \
    && git checkout --detach FETCH_HEAD \
    && test "$(git rev-parse HEAD)" = "${OLLAMA_SOURCE_REVISION}"
RUN go get \
      github.com/buger/jsonparser@v1.1.2 \
      golang.org/x/image@v0.43.0 \
      golang.org/x/mod@v0.40.0 \
      golang.org/x/net@v0.56.0 \
      golang.org/x/text@v0.39.0 \
    && go mod tidy \
    && CGO_ENABLED=1 go build -buildvcs=false -trimpath -buildmode=pie \
      -ldflags="-w -s -X github.com/ollama/ollama/version.Version=0.33.2" -o /out/ollama .

FROM ollama/ollama@sha256:9e7d782e99880c70f9563c51633da875ca605518a8f8d95c2532bda70a027b7a
ARG JSM_GIT_SHA=unknown
ARG OLLAMA_SOURCE_REVISION=f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a
COPY --from=build /out/ollama /usr/bin/ollama
LABEL org.opencontainers.image.revision=${JSM_GIT_SHA} \
      org.opencontainers.image.source-revision=${OLLAMA_SOURCE_REVISION} \
      org.opencontainers.image.version=0.33.2-jsm-security-rebuild
USER 65532:65532
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 CMD ["ollama", "list"]
