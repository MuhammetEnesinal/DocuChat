# ── Build aşaması ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Önce yalnız csproj'lar → restore katmanı cache'lenir (kod değişince restore tekrarlanmaz)
COPY DocuChat.Domain/DocuChat.Domain.csproj DocuChat.Domain/
COPY DocuChat.Application/DocuChat.Application.csproj DocuChat.Application/
COPY DocuChat.Infrastructure/DocuChat.Infrastructure.csproj DocuChat.Infrastructure/
COPY DocuChat.API/DocuChat.API.csproj DocuChat.API/
RUN dotnet restore DocuChat.API/DocuChat.API.csproj

COPY . .
# Cache-bust kolu: daemon çökmesi publish katmanını bozuk cache'lerse
# `--build-arg REBUILD=<yeni değer>` yalnız BU katmandan itibarens yeniden derletir
# (apt/LibreOffice katmanı runtime aşamasında — etkilenmez).
ARG REBUILD=0
RUN echo "rebuild-tag: $REBUILD" \
    && dotnet publish DocuChat.API/DocuChat.API.csproj -c Release -o /app/publish /p:UseAppHost=false

# ── Runtime aşaması ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# LibreOffice: .doc/.rtf/.mhtml → docx dönüşümü için (DocumentParserService "soffice" çağırır).
# writer yeterli (xlsx/csv C# tarafında ClosedXML ile işleniyor); fontlar Türkçe metin için.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libreoffice-writer fonts-dejavu fontconfig curl \
    && rm -rf /var/lib/apt/lists/*

# Aynı cache-bust kolu: bozuk COPY katmanı cache'e girdiyse buradan itibaren tazelenir.
ARG REBUILD=0
RUN echo "runtime-rebuild-tag: $REBUILD"
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5025
EXPOSE 5025

ENTRYPOINT ["dotnet", "DocuChat.API.dll"]
