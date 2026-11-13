FROM mcr.microsoft.com/dotnet/sdk:11.0.100

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN install -d -o app -g app /home/app

USER app
WORKDIR /workspaces/derail
