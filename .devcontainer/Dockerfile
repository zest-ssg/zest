# Zest SSG development container.
#
# Pins the .NET SDK that matches <TargetFramework> in Directory.Build.props so
# a fresh clone builds without a local toolchain. Git is included because the
# repository's husky hooks run on commit.

FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        git \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspaces/zest
