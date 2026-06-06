# syntax=docker/dockerfile:1.7
# Build context: InfinityAI.Document.Worker directory (self-contained, no external project refs)

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH

WORKDIR /source

COPY InfinityAI.Document.Worker/InfinityAI.Document.Worker.csproj InfinityAI.Document.Worker/
RUN dotnet restore InfinityAI.Document.Worker/InfinityAI.Document.Worker.csproj -a $TARGETARCH

COPY InfinityAI.Document.Worker/ InfinityAI.Document.Worker/

RUN dotnet publish InfinityAI.Document.Worker/InfinityAI.Document.Worker.csproj \
    --no-restore \
    -a $TARGETARCH \
    -c Release \
    -o /app \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final

WORKDIR /app

COPY --from=build /app .

ENTRYPOINT ["dotnet", "InfinityAI.Document.Worker.dll"]
