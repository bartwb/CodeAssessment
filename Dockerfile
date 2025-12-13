# ===== Fase 1: Build + tools + cache prep =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# NuGet/Dotnet env voor voorspelbaarheid en snelheid
ENV NUGET_PACKAGES=/root/.nuget/packages \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    HOME=/root

# NuGet.Config met nuget.org
RUN mkdir -p /root/.nuget/NuGet
RUN printf '<configuration>\n  <packageSources>\n    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />\n  </packageSources>\n</configuration>\n' > /root/.nuget/NuGet/NuGet.Config

# Copy solution (alles) en restore/publish specifiek de API
WORKDIR /src
COPY . .

RUN dotnet restore "CodeAssessment.Api/CodeAssessment.Api.csproj" --configfile /root/.nuget/NuGet/NuGet.Config
RUN dotnet publish "CodeAssessment.Api/CodeAssessment.Api.csproj" -c Release -o /app/publish --no-restore


# dotnet-script installeren
RUN dotnet tool install -g dotnet-script
ENV PATH="$PATH:/root/.dotnet/tools"

# ---------- PREWARM 1: cache voor 'app' werkmap ----------
WORKDIR /tmp/dotnet-script-cache-prep
RUN echo 'Console.WriteLine(1+2);' > prep.csx \
 && /root/.dotnet/tools/dotnet-script prep.csx || true

# Expliciete restore van het gegenereerde script-proj (app/* varianten)
RUN set -eux; \
    for P in /root/.cache/dotnet-script/app/net8.0 /root/.cache/dotnet-script/app; do \
      if [ -f "$P/script.csproj" ]; then \
        dotnet restore "$P/script.csproj" -r linux-x64 --configfile /root/.nuget/NuGet/NuGet.Config || true; \
      fi; \
    done

# ---------- PREWARM 2: cache voor runtime werkmap '/tmp' ----------
WORKDIR /tmp
RUN echo 'Console.WriteLine(42);' > prep.csx \
 && /root/.dotnet/tools/dotnet-script prep.csx || true

# Expliciete restore van de '/tmp' cache
RUN if [ -f /root/.cache/dotnet-script/tmp/net8.0/script.csproj ]; then \
      dotnet restore /root/.cache/dotnet-script/tmp/net8.0/script.csproj \
        -r linux-x64 --configfile /root/.nuget/NuGet/NuGet.Config || true; \
    fi

# ===== Fase 2: Runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Zelfde env als bij build, zodat runtime restore niet opnieuw first-time dingen doet
ENV NUGET_PACKAGES=/root/.nuget/packages \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    HOME=/root \
    ASPNETCORE_URLS=http://0.0.0.0:6000

# Kopieer app + tools + caches
COPY --from=build /app/publish /app
COPY --from=build /root/.dotnet/tools /root/.dotnet/tools
COPY --from=build /root/.nuget /root/.nuget
COPY --from=build /root/.cache/dotnet-script /root/.cache/dotnet-script
ENV PATH="$PATH:/root/.dotnet/tools"

# Zorg dat /tmp bestaat en schrijfbaar is
RUN mkdir -p /tmp && chmod 1777 /tmp

EXPOSE 6000
ENTRYPOINT ["dotnet", "webapi.dll"]
