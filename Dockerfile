# Usurper Reborn — Game Server Container
# Multi-stage build: SDK for compilation, runtime-deps for minimal image

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY usurper-reloaded.csproj runtimeconfig.template.json GlobalUsings.cs ./
COPY Scripts/ Scripts/
COPY Console/ Console/
COPY Data/ Data/
COPY Assets/ Assets/
COPY Localization/ Localization/
COPY app.ico ./
RUN dotnet publish usurper-reloaded.csproj -c Release -r linux-x64 --self-contained -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0
WORKDIR /opt/usurper
COPY --from=build /app .

RUN mkdir -p /var/usurper

EXPOSE 4000
VOLUME ["/var/usurper"]

ENTRYPOINT ["./UsurperReborn"]
CMD ["--mud-server", "--mud-port", "4000", \
     "--db", "/var/usurper/usurper_online.db", \
     "--log-stdout", "--auto-provision", \
     "--sim-interval", "30"]
