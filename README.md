# Dükkan Paneli

Küçük bir dükkanın sahibinin işlerini uzaktan takip etmesi için web uygulaması: satış (sepet + fiş), stok ve kapasite, stok hareketleri, gelir-gider, tedarikçiler, borç-alacak, takvim, raporlar ve **her değişikliğin geri alınabildiği işlem geçmişi**.

- **Backend:** C# · ASP.NET Core 10 Minimal API · Entity Framework Core · SQLite
- **Frontend:** HTML/CSS/JavaScript (kütüphanesiz), telefona kurulabilen PWA
- **Testler:** xUnit + `WebApplicationFactory` ile 17 entegrasyon testi (gerçek HTTP uç noktaları, geçici veritabanı)

## Çalıştırma

Gereken: [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
cd dukkan-paneli
dotnet run --project src/DukkanPaneli.Api --urls http://localhost:5080
```

Tarayıcıda `http://localhost:5080` adresini açın. İlk açılışta **kurulum ekranı** gelir: dükkan adını ve sahip hesabını oluşturursunuz (isterseniz örnek verilerle). Çalışan hesapları sonra **Ayarlar → Kullanıcılar**'dan eklenir.

Testler:

```powershell
dotnet test
```

## Özellikler

| Bölüm | Ne yapar |
|---|---|
| Genel Bakış | Bu ay / geçen ay kazanç, bugünkü satış, borç-alacak, stok değeri, 6 aylık grafik, uyarılar (geciken ödeme, kritik stok, bugünkü etkinlik) |
| Satış | Dokunmatik ürün kartları, sepet, indirim, KDV dahil toplam, nakit / kart / veresiye, yazdırılabilir fiş |
| Satışlar & Fişler | Gün/ay/yıl/aralık bazında satış listesi, fiş tekrar yazdırma, satış iptali (stok geri gelir) |
| Gelir & Gider | Dönem seçimi, KDV'li gider girişi, CSV. Satıştan/ödemeden otomatik oluşan kayıtlar kilitlidir |
| Ürünler & Stok | Kapasite doluluğu, kritik stok, stok girişi (peşin veya tedarikçiye vadeli), fire / iade / sayım / düzeltme |
| Stok Hareketleri | Her stok değişikliğinin kaydı; ürün bazında geçmiş ve **istenen tarihteki stok** |
| Borç & Alacak | Vade takibi, kısmi ödeme/tahsilat, ödeme iptali |
| Takvim | Etkinlikler + borç/alacak vadeleri, aylık tekrar |
| Raporlar | Ciro, brüt kâr ve marj, ortalama sepet, en çok satan ürünler, ödeme türleri, kasiyer bazında satış, tahmini KDV, fire kaybı; **Excel** indirme ve **Yazdır / PDF** |
| İşlem Geçmişi | Kim, ne zaman, neyi değiştirdi (önce/sonra değerleriyle). **Geri al** ve **Yeniden uygula** |
| Ayarlar | Dükkan bilgileri, kullanıcılar ve roller, şifre değiştirme, bildirimler, veritabanı yedeği |

### Roller

- **Sahip:** her şey.
- **Çalışan:** satış, stok girişi/düzeltme, takvim, fişler. Gelir-gider, borçlar, raporlar, maliyetler, geçmiş ve kullanıcı yönetimi kapalı; geçmiş tarihe satış giremez, satış iptal edemez. Bu kurallar **sunucuda** uygulanır (arayüzde gizlemek yeterli sayılmaz).

### Geçmiş ve geri alma nasıl çalışıyor?

Her değişiklik `AppDb.SaveAuditedAsync` üzerinden kaydedilir: EF Core'un değişiklik izleyicisinden etkilenen her satırın **önceki ve sonraki** hâli aynı veritabanı işleminde (transaction) `AuditLogs` tablosuna JSON olarak yazılır.

**Geri al** (`AppDb.UndoAsync`) eklenen satırları siler, silinenleri geri ekler, değişenleri eski değerine döndürür. Önce, etkilenen satırların hâlâ işlemden hemen sonraki hâlinde olup olmadığını kontrol eder. Biri sonradan başka bir işlemle değiştiyse (ör. aynı ürüne ikinci bir satış), veri bozulmasın diye geri almayı **reddeder** ve önce geri alınması gereken işlemleri listeler. Geri alma işleminin kendisi de geçmişe yazılır ve **yeniden uygulanabilir**.

### Tutarlılık kuralları

- Stok sadece hareketlerle değişir (ürün düzenleme ekranından stok değiştirilemez).
- Satışa, ödemeye ya da stok girişine bağlı gelir/gider kayıtları elle silinemez veya düzenlenemez; ilgili işlem iptal edilir ya da geri alınır.
- Ürün ve tedarikçi silme aslında arşivlemedir; eski satışlar ve hareketler bozulmaz.
- Nakit esası: veresiye satış ve vadeli alım, tahsil edildiğinde / ödendiğinde gelir ya da gider olur.

### Güvenlik

- Şifreler ASP.NET Core Identity `PasswordHasher` (PBKDF2) ile saklanır.
- `HttpOnly` + `SameSite=Strict` oturum çerezi; şifre/rol değişince ya da hesap kapatılınca açık oturumlar hemen düşer.
- Değişiklik isteklerinde özel başlık zorunlu (CSRF koruması), giriş denemesi sınırı (IP başına dakikada 10), güvenlik başlıkları (CSP vb.).

## Uzaktan erişim

Uygulama bir sunucu olduğu için, çalıştığı bilgisayara erişilebilen her cihazdan kullanılabilir:

- **Aynı ağda:** `--urls http://0.0.0.0:5080` ile çalıştırıp telefondan `http://<bilgisayarın-ip-adresi>:5080` adresini açın.
- **İnternetten:** bir sunucuya (VPS, Azure App Service vb.) yükleyin ya da dükkan bilgisayarını Cloudflare Tunnel / Tailscale ile güvenli biçimde açın. İnternete açarken **mutlaka HTTPS** kullanın.

Telefonda tarayıcı menüsünden **"Ana ekrana ekle"** ile uygulama gibi kurulur.

## Proje yapısı

```
src/DukkanPaneli.Api/
  Program.cs                  kimlik doğrulama, güvenlik, uç noktaların bağlanması
  Data/Entities.cs            veri modeli
  Data/AppDb.cs               EF Core bağlamı + denetim kaydı ve geri alma
  Data/DemoData.cs            tutarlı örnek veri (30 günlük simülasyon)
  Endpoints/*.cs              Auth, Catalog (ürün/stok/tedarikçi), Sales, Finance, Core, Reports
  wwwroot/                    arayüz (js/core, views-ops, views-admin, forms, main) + PWA
tests/DukkanPaneli.Tests/     entegrasyon testleri
```

## Bilinen sınırlar / sonraki adımlar

- Veritabanı şeması `EnsureCreated` ile oluşturuluyor. Gerçek kullanımda şema değişiklikleri için **EF Core migrations**'a geçilmeli.
- Bildirimler panel açıkken ya da arka planda çalışırken gelir. Uygulama tamamen kapalıyken bildirim için Web Push (VAPID) sunucusu gerekir.
- Fiş bir **bilgi fişidir**; e-Arşiv fatura / ÖKC entegrasyonu yoktur. KDV rakamları tahminidir.
- Yedeği geri yüklemek şu an elle yapılıyor (sunucudaki `App_Data/dukkan.db` dosyasını değiştirerek).
