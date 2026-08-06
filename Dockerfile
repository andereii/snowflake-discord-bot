# Etapa de compilación
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaurar dependencias
COPY src/Snowflake.Bot/Snowflake.Bot.csproj Snowflake.Bot/
RUN dotnet restore Snowflake.Bot/Snowflake.Bot.csproj

# Copiar el código y compilar
COPY src/Snowflake.Bot/ Snowflake.Bot/
WORKDIR /src/Snowflake.Bot
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Etapa de ejecución (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Instalar ffmpeg y yt-dlp (Python 3) requeridos para las descargas de multimedia
RUN apt-get update && \
    apt-get install -y ffmpeg python3 curl && \
    curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp && \
    chmod a+rx /usr/local/bin/yt-dlp && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Exponer el puerto de la API web
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Snowflake.Bot.dll"]
