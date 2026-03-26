# Build Blazor WASM and serve with nginx
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Muxity.Web/Muxity.Web.csproj", "Muxity.Web/"]
RUN dotnet restore "Muxity.Web/Muxity.Web.csproj"
COPY src/Muxity.Web/ Muxity.Web/
RUN dotnet publish "Muxity.Web/Muxity.Web.csproj" -c Release -o /app/publish

FROM nginx:1.25-alpine AS runtime
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/Muxity.Web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
