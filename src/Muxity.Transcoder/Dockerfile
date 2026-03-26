# Software (CPU) transcoder — includes FFmpeg from package manager
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Muxity.Shared/Muxity.Shared.csproj",          "Muxity.Shared/"]
COPY ["src/Muxity.Transcoder/Muxity.Transcoder.csproj",  "Muxity.Transcoder/"]
RUN dotnet restore "Muxity.Transcoder/Muxity.Transcoder.csproj"
COPY src/Muxity.Shared/    Muxity.Shared/
COPY src/Muxity.Transcoder/ Muxity.Transcoder/
RUN dotnet publish "Muxity.Transcoder/Muxity.Transcoder.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Muxity.Transcoder.dll"]
