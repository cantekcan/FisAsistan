# FisAsistan

Mali müşavirler için fiş fotoğraflarını dijitalleştiren MVP. **Hiçbir AI/LLM API'si veya ücretli
servis kullanılmaz** — OCR yerel (Tesseract), alan çıkarımı kural tabanlı (regex/C#), çoklu fiş
tespiti klasik görüntü işleme (OpenCvSharp4).

Akış: **Fotoğraf yükle → OCR → kural tabanlı alan çıkarımı → kullanıcı doğrulama → onay → CSV/Excel.**
Bir fotoğrafta birden fazla fiş varsa sistem bunları ayırıp her birini ayrı fiş olarak işler.

## Hızlı Başlangıç (Docker)

```bash
docker compose up --build
```

- Arayüz: http://127.0.0.1:5175
- API: http://localhost:8080 — Swagger: http://localhost:8080/swagger

Servisler `restart: unless-stopped` ile tanımlı; Docker Desktop açılınca kendiliğinden başlar.

## Yerel Geliştirme

Gereksinimler: .NET 10 SDK, Node.js 20+, Docker Desktop.

```bash
./scripts/setup-tessdata.ps1          # Tesseract dil verisi (bir kez; Linux/macOS: .sh)
docker compose up -d postgres
cd src/FisAsistan.Api && dotnet run   # http://localhost:5080, migration'lar otomatik
cd frontend && npm install && npm run dev
```

Frontend'in API adresi `frontend/.env` → `VITE_API_BASE_URL`. Farklı bir portta açılırsa o portu
`appsettings.json` → `Cors:AllowedOrigins` listesine ekleyin.

## Mimari

```
src/FisAsistan.Domain          Entity'ler, enum'lar
src/FisAsistan.Application     Arayüzler, DTO'lar, kural tabanlı parser (her alan için ayrı IFieldExtractor)
src/FisAsistan.Infrastructure  EF Core + PostgreSQL, Tesseract, OpenCV segmentasyon, ImageSharp, export, JWT
src/FisAsistan.Api             ASP.NET Core Web API
tests/FisAsistan.Tests         xUnit (99 test)
frontend/                      React + TypeScript + Vite
```

Bulunamayan alanlar tahmin edilmez, boş bırakılır ve kullanıcıya uyarı gösterilir. KDV, orana göre
gruplanarak (her oran tek satır) tutulur.

## Çoklu Fiş Yükleme

`/batch-upload` ekranı → OpenCV önerilen bölgeleri çıkarır → kullanıcı bölgeleri onaylar/siler/elle
ekler → her bölge mevcut tek fiş pipeline'ından geçer.

Segmentasyon: kağıt maskesinin dış konturları (fiş içindeki kutular ayrı fiş sayılmaz) → alan/en-boy/
içerme filtreleri → yapışık fişler, metin satırlarının kesmediği dikey koridordan bölünür → her bölgede
en az 3 metin satırı aranır → perspektif düzeltme.

**Sınırlamalar** (sonuç her zaman kullanıcı onayından geçer):

- Kağıtları birbirine değen, metni kenara dayalı fişler ayrılamayabilir.
- Kısa fişlerde fiyat kolonu ayrı bölge çıkabilir; fişe düşen kalın koyu şerit fişi bölebilir.
- Alt alta yapışık, üst üste binmiş fişler ve beyaz zemindeki beyaz fiş desteklenmez.
- Gerçek fotoğraf test seti küçüktür (4 görüntü, kişisel veri olduğu için repoda yok).

## Testler

```bash
dotnet test
cd frontend && npx tsc --noEmit
```

API testleri sahte OCR ve InMemory veritabanı kullanır; segmentasyon testleri gerçek OpenCV ile
sentetik görseller ve (varsa) yerel gerçek fotoğraflar üzerinde çalışır.

## Notlar

- ImageSharp v2.1.13'e sabit (v3+ ticari lisans gerektirir).
- Dosya formatları: JPG, JPEG, PNG, BMP (maks. 10 MB).
- Muhasebe programı entegrasyonu, e-posta doğrulama ve çoklu mükellef desteği kapsam dışı.
- Dockerfile, Tesseract NuGet paketinin Linux'ta beklediği native kütüphane adları için
  `liblept`/`libtesseract`'ı `/app/x64/` altına kopyalar.
