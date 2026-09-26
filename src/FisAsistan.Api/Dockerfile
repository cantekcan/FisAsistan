# syntax=docker/dockerfile:1

# ---- Build aşaması ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FisAsistan.slnx .
COPY src/FisAsistan.Domain/FisAsistan.Domain.csproj src/FisAsistan.Domain/
COPY src/FisAsistan.Application/FisAsistan.Application.csproj src/FisAsistan.Application/
COPY src/FisAsistan.Infrastructure/FisAsistan.Infrastructure.csproj src/FisAsistan.Infrastructure/
COPY src/FisAsistan.Api/FisAsistan.Api.csproj src/FisAsistan.Api/
COPY tests/FisAsistan.Tests/FisAsistan.Tests.csproj tests/FisAsistan.Tests/

RUN dotnet restore src/FisAsistan.Api/FisAsistan.Api.csproj

COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/FisAsistan.Api/FisAsistan.Api.csproj -c Release -o /app/publish --no-restore

# ---- Çalışma zamanı aşaması ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Tesseract OCR'ı (yerel, ücretsiz, açık kaynak) ve Türkçe/İngilizce dil paketlerini kur.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        tesseract-ocr \
        tesseract-ocr-eng \
        tesseract-ocr-tur \
        libleptonica-dev \
    && rm -rf /var/lib/apt/lists/* \
    && TESSDATA_SRC=$(find / -type d -name "tessdata" 2>/dev/null | head -n1) \
    && mkdir -p /app/tessdata \
    && cp "$TESSDATA_SRC"/*.traineddata /app/tessdata/ \
    # Tesseract'ın .NET sarmalayıcısı (charlesw/Tesseract) Windows'taki DLL adlarına
    # (leptonica-1.82.0.dll, tesseract50.dll) karşılık gelen .so isimlerini arar;
    # Debian paketleri farklı isimlendirdiği için sembolik link kuruyoruz.
    && LEPT_LIB=$(find /usr/lib -name "liblept.so*" ! -type l | sort | head -n1) \
    && TESS_LIB=$(find /usr/lib -name "libtesseract.so*" ! -type l | sort | head -n1) \
    && ln -sf "$LEPT_LIB" /usr/lib/x86_64-linux-gnu/libleptonica-1.82.0.so \
    && ln -sf "$TESS_LIB" /usr/lib/x86_64-linux-gnu/libtesseract50.so \
    # .NET'in DllImport("dl") çözümlemesi sürümsüz "libdl.so" ister; minimal runtime
    # imajında yalnızca "libdl.so.2" bulunuyor (libc6-dev kurulu değil), sembolik link ekliyoruz.
    && ln -sf /usr/lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so \
    && ldconfig \
    && echo "$LEPT_LIB" > /tmp/lept_lib_path \
    && echo "$TESS_LIB" > /tmp/tess_lib_path

COPY --from=build /app/publish .

# Tesseract'ın .NET sarmalayıcısı (InteropDotNet) native kütüphaneleri sistem yolларında değil,
# uygulamanın kendi çalışma dizininde (Windows'taki DLL yan yükleme modeline benzer şekilde)
# arıyor; bu yüzden beklenen adlarla doğrudan /app içine de kopyalıyoruz.
RUN LEPT_LIB=$(cat /tmp/lept_lib_path) \
    && TESS_LIB=$(cat /tmp/tess_lib_path) \
    && cp "$LEPT_LIB" /app/libleptonica-1.82.0.so \
    && cp "$TESS_LIB" /app/libtesseract50.so \
    # InteropDotNet, Windows NuGet paketindeki "x64/" klasör yapısını taklit ederek
    # native kütüphaneyi dlopen'a hiç ulaşmadan doğrudan bu yoldan (File.Exists) arıyor.
    && mkdir -p /app/x64 \
    && cp "$LEPT_LIB" /app/x64/libleptonica-1.82.0.so \
    && cp "$TESS_LIB" /app/x64/libtesseract50.so \
    && rm -f /tmp/lept_lib_path /tmp/tess_lib_path

RUN mkdir -p /app/storage/receipts

# dlopen() Linux'ta çalışma dizinini otomatik aramaz (Windows'un aksine); native
# kütüphaneleri hem /usr/lib/x86_64-linux-gnu'ya sembolik link hem de /app'e kopyaladık,
# LD_LIBRARY_PATH ile ikisini de dlopen'ın bare-filename arama sırasına dahil ediyoruz.
ENV LD_LIBRARY_PATH=/app:/usr/lib/x86_64-linux-gnu:${LD_LIBRARY_PATH}

EXPOSE 8080
ENTRYPOINT ["dotnet", "FisAsistan.Api.dll"]
