FROM eclipse-temurin:17-jre
WORKDIR /opt/lavalink
ADD https://github.com/lavalink-devs/Lavalink/releases/download/4.2.2/Lavalink.jar Lavalink.jar
COPY application.yml .
EXPOSE 2333
CMD ["java", "-jar", "Lavalink.jar"]
