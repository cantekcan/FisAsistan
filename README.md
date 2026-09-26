# FisAsistan

Mali müşavirler için fiş fotoğraflarını dijitalleştiren, **tamamen yerel/ücretsiz OCR ve kural tabanlı
parser** kullanan (hiçbir AI/LLM API'si ve ücretli servis olmadan) bir MVP.

Akış: **Fiş fotoğrafı yükle → OCR (Tesseract) → kural tabanlı alan çıkarımı → kullanıcı
doğrulama/düzeltme → onay → CSV/Excel dışa aktarım.**

## Mimari

Hafif Clean Architecture / Modular Monolith:

```
src/
  FisAsistan.Domain          Entity'ler, enum'lar — dış bağımlılık yok
  FisAsistan.Application     Arayüzler, DTO'lar, kural tabanlı parser motoru (regex, saf C#)
  FisAsistan.Infrastructure  EF Core + PostgreSQL, Tesseract OCR, ImageSharp ön işleme,
                              dosya depolama, CSV/Excel export, JWT/BCrypt auth
  FisAsistan.Api             ASP.NET Core Web API, controller'lar, Swagger
tests/
  FisAsistan.Tests           xUnit — parser, export, API entegrasyon testleri (53 test)
frontend/
  React + TypeScript + Vite  Login, Dashboard, Fiş Yükleme, Fiş Detay/Düzenleme ekranları
```

**Parser tasarımı:** Her fiş alanı (`ReceiptFieldName`) için ayrı bir `IFieldExtractor` sınıfı vardır
(`src/FisAsistan.Application/Receipts/Parsing/Extractors/`). Hiçbir alan tahmin edilmez — bulunamayan
alanlar boş bırakılır ve kullanıcıya açık bir uyarı gösterilir. `ReceiptParserService` tüm extractor'ları
çalıştırır ve tutarlılık kontrolleri (ör. Ara Toplam + KDV ≠ Genel Toplam) yaparak açıklanabilir
uyarılar üretir — "güven skoru" yerine her zaman *hangi kural, hangi metinden, neden* bu sonucu
üretti bilgisini taşır.

## Gereksinimler

- .NET 10 SDK
- Node.js 20+ ve npm
- Docker Desktop (PostgreSQL ve/veya tam stack için)
- (Yerel çalıştırma için) Tesseract dil verisi — bkz. aşağıdaki kurulum adımı

## Hızlı Başlangıç (Docker Compose ile — önerilen)

```bash
docker compose up --build
```

Bu komut PostgreSQL'i ve (Tesseract dahil) API'yi ayağa kaldırır. API `http://localhost:8080` üzerinde,
Swagger `http://localhost:8080/swagger` adresinde çalışır. Frontend'i ayrıca çalıştırın (bkz. aşağı).

## Yerel Geliştirme (Docker'sız backend)

1. **Tesseract dil verisini indirin** (bir kez, ~40 MB, resmi `tesseract-ocr/tessdata` deposundan):

   ```bash
   ./scripts/setup-tessdata.sh        # Linux/macOS/WSL
   # veya
   ./scripts/setup-tessdata.ps1       # Windows PowerShell
   ```

2. **PostgreSQL'i başlatın:**

   ```bash
   docker compose up -d postgres
   ```

3. **API'yi çalıştırın** (migration'lar otomatik uygulanır):

   ```bash
   cd src/FisAsistan.Api
   dotnet run
   ```

   API varsayılan olarak `http://localhost:5080`'de (launchSettings.json) çalışır; Swagger UI kök
   `/swagger` altında.

4. **Frontend'i çalıştırın:**

   ```bash
   cd frontend
   npm install
   npm run dev
   ```

   `frontend/.env` içindeki `VITE_API_BASE_URL` backend adresini gösterir (varsayılan
   `http://localhost:5080`). Vite dev sunucusu farklı bir portta açılırsa (5173 meşgulse),
   backend `appsettings.json` → `Cors:AllowedOrigins` listesine o portu eklemeniz gerekir.

## Testler

```bash
dotnet test
```

53 test: tarih/saat/VKN-TCKN/KDV/toplam tutar çıkarım testleri, farklı fiş formatı testleri,
tutarlılık kontrolü testleri, CSV/Excel export testleri, API entegrasyon testleri (auth, upload,
dosya güvenliği, onay/red/silme/export akışları). API testleri gerçek Tesseract/PostgreSQL yerine
sahte (fake) OCR servisi ve InMemory veritabanı kullanır — harici bağımlılık gerektirmez.

Frontend tip kontrolü:

```bash
cd frontend
npx tsc --noEmit
```

## Önemli Tasarım Kararları ve Sınırlamalar

- **Hiçbir AI/LLM API'si veya ücretli servis kullanılmıyor.** OCR tamamen yerel (Tesseract, açık
  kaynak), alan çıkarımı tamamen regex/kural tabanlı (C#).
- **SixLabors.ImageSharp bilinçli olarak v2.1.13'e sabitlendi** — v3+ ticari lisans (Six Labors
  Split License) gerektiriyor; v2.1.x Apache-2.0 ile tamamen ücretsiz.
- Desteklenen dosya formatları: JPG, JPEG, PNG, BMP (maks. 10 MB). PDF desteklenmiyor.
- Muhasebe programına (Luca/Logo/Mikro) otomatik entegrasyon **kapsam dışı** — yalnızca CSV/Excel
  dışa aktarım var. Hedef program netleşmeden bu entegrasyon eklenmemeli.
- Basit tek-kullanıcı yetkilendirme modeli (her mali müşavir kendi yüklediği fişleri görür);
  çoklu mükellef/tenant izolasyonu bu MVP kapsamında yok.

## Kalan/Bilinen Sorunlar

- Satıcı adı ve adres çıkarımı heuristik tabanlıdır (fişin üst satırlarına bakar); farklı yazar
  kasa marka formatlarında yanlış satırı seçebilir — bu yüzden UI'de her zaman "mutlaka gözden
  geçirin" notuyla işaretlenir.
- Docker imajı build sırasında internetten Tesseract dil paketlerini apt üzerinden indirir;
  tamamen air-gapped ortamlarda önceden indirilmiş bir imaj gerekir.
- **Dockerfile notu:** `Tesseract` NuGet paketinin (charlesw/Tesseract) Linux'taki native
  kütüphane arama mantığı standart `dlopen`/`LD_LIBRARY_PATH` yerine Windows'taki NuGet klasör
  yapısını (`x64/leptonica-1.82.0.dll`) taklit ediyor — bu yüzden Dockerfile, apt ile kurulan
  `liblept`/`libtesseract` paylaşımlı kütüphanelerini beklenen adlarla `/app/x64/` altına da
  kopyalıyor. Bu, gerçek bir konteynerde test edilerek doğrulandı (bkz. commit geçmişi).
- E-posta doğrulama / şifre sıfırlama akışları yok (temel auth altyapısı istendiği için MVP'de
  kapsam dışı bırakıldı).
