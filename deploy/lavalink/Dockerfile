FROM eclipse-temurin:17-jre
WORKDIR /opt/lavalink
COPY Lavalink.jar .
COPY application.yml .
COPY plugins/ ./plugins/
EXPOSE 2333
CMD ["java", "-jar", "Lavalink.jar"]
