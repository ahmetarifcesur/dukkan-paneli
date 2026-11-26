# Öğrenme Rehberi

Bu rehber projeyi **baştan sona anlamak** için hazırlandı. Amaç, mülakatta herhangi bir dosyayı açıp
"bu ne yapıyor, neden böyle?" sorusuna kendi cümlelerinizle cevap verebilmek.

Önerilen yol: her bölümü okuyun, ilgili dosyayı açın, sonra sondaki **alıştırmaları kendiniz** yapın.
Alıştırmalar aynı zamanda depoya *sizin yazdığınız* commit'ler ekler.

---

## 1. Büyük resim

```
Tarayıcı (wwwroot/js)  ──HTTP/JSON──►  Uç nokta (Features/*/…Endpoints.cs)
                                            │  isteği alır, servisi çağırır
                                            ▼
                                       Servis (Features/*/…Service.cs)
                                            │  iş kurallarını uygular
                                            ▼
                                       AppDb (EF Core) ──► SQLite (App_Data/dukkan.db)
                                            │
                                            └─ SaveAuditedAsync: değişikliği + geçmiş kaydını tek işlemde yazar
```

Üç katman var ve her birinin tek bir işi var:

| Katman | Bilir | Bilmez |
|---|---|---|
| **Uç nokta** (Endpoints) | HTTP: URL, yetki, durum kodu | İş kuralları |
| **Servis** (Service) | İş kuralları: "stok eksiye düşemez" | HTTP, çerez, JSON |
| **AppDb** | Tablolar, ilişkiler, geçmiş kaydı | Hangi kuralın neden var olduğu |

Bu ayrım sayesinde servisleri HTTP olmadan test edebiliyoruz (bkz. `tests/.../Unit/SaleServiceTests.cs`).

## 2. Okuma sırası

1. **`Program.cs`**: 50 satır civarı. Uygulama nasıl ayağa kalkıyor?
2. **`Infrastructure/ServiceRegistration.cs`**: Servisler DI'a nasıl kaydediliyor, uç noktalar nasıl bağlanıyor?
3. **`Data/Entities/*.cs`**: Veri modeli. Önce `Product`, `Sale`, `StockMovement`, `CashEntry`, `Debt`.
4. **`Data/AppDb.cs`**: Tablolar ve ilişkiler. Neden hiçbir ilişkide zincirleme silme (cascade) yok?
5. **`Features/Sales/SaleService.cs` → `CreateAsync`**: Projenin kalbi. Aşağıdaki 3. bölümde adım adım anlatılıyor.
6. **`Data/AppDb.Audit.cs`**: Geçmiş ve geri alma. En zor dosya, en sona bırakın.
7. **`Infrastructure/AuthSetup.cs` + `SecurityMiddleware.cs`**: Giriş, roller ve güvenlik.
8. **`tests/`**: Testleri okumak, kodun *ne yapması gerektiğini* anlamanın en hızlı yolu.

## 3. Bir isteğin yolculuğu: "Satışı tamamla" düğmesi

1. **Tarayıcı**: `wwwroot/js/views-ops.js` → `completeSale()` fonksiyonu
   `POST /api/sales` isteği gönderir. Gövdesi şöyle:
   `{ paymentType: "Nakit", lines: [{ productId: 3, quantity: 2 }] }`
   `core.js` → `api()` her isteğe `X-Requested-With: dukkan` başlığını ekler (CSRF koruması).
2. **Ara katmanlar** (`Program.cs` sırasıyla):
   - `UseDukkanSecurity`: başlık kontrolü.
   - `UseAuthentication`: çerezden kullanıcıyı okur.
   - `AuthSetup.ValidateSessionAsync`: hesap kapatıldıysa oturumu düşürür.
3. **Uç nokta**: `SalesEndpoints.cs` → `MapPost("/")`.
   - ASP.NET Core JSON'u `SaleRequest` kaydına çevirir. `"Nakit"` değeri, `Enums.cs`'teki `[JsonStringEnumMemberName]` sayesinde `PaymentType.Cash` olur.
   - `SaleService` nesnesini DI verir.
   - `Actor.From(ctx.User)` çerezdeki bilgilerden kimin satış yaptığını çıkarır.
4. **Servis**: `SaleService.CreateAsync` sırayla şunları yapar:
   - Doğrulama: sepet boş mu, çalışan geçmiş tarih girmiş mi, stok yetiyor mu?
   - `Sale` ve `SaleLine` nesneleri oluşturur. Toplamı, indirimi ve KDV'yi hesaplar.
   - Her kalem için `Product.Stock` değerini düşürür ve bir `StockMovement` ekler.
   - Peşin satışta `CashEntry` (gelir), veresiye satışta `Debt` (alacak) ekler.
   - Hepsini **tek** `SaveAuditedAsync` çağrısıyla kaydeder.
5. **AppDb.SaveAuditedAsync**:
   - Bir transaction açar.
   - EF Core'un ChangeTracker'ından eklenen beş-on satırın görüntüsünü alır.
   - Kaydeder ve bu görüntüleri JSON olarak `AuditLogs` tablosuna yazar.
   - Bir hata olursa **hiçbiri** kaydedilmez.
6. **Yanıt**: `OpResult.Ok(log, …)` → `HttpResults.ToHttp()` → `{ auditId, undoable, summary, data }`.
   Tarayıcı bu `auditId` ile bildirimdeki **Geri al** düğmesini çalıştırır.

## 4. Kavramlar ve "neden böyle?"

**Stok neden doğrudan düzenlenemiyor?**
Stok değiştiğinde nedeni (satış, fire, sayım…) kaybolursa "stok neden 3 eksik?" sorusunun cevabı olmaz.
Her değişiklik bir `StockMovement` olduğu için **stok = hareketlerin toplamı** kuralı her zaman geçerli. Bu kural
`ReportAndDemoTests.Ornek_veride_stoklar_hareketlerle_tutarli` testiyle kontrol ediliyor. Bonus olarak geçmişteki
herhangi bir tarihteki stok hesaplanabiliyor (`StockService.ProductMovementsAsync`).

**Para için neden `decimal`?**
`double` ikili tabanda çalışır: `0.1 + 0.2 = 0.30000000000000004`. Para hesaplarında kuruş kayması kabul edilemez.

**SQLite'ta decimal neden tamsayı olarak saklanıyor?** (`Data/Converters.cs`)
SQLite'ın decimal türü yok. EF Core onu metin olarak saklarsa `ORDER BY` ve `SUM` yanlış çalışır.
×10000 ile `long` olarak saklıyoruz.

**Neden exception yerine `OpResult`?**
"Yetersiz stok" bir hata değil, beklenen bir durum. Exception, gerçekten beklenmeyen durumlar (veritabanı
kapandı gibi) içindir. `OpResult` ile her metodun nasıl başarısız olabileceği koddan okunur.

**Neden DTO? Entity'yi doğrudan döndürsek olmaz mı?**
- `User` nesnesinde `PasswordHash` var; dışarı sızmamalı.
- Çalışan `Cost` (alış fiyatı) alanını görmemeli.
- API'nin şekli veritabanı şemasından bağımsız olmalı: bir sütun eklemek arayüzü bozmamalı.

**Nakit esası nedir?**
Gelir, para kasaya girdiğinde yazılır. Veresiye satış anında gelir değil **alacak** oluşur; gelir, tahsilat
yapıldığında yazılır. Küçük esnaf "kasada ne var" diye düşündüğü için bu yöntem seçildi.

**Bağlı kayıt nedir?** (`CashService.LockReason`)
Satıştan otomatik oluşan gelir kaydı elle silinirse satış duruyor ama parası kayboluyor; veri tutarsız
hâle geliyor. Bu yüzden bu kayıtlar kilitli. Değiştirmenin yolu ana işlemi (satışı) iptal etmek ya da geri almak.

**Geri alma nasıl çalışıyor?** (`AppDb.Audit.cs`)
1. Kaydetmeden önce ve sonra her satırın tüm alanlarının JSON görüntüsü alınır.
2. Geri almak için:
   - eklenen satırlar silinir,
   - silinenler geri eklenir,
   - değişenlere eski değerleri yazılır.
3. **Çakışma kontrolü**: Satır o işlemden sonra başka bir işlemle değiştiyse (ör. aynı ürüne ikinci satış),
   körü körüne geri almak ikinci satışın stok düşüşünü de silerdi. Bu durumda geri alma reddedilir ve
   önce hangi işlemin geri alınması gerektiği söylenir.

**Oturum anında nasıl düşüyor?** (`User.SecurityStamp`)
Çerezde bir "damga" var. Şifre, rol ya da aktiflik değişince veritabanındaki damga yenilenir. Her istekte
ikisi karşılaştırılır; tutmazsa oturum kapanır.

**CSRF nedir, nasıl korunuyoruz?**
Kötü niyetli bir site, sizin tarayıcınızdan bizim API'ye form gönderebilir; tarayıcı çerezi otomatik ekler.
Ama başka bir site özel bir HTTP başlığı (`X-Requested-With`) ekleyemez. Bu yüzden bu başlığı zorunlu tutuyoruz.
Ek olarak çerez `SameSite=Strict`.

**Kural neden sunucuda?** (`SaleService.CreateAsync`, `SecurityTests`)
Arayüz çalışana fiyat alanını göstermiyor, ama bu güvenlik değildir: herkes tarayıcının geliştirici
araçlarından API'ye istediği isteği gönderebilir. Bu yüzden "fiyatı sadece sahip değiştirir" kuralı sunucuda.
Genel ilke: **istemciye asla güvenme**; arayüzdeki kontroller sadece kullanım kolaylığı içindir.

**Arka plan servisi ve bakım komutları** (`AutoBackupService`, `Infrastructure/Commands.cs`)
- `BackgroundService`, uygulama açıkken arka planda çalışan bir döngüdür. Burada saatte bir "bugünün yedeği
  var mı?" diye bakıyor, yoksa `VACUUM INTO` ile tutarlı bir kopya alıyor ve eski yedekleri siliyor.
- Şifre sıfırlama ve geri yükleme web'den yapılamaz: sunucuya erişimi olan kişi `dotnet run -- <komut>` ile
  çalıştırır. `Program.cs`, ilk argüman bir komutsa web sunucusunu hiç başlatmadan komutu çalıştırıp çıkar.
- Geri yükleme önce dosyanın sağlam bir veritabanı olduğunu kontrol eder (`PRAGMA integrity_check`), mevcut
  veriyi kopyalar, sonra değiştirir. Silinen veriyi geri getirmenin yolunu açık bırakmak, iyi bir alışkanlıktır.

**Yapay zekâ asistanı nasıl kuruldu?** (`Features/Assistant/`)
- **Tek istek, tek cevap.** Model veritabanına dokunmaz; `ShopSnapshotService` dükkanın özetini metne çevirir, soru ile
  birlikte gönderilir. En basit yapı bu: ajan, araç çağrısı ya da döngü yok. Rakamlar `ReportService.Build` ile aynı
  hesaptan geldiği için asistan ekrandakiyle aynı rakamları söyler.
- **Arayüz arkasında model (`IAssistantModel`).** Gerçek uygulamada `ClaudeAssistantModel` Claude API'sini çağırır;
  testlerde sahte bir model konur (`AssistantTests`). Böylece testler internete çıkmaz, para harcamaz ve modele tam
  olarak ne gönderildiği kontrol edilebilir. Bu "bağımlılığı tersine çevirme" (dependency inversion) örneğidir.
- **Kişisel veri en aza.** `Pseudonyms` müşteri ve çalışan adlarını "Müşteri-1" gibi takma adlarla değiştirir, cevap
  gelince geri çevirir. KVKK'nın "veri minimizasyonu" ilkesinin koddaki karşılığı.
- **Hata yönetimi.** SDK'nın her HTTP durumu için ayrı istisna sınıfı var (401, 429, 5xx, bağlantı); her biri kullanıcıya
  ayrı bir Türkçe mesaj olarak döner. Anahtar sadece sunucuda durur.

## 5. Sık kullanılan komutlar

```powershell
dotnet run --project src/DukkanPaneli.Api --urls http://localhost:5080   # çalıştır
dotnet test                                                              # tüm testler
dotnet test --filter "FullyQualifiedName~Unit"                           # sadece birim testleri
dotnet tool restore                                                      # dotnet-ef aracını kur (ilk sefer)
dotnet ef migrations add YeniAlan --project src/DukkanPaneli.Api -o Data/Migrations   # şema değişikliği
dotnet ef migrations list --project src/DukkanPaneli.Api                 # migration listesi
dotnet run --project src/DukkanPaneli.Api -- help                       # bakım komutları (yedek, şifre sıfırlama)
```

## 6. Alıştırmalar (kolaydan zora)

Her alıştırmadan sonra `dotnet test` çalıştırın ve **ayrı bir commit** atın.

1. **Isınma: metin değiştirme.** Satış iptal edilirken yazılan özet metnini değiştirin
   (`SaleService.CancelAsync`). Bir testin bozulup bozulmadığını kontrol edin.

2. **Yeni doğrulama.** Ürün adı en az 2 karakter olsun (`ProductService.Validate`).
   Bunun için `StockTests` sınıfına bir test yazın: önce test, sonra kod (TDD).

3. **Yeni alan + migration.** `Product` varlığına `Barcode` (string, boş olabilir) ekleyin:
   - `Product.cs`, `ProductRequest`, `ProductDto` ve `ProductService` içinde alanı ekleyin.
   - `dotnet ef migrations add AddProductBarcode …` ile migration oluşturun ve oluşan dosyayı okuyun.
   - Arayüzde ürün formuna alanı ekleyin (`wwwroot/js/forms.js` → `productForm`).

4. **Yeni uç nokta.** `GET /api/products/low-stock`: kritik stoktaki ürünleri döndürsün. Adımlar:
   - Servise bir metot yazın.
   - Uç noktayı `CatalogEndpoints`'e ekleyin.
   - Bir entegrasyon testi yazın.

5. **İş kuralı.** Aynı müşterinin toplam açık veresiyesi 5.000 ₺'yi geçemesin.
   `SaleService.CreateAsync` içinde kontrol edin ve `SaleServiceTests` sınıfına test ekleyin.

6. **Zor: geri alma senaryosu.** Bir ürünün fiyatını değiştirin, sonra bir satış yapın, sonra fiyat
   değişikliğini geri almayı deneyin. Çakışma var mı? Neden? (İpucu: satış `Product` satırında hangi alanı
   değiştiriyor?) Cevabı bir testle doğrulayın.

7. **Proje: Android istemci.** Kotlin ile bu API'ye bağlanan basit bir uygulama yazın: giriş, ürün listesi, satış.
   Çerez tabanlı oturum için OkHttp'nin `CookieJar` arayüzünü kullanın ve `X-Requested-With` başlığını eklemeyi unutmayın.

## 7. Mülakatta sorulabilecekler

- "Bir satış sırasında elektrik kesilirse ne olur?" → Transaction: ya hepsi kaydedilir ya hiçbiri.
- "İki kasiyer aynı anda son ürünü satarsa?" → Şu an SQLite yazmaları sıraya koyar. Ama iki istek de
  stoğu aynı anda okursa ikisi de geçebilir. Çözüm: `Product`'a bir eşzamanlılık belirteci (concurrency token)
  eklemek. Bu, bilinen bir sınır ve iyi bir geliştirme konusu.
- "Neden SQLite?" → Tek dükkan, tek sunucu, kurulum gerektirmez, yedeği tek dosya. Büyürse EF Core sayesinde
  PostgreSQL'e geçmek için çoğunlukla bağlantı satırını değiştirmek yeter.
- "Kullanıcı şifresini unutursa?" → Web'den sıfırlama yok (e-posta altyapısı yok, saldırı yüzeyi de olurdu).
  Sunucuda `reset-password` komutu geçici şifre üretir ve kullanıcının açık oturumlarını kapatır.
- "Veri kaybına karşı ne yaptınız?" → Günlük otomatik yedek (`VACUUM INTO`), son 14 gün saklanıyor; geri yükleme
  komutu önce dosyayı doğruluyor ve mevcut veriyi kopyalıyor. Eksik: yedekler aynı diskte, başka yere kopyalanmalı.
- "Yapay zekâ özelliğini nasıl test ettiniz?" → Modeli bir arayüzün arkasına koydum; testlerde sahte model
  kullanılıyor ve modele giden metinde müşteri adının olmadığını, cevaptaki takma adların geri çevrildiğini doğruluyorum.
- "Testleriniz neyi kapsıyor, neyi kapsamıyor?" → Backend iş kuralları ve yetkiler kapsanıyor.
  Arayüz (JavaScript) için otomatik test yok.
