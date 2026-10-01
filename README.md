# FisAsistan

Mali müşavirler için fiş fotoğraflarını dijitalleştiren, **tamamen yerel/ücretsiz OCR ve kural tabanlı
parser** kullanan (hiçbir AI/LLM API'si ve ücretli servis olmadan) bir MVP.

Akış: **Fiş fotoğrafı yükle → OCR (Tesseract) → kural tabanlı alan çıkarımı → kullanıcı
doğrulama/düzeltme → onay → CSV/Excel dışa aktarım.**

**Çoklu fiş desteği:** Tek bir fotoğrafta birden fazla fiş varsa (ör. masaya yan yana dizilmiş
10 fiş), sistem bunları OpenCV ile (yine AI kullanmadan) otomatik olarak ayırt edip her birini
bağımsız bir fişe dönüştürebilir — bkz. [Çoklu Fiş Yükleme](#çoklu-fiş-yükleme-bir-fotoğrafta-birden-fazla-fiş).

## Mimari

Hafif Clean Architecture / Modular Monolith:

```
src/
  FisAsistan.Domain          Entity'ler, enum'lar — dış bağımlılık yok
  FisAsistan.Application     Arayüzler, DTO'lar, kural tabanlı parser motoru (regex, saf C#)
  FisAsistan.Infrastructure  EF Core + PostgreSQL, Tesseract OCR, OpenCV çoklu-fiş segmentasyonu,
                              ImageSharp ön işleme, dosya depolama, CSV/Excel export, JWT/BCrypt auth
  FisAsistan.Api             ASP.NET Core Web API, controller'lar, Swagger
tests/
  FisAsistan.Tests           xUnit — parser, segmentasyon, export, API entegrasyon testleri (99 test)
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

Bu komut PostgreSQL'i, (Tesseract dahil) API'yi ve arayüzü (nginx) ayağa kaldırır:

- Arayüz: `http://127.0.0.1:5175`
- API: `http://localhost:8080`, Swagger: `http://localhost:8080/swagger`

Tüm servisler `restart: unless-stopped` ile tanımlıdır; Docker Desktop açıldığında kendiliğinden başlar.

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

## Çoklu Fiş Yükleme (Bir Fotoğrafta Birden Fazla Fiş)

Kullanıcı, masaya yan yana dizilmiş birden fazla fişin **tek bir fotoğrafını** yükleyebilir
(`/batch-upload` ekranı, `POST /api/receipt-batches/upload`). Akış:

```
Fotoğraf → OpenCV segmentasyonu (kontur tespiti + perspektif düzeltme)
         → önerilen bölgeler kullanıcı onayına sunulur (silme/elle bölge çizme)
         → onaylanan her bölge, MEVCUT tek-fiş pipeline'ından (önişleme → Tesseract → parser)
           bağımsız olarak geçer → N ayrı Receipt kaydı
```

**Kesinlikle AI/LLM/ML modeli/bulut servisi kullanılmaz** — tespit tamamen klasik görüntü
işleme (OpenCvSharp4). Pipeline:

```
Orijinal → Önişleme (gri, blur)
 → Kağıt maskesi: parlak (Otsu) VE düşük doygunluk (HSV S ≤ 60; renkli kumaş/masa hariç)
 → Metin maskesi: black-hat + karakter boyutu filtresi (karakter yüksekliği görüntüden tahmin edilir)
   + Hough ile uzun dikey çizgilerin (dikiş/kenar/gölge) metinden çıkarılması
 → Ham adaylar: kağıt maskesinin yalnızca DIŞ konturları (RETR_EXTERNAL) + minAreaRect
 → Aday filtreleme: göreli alan (≥ %1), en-boy oranı (≤ 8), dikdörtgensellik (≥ 0.5)
 → İçerme filtresi (başka adayın ≥ %80 içindeyse elenir) + tekrar/IoU (> 0.5) filtresi
 → Birleşik fiş tespiti ve bölme: aday dik çerçeveye çevrilir, metin satırlarının kesmediği
   dikey "koridor" aranır (±8° eğim taraması). Bölme için koridor + şunlardan en az biri gerekir:
   geniş boşluk / dikey kenar sürekliliği / parlaklık çukuru. Her iki parçada da yeterli metin
   ve makul genişlik şartı var (tek sinyale — ör. yalnızca genişliğe — dayanılmaz). Özyinelemeli.
 → Son doğrulama: her bölgede, bölgenin DİK çerçevesinde sayılan en az 3 metin satırı olmalı
   (metinsiz parlak alanlar fiş sayılmaz; eğik fişlerde de çalışır)
 → Perspektif düzeltme (warpPerspective) → Nihai kırpmalar
```

Bildirilen iki hata bu yapıyla giderildi: (1) fiş içindeki TOPKDV/TOPLAM kutusu gibi iç
bölgeler yalnızca dış kontur alındığı ve içerme filtresi uygulandığı için ayrı fiş olamaz;
(2) birbirine yapışık fişler ([BİM][ŞOK][TURUNCU]) metin koridoru + ek sinyallerle ayrılır.

**Segmentasyon sonucu asla kesin kabul edilmez** — OCR'a gönderilmeden önce kullanıcı onay
ekranında bölgeleri gözden geçirmesi, yanlış olanları silmesi, eksikleri elle (basit dikdörtgen
çizerek) eklemesi gerekir.

**Desteklenen/desteklenmeyen durumlar (dürüstçe):**

| Durum | Destek |
|---|---|
| Fişler aralarında boşlukla yan yana/alt alta, açılı (sentetik testlerde 0–30° doğrulandı) | ✅ İyi çalışır |
| Fişler koyu, düz bir zemin üzerinde, aralarında boşlukla | ✅ İyi çalışır |
| Fişler **yan yana, birbirine değiyor** (aralarında boşluk yok) | ✅ Metin koridoru ile ayrılır (gerçek fotoğraflarda 2 ve 3 yapışık fiş doğru ayrıldı). |
| Yapışık fişler + fotoğraf belirgin eğik (~6°+) ve fiş içi uzun kesikli çizgiler koridoru kesiyor | ⚠️ Stres setinde 28 senaryodan 1'i başarısız (3 yapışık fiş, −6° döndürme → 2 bulundu). Kullanıcı onay ekranında elle düzeltmeli. |
| Fişler **alt alta yapışık** (dikey istif) | ⚠️ Bilinçli olarak bölünmez — yatay bölme, fiş üzerindeki katlanma izlerini fiş sınırı sanma riski taşıyor. Elle bölge çizilmeli. |
| Fişler **üst üste binmiş** (biri diğerinin üstünde) | ⚠️ Bilinen sınırlama — tek bölge olarak algılanabilir; fişler aralanıp tekrar çekilmeli veya elle bölge çizilmeli. |
| Fiş, zeminle **aynı/benzer renkte** (ör. beyaz fiş beyaz masa) | ⚠️ Bilinen sınırlama — kontrast olmadan segmentasyon güvenilir çalışmaz, 0 bölge dönebilir. |
| Hiç bölge bulunamadı | Kullanıcıya net mesaj + "Tek fiş olarak devam et" / "Tekrar dene" seçenekleri sunulur, uygulama çökmez. |

**Veritabanı:** Yeni `ReceiptBatch` tablosu (orijinal fotoğraf + onay bekleyen bölgeler JSON
olarak) + mevcut `Receipt` tablosuna nullable `BatchId`/`SourceRegionJson` kolonları eklendi.
Tekil fiş yüklemelerinde bu kolonlar her zaman null kalır — **mevcut tek-fiş akışı hiç
değişmedi**. Batch'ten oluşan fişler normal `Receipt` kayıtları olduğundan mevcut onay/red/export
akışlarında ekstra kod gerekmeden aynen çalışır.

**Eşzamanlılık:** Bir fotoğraftaki bölgeler en fazla 3 eşzamanlı işlenir (`SemaphoreSlim(3)`).
Her bölge kendi DI scope'unda (dolayısıyla kendi `DbContext` örneğinde) işlenir — EF Core'un
`DbContext`'i thread-safe olmadığından bu şarttır. Tesseract tarafında her OCR çağrısı zaten
kendi motor örneğini oluşturduğundan paralel çalışmak güvenlidir.

## Testler

```bash
dotnet test
```

99 test: tarih/saat/VKN-TCKN/KDV/toplam tutar çıkarım testleri, farklı fiş formatı testleri,
tutarlılık kontrolü testleri, CSV/Excel export testleri, API entegrasyon testleri (auth, upload,
dosya güvenliği, onay/red/silme/export akışları), **çoklu-fiş segmentasyon testleri** (0/1/2/4/10
bölge, küçük/büyük alan filtreleme, üst üste binen bölgeler, döndürülmüş fişler, IoU/köşe sıralama
saf birim testleri) ve **çoklu-fiş API entegrasyon testleri** (batch oluşturma, bölge düzenleme,
işleme, kullanıcı izolasyonu, export'a yansıma). API testleri gerçek Tesseract/PostgreSQL yerine
sahte (fake) OCR servisi ve InMemory veritabanı kullanır — harici bağımlılık gerektirmez.
Segmentasyon testleri **gerçek OpenCV** ile iki tür görsel üzerinde çalışır: (a) programatik
olarak üretilen, üzerinde gerçek yazı satırları bulunan sentetik fişler (yapışık 2/3 fiş, iç
TOPKDV kutusu, metinsiz parlak alan, 0–30° döndürme vb.); (b) `tests/FisAsistan.Tests/TestData/RealReceipts`
altındaki **4 gerçek telefon fotoğrafı** (kişisel veri olduğu için repoda yok, `.gitignore`da; yalnızca yerelde) (tek BİM, tek fiş desenli zeminde, yapışık BİM+TURUNCU,
yapışık BİM+ŞOK+TURUNCU → beklenen 1/1/2/3). Gerçek fotoğraf seti küçüktür; daha fazla gerçek
örnekle genişletilmesi önerilir.

Frontend tip kontrolü:

```bash
cd frontend
npx tsc --noEmit
```

## Önemli Tasarım Kararları ve Sınırlamalar

- **Hiçbir AI/LLM API'si veya ücretli servis kullanılmıyor.** OCR tamamen yerel (Tesseract, açık
  kaynak), alan çıkarımı tamamen regex/kural tabanlı (C#), çoklu-fiş tespiti tamamen klasik
  görüntü işleme (OpenCvSharp4, Apache-2.0 lisanslı, açık kaynak).
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
