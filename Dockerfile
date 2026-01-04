# ===== Build stage =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything
COPY . .

# Warm up NuGet cache in build layer (helps runtime restore speed)
RUN dotnet new console -n Warmup --framework net8.0 && \
    dotnet restore Warmup/Warmup.csproj && \
    rm -rf Warmup

# Warm up classlib/test dependencies to prime cache
RUN dotnet new classlib -n CacheLib --framework net8.0 && \
    dotnet restore CacheLib/CacheLib.csproj && \
    rm -rf CacheLib

# Restore + publish jouw API
RUN dotnet restore "CodeAssessment.Api/CodeAssessment.Api.csproj"
RUN dotnet publish "CodeAssessment.Api/CodeAssessment.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ===== Runtime stage (SDK nodig voor dotnet new/restore/build/run) =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS final
WORKDIR /app

# Luister altijd op 6000
ENV ASPNETCORE_URLS=http://0.0.0.0:6000 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_UI_LANGUAGE=en \
    NUGET_XMLDOC_MODE=skip \
    TMPDIR=/app/tmp

EXPOSE 6000

# Kopieer gepublishte API
COPY --from=build /app/publish ./

# Zorg dat /tmp bestaat en schrijfbaar is (jij gebruikt Path.GetTempPath()) en TMPDIR voor lokale snelle disk
RUN mkdir -p /tmp /app/tmp && chmod 1777 /tmp /app/tmp

# Start API
ENTRYPOINT ["dotnet", "CodeAssessment.Api.dll"]
