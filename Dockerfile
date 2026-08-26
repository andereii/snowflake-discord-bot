# Etapa de compilación
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# DMD (compilador D) portable: se usa en esta etapa para compilar el piechart
# de las encuestas CONTRA la glibc de esta imagen (la misma del runtime),
# evitando binarios compilados contra glibc más nuevas que fallen en producción.
# gcc es el linker que usa dmd (el binario resultante queda dinámico contra esta glibc).
RUN apt-get update && \
    apt-get install -y --no-install-recommends xz-utils curl gcc libc6-dev && \
    rm -rf /var/lib/apt/lists/*

# Restaurar dependencias
COPY src/Snowflake.Bot/Snowflake.Bot.csproj Snowflake.Bot/
RUN dotnet restore Snowflake.Bot/Snowflake.Bot.csproj

# Copiar el código y compilar
COPY src/Snowflake.Bot/ Snowflake.Bot/
WORKDIR /src/Snowflake.Bot
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Compilar el generador de gráficas de encuestas (piechart) con DMD 2.109.1
COPY src/Dlang/ /src/Dlang/
RUN curl -sL -o /tmp/dmd.tar.xz https://downloads.dlang.org/releases/2.x/2.109.1/dmd.2.109.1.linux.tar.xz \
    && mkdir -p /tmp/dmd \
    && tar -xJf /tmp/dmd.tar.xz -C /tmp/dmd \
    && mkdir -p /app/publish/Dlang \
    && cd /src/Dlang \
    && /tmp/dmd/dmd2/linux/bin64/dmd -O -release -d -I. \
        -of=/app/publish/Dlang/piechart \
        piechart.d color.d png.d core.d bmp.d font8x8.d

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

# /app/Dlang/piechart viene compilado dentro de /app/publish (etapa build),
# enlazado contra la misma glibc de esta imagen de runtime.
COPY --from=build /app/publish .

# Exponer el puerto de la API web
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Snowflake.Bot.dll"]
