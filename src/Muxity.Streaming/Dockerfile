FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Muxity.Shared/Muxity.Shared.csproj",        "Muxity.Shared/"]
COPY ["src/Muxity.Streaming/Muxity.Streaming.csproj",  "Muxity.Streaming/"]
RUN dotnet restore "Muxity.Streaming/Muxity.Streaming.csproj"
COPY src/Muxity.Shared/   Muxity.Shared/
COPY src/Muxity.Streaming/ Muxity.Streaming/
RUN dotnet publish "Muxity.Streaming/Muxity.Streaming.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Muxity.Streaming.dll"]
