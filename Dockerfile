# First stage - Build Crystite from source
FROM mcr.microsoft.com/dotnet/sdk:8.0

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

USER root
WORKDIR /root

COPY . /root/Crystite
WORKDIR /root/Crystite
RUN dotnet publish -f net8.0 -c Release -r linux-x64 --self-contained false -o bin/crystite Crystite/Crystite.csproj
RUN dotnet publish -f net8.0 -c Release -r linux-x64 --self-contained false -o bin/crystitectl Crystite.Control/Crystite.Control.csproj

# Second stage - Copy build artifacts and configure application container
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim

LABEL org.opencontainers.image.authors="Jarl Gullberg & djsime1"
LABEL org.opencontainers.image.source="https://github.com/djsime1/Crystite"
LABEL org.opencontainers.image.description="Custom headless server for the VR sandbox Resonite"

USER root
WORKDIR /root

RUN apt-get update
RUN apt-get install -y wget libassimp5 libfreeimage3 libfreetype6 libopus0 libbrotli1 zlib1g yt-dlp jq unzip

COPY --from=0 /root/Crystite/bin/crystite /usr/lib/crystite
COPY --from=0 /root/Crystite/bin/crystitectl /usr/lib/crystitectl

RUN mkdir /Config /Data /Cache /Logs /var/lib/crystite
VOLUME [ "/Data", "/Logs", "/var/lib/crystite" ]
COPY docker/appsettings.json /etc/crystite/appsettings.json
COPY docker/Config.json /Config/Config.json
COPY docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
WORKDIR /var/lib/crystite

ENTRYPOINT [ "sh", "/entrypoint.sh" ]
CMD [ "/usr/lib/crystite/crystite" ]