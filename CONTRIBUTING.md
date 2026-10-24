# Postacı'ya katkıda bulunma

Postacı'yı geliştirmeye ayıracağınız zaman için teşekkürler. Postacı bir şirket ürünü değil, **herkesin
geliştirmesine açık kişisel bir açık kaynak projesidir** ve büyük bölümü yapay zekâ yardımıyla yazılmıştır
(ayrıntılar [README'de](README.md#yapay-zekâ-ile-geliştirme)). İlk katkınız olsa bile çekinmeyin; nereden
başlayacağınızı bilmiyorsanız bir issue açıp sormanız yeterli.

Bu rehber; geliştirme ortamını kurmayı, kodun nasıl düzenlendiğini, yeni bir Exchange işlemi eklemeyi, test
yazmayı, yapay zekâ araçlarıyla katkı kurallarını ve pull request sürecini anlatır.

Kısa yol: [Ortam](#geliştirme-ortamı) → [Derle ve test et](#derleme-ve-test) → [Değişikliği yap](#katman-kuralları)
→ [Test ekle](#test-yazma) → [PR aç](#pull-request-süreci).

## İçindekiler

- [Başlamadan önce](#başlamadan-önce)
- [Gizlilik kuralı](#gizlilik-kuralı)
- [Yapay zekâ araçlarıyla katkı](#yapay-zekâ-araçlarıyla-katkı)
- [Geliştirme ortamı](#geliştirme-ortamı)
- [Derleme ve test](#derleme-ve-test)
- [Depo yapısı](#depo-yapısı)
- [Katman kuralları](#katman-kuralları)
- [Kod stili](#kod-stili)
- [Yeni bir EWS işlemi eklemek](#yeni-bir-ews-işlemi-eklemek)
- [Arayüz (WinUI) geliştirme](#arayüz-winui-geliştirme)
- [Test yazma](#test-yazma)
- [Commit ve dal kuralları](#commit-ve-dal-kuralları)
- [Pull request süreci](#pull-request-süreci)
- [Sürüm yayınlama (bakımcılar için)](#sürüm-yayınlama-bakımcılar-için)
- [İkonları yeniden üretmek](#ikonları-yeniden-üretmek)
- [Sık karşılaşılan sorunlar](#sık-karşılaşılan-sorunlar)

## Başlamadan önce

- Katkı türü fark etmez: hata bildirimi, öneri, belge ve yazım düzeltmesi, test, gerçek sunucuda deneme raporu,
  kod. **`iyi ilk issue`** etiketli konular yeni başlayanlar için seçilmiştir.
- Topluluk kurallarımız [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) dosyasındadır; katılan herkes bunlara uyar.

- **Hata bildirimi** ve **özellik önerisi** için [issue şablonlarını](.github/ISSUE_TEMPLATE) kullanın. Önce aynı
  konuda açık bir issue olup olmadığına bakın.
- **Güvenlik açıklarını** issue olarak açmayın; [SECURITY.md](SECURITY.md)'deki gizli bildirim yolunu kullanın.
- Büyük bir değişiklik (yeni ekran, yeni bağımlılık, mimari değişiklik) düşünüyorsanız kodlamaya başlamadan önce
  bir issue açıp yaklaşımı konuşalım; boşa emek harcanmasın.
- Küçük düzeltmeler (yazım hatası, açıkça yanlış bir davranış) için doğrudan PR açabilirsiniz.
- Proje [MIT Lisansı](LICENSE) ile dağıtılır. PR göndererek katkınızın aynı lisansla yayımlanmasını kabul etmiş
  olursunuz. Başka bir projeden kod kopyalıyorsanız lisansının MIT ile uyumlu olduğundan emin olun ve kaynağını PR
  açıklamasında belirtin.

## Gizlilik kuralı

Postacı kurumsal posta kutularıyla çalışır. Bu yüzden **depoya, issue'lara, PR'lara, test verilerine ve ekran
görüntülerine hiçbir zaman** şunları koymayın:

- gerçek e-posta adresleri, kişi adları, telefon numaraları,
- gerçek posta, toplantı veya kişi içerikleri,
- kurum içi sunucu adları, EWS adresleri, alan (domain) adları,
- parola, token veya ağ trafiği kaydı (Fiddler/Wireshark çıktısı vb.).

Test verilerinde `sirket.com`, `mail.test.local`, `ad.soyad@sirket.com` gibi uydurma değerler kullanın. Bir SOAP
yanıtını örnek olarak paylaşmanız gerekiyorsa önce bütün adları, adresleri, kimlikleri (`ItemId`, `ChangeKey`) ve
içerikleri değiştirin.

Yanlışlıkla gizli veri paylaşıldığını fark ederseniz hemen bakımcılara haber verin.
**Şüpheli durum 24 saat içinde Yönetim birimine bildirmesi gerekmektedir.**

## Yapay zekâ araçlarıyla katkı

Proje zaten yapay zekâ yardımıyla geliştirildiği için Claude, Copilot, ChatGPT gibi araçlarla hazırlanmış katkılar
da memnuniyetle kabul edilir. Tek koşul, şeffaflık ve sorumluluktur:

1. **Belirtin.** PR şablonundaki "Yapay zekâ kullanımı" bölümünde hangi aracı ve neyin için kullandığınızı kısaca
   yazın. Commit'e `Co-Authored-By:` satırı eklemeniz önerilir ama zorunlu değildir.
2. **Sahiplenin.** Gönderdiğiniz her satırı okuyun ve anlayın; inceleme sırasında "neden böyle?" sorusuna sizin
   yanıt verebilmeniz gerekir. "Yapay zekâ böyle yazdı" geçerli bir gerekçe değildir.
3. **Doğrulayın.** Testleri yerelde çalıştırın, arayüz değişikliğini Windows'ta açıp deneyin. Yapay zekânın var
   olmayan bir EWS öğesi, özniteliği veya API'si "uydurması" sık görülür; Microsoft belgeleriyle karşılaştırın.
4. **Küçük tutun.** İncelenmemiş, yüzlerce satırlık toplu üretimler yerine tek konulu, incelenebilir PR'lar açın.
5. **Gizli veri vermeyin.** Yapay zekâ aracına parola, token, gerçek posta içeriği, kurum içi sunucu adı veya
   başka bir şirketin kaynak kodunu yapıştırmayın. [Gizlilik kuralı](#gizlilik-kuralı) burada da geçerlidir.
6. **Lisansa dikkat edin.** Başka bir projeden birebir alındığını düşündüğünüz uzun kod parçalarını göndermeyin;
   katkınızın MIT lisansıyla yayımlanabilir olması gerekir.
7. **Issue'lar için de geçerli.** Yapay zekâya yazdırdığınız hata raporlarını göndermeden önce gerçekten
   yeniden üretebildiğinizi kontrol edin.

İnceleme, katkı yapay zekâ ile hazırlanmış olsun ya da olmasın aynı ölçütlerle yapılır.

## Geliştirme ortamı

| Ne yapacaksınız? | Gerekenler |
|---|---|
| Uygulamanın tamamını derlemek ve çalıştırmak | Windows 10 (19041+) veya 11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), WebView2 Runtime (Windows 11'de hazır gelir) |
| Visual Studio ile çalışmak (isteğe bağlı) | Visual Studio 2026 veya 2022 17.14+, **WinUI application development** iş yükü |
| Yalnızca `Core` / `ViewModels` / testler | .NET 10 SDK yeterli; Linux ve macOS'ta da çalışır |
| İkonları yeniden üretmek | Python 3, Node.js ve Playwright (Chromium) |

Gerçek bir Exchange sunucusuna erişiminiz olmasa da katkı verebilirsiniz: Exchange ile konuşan kodun tamamı
[sahte sunucu](#test-yazma) ile test edilir. Arayüz değişikliklerini ise Windows'ta çalıştırarak denemeniz gerekir.

Önerilen editör ayarları:

- [`.editorconfig`](.editorconfig) Visual Studio, Rider ve VS Code (C# Dev Kit) tarafından otomatik okunur.
- Dosyalar UTF-8 ve 4 boşluk girintili; satır sonları Git tarafından [`.gitattributes`](.gitattributes) ile
  düzenlenir (YAML, Python ve kabuk betikleri her zaman LF).

## Derleme ve test

```powershell
# Bağımlılıkları indir ve kütüphaneleri derle (her işletim sistemi)
dotnet build src/ExchangeMail.Core
dotnet build src/ExchangeMail.ViewModels

# Testleri çalıştır (her işletim sistemi)
dotnet test --project tests/ExchangeMail.Core.Tests

# Tek bir test sınıfını veya testi çalıştır
dotnet test --project tests/ExchangeMail.Core.Tests --filter-class "*FeatureTests"
dotnet test --project tests/ExchangeMail.Core.Tests --filter-method "*Saving_an_existing_draft*"

# Uygulamayı derle ve çalıştır (yalnızca Windows)
dotnet build src/ExchangeMail.App -c Debug -p:Platform=x64
./src/ExchangeMail.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Postaci.exe
```

Notlar:

- Testler **Microsoft.Testing.Platform** üzerinde çalışır ([`global.json`](global.json) bunu seçer). Bu yüzden
  `dotnet test` komutuna klasör değil `--project` ile proje verilir; filtreler xUnit v3 seçenekleriyle
  (`--filter-class`, `--filter-method`, `--filter-trait`) yazılır.
- Linux/macOS'ta `dotnet build ExchangeMail.slnx` çalıştırmayın: WinUI projesinin XAML derleyicisi yalnızca
  Windows'ta çalışır. Kütüphaneleri ve test projesini ayrı ayrı derleyin.
- Uygulama paketlenmemiş (unpackaged) ve self-contained derlenir; MSIX veya Windows App SDK runtime kurulumu
  gerekmez.

## Depo yapısı

```text
.
├── src/
│   ├── ExchangeMail.Core/          # EWS istemcisi ve modeller (UI yok, platformdan bağımsız)
│   │   ├── Ews/                    # EwsClient (partial), Autodiscover, Xml yardımcıları, hatalar
│   │   └── Models/                 # Mail, Calendar, Contact, AutoReply kayıtları
│   ├── ExchangeMail.ViewModels/    # MVVM katmanı (CommunityToolkit.Mvvm), ayarlar, biçimlendirme
│   └── ExchangeMail.App/           # WinUI 3 uygulaması (yalnızca Windows)
│       ├── Views/                  # Sayfalar, pencereler, diyaloglar (XAML + code-behind)
│       ├── Controls/               # HtmlEditor, WeekGridView, AvailabilityView
│       ├── Services/               # Kimlik bilgisi, bildirim, tepsi, başlangıç, yazı tipleri
│       ├── Helpers/                # Ui, HtmlViewer
│       └── Assets/                 # Postaci.ico, Postaci-unread.ico, PNG'ler
├── tests/ExchangeMail.Core.Tests/  # xUnit v3 testleri + FakeEwsServer
├── assets/icon/                    # İkon kaynakları ve üretim betikleri
└── .github/                        # CI, sürüm, Dependabot, issue/PR şablonları
```

> Proje klasörleri ve ad alanları tarihsel olarak `ExchangeMail` adını taşır; ürün adı, çalıştırılabilir dosya ve
> veri klasörü `Postacı` / `Postaci`'dir. Ürün adını tek yerden almak için `AppInfo.Name` kullanın.

## Katman kuralları

Bağımlılıklar tek yönlüdür: **App → ViewModels → Core**.

| Katman | Ne içerir | Ne içermez |
|---|---|---|
| **Core** | EWS SOAP istekleri, yanıt ayrıştırma, modeller, Autodiscover | UI, dosya sistemi ayarları, Windows API'leri, `ViewModels` referansı |
| **ViewModels** | Ekran durumu, komutlar, ayarlar (`UserPreferences`), HTML yardımcıları (`MailFormat`) | WinUI tipleri (`Microsoft.UI.*`), `DispatcherQueue`, P/Invoke |
| **App** | XAML, görsel davranış, Windows servisleri (Credential Manager, bildirim, tepsi, kayıt defteri) | İş mantığı, doğrudan EWS çağrısı |

Pratikte:

- Platforma özgü bir şeye ViewModel'den ihtiyaç varsa ViewModels'te bir arayüz tanımlayın (örnek:
  `ICredentialStore`, `IStartupService`) ve uygulamasını App'te yazın. Böylece ViewModel testlerde sahte
  uygulamayla çalışır.
- ViewModel'ler `Exception` mesajlarını kullanıcıya `ErrorMessage` / `StatusText` gibi özelliklerle verir;
  hata yutmayın. Kullanıcının bilmesi gerekmeyen hataları (ör. parola kaydedilemedi) yutacaksanız nedenini yorumla
  belirtin.
- **Kullanıcıya görünen metinler Türkçe**, tanımlayıcılar (sınıf, metot, değişken adları) ve kod yorumları
  **İngilizce** yazılır. Türkçe karakterleri (ç, ğ, ı, İ, ö, ş, ü) arayüz metinlerinde doğru kullanın.

## Kod stili

Biçim kuralları [`.editorconfig`](.editorconfig) içindedir; derleme uyarı vermeden geçmelidir. Bunlara ek olarak:

- **Nullable açık**, implicit usings açık, `LangVersion` en günceli ([`Directory.Build.props`](Directory.Build.props)).
  `!` (null-forgiving) yerine mümkünse açık kontrol yazın.
- **File-scoped namespace** (`namespace X;`), private alanlar `_camelCase`, sabitler ve `static readonly` alanlar
  `PascalCase`.
- Async metotlar `Async` ile biter ve son parametre olarak `CancellationToken ct = default` alır; token'ı
  `ExecuteAsync`'e kadar iletin.
- MVVM için CommunityToolkit.Mvvm kaynak üreticilerini kullanın:

  ```csharp
  public partial class ExampleViewModel : ObservableObject
  {
      [ObservableProperty]
      [NotifyPropertyChangedFor(nameof(CanSave))]
      private string _subject = "";

      public bool CanSave => Subject.Length > 0;

      [RelayCommand]
      private async Task SaveAsync(CancellationToken ct) { /* ... */ }
  }
  ```

- Yorumlar "ne" değil **"neden"** anlatsın. Özellikle Exchange'in alışılmadık davranışlarına dayanan her satıra kısa
  bir yorum ekleyin (ör. *"Exchange requires a change key unless ConflictResolution is AlwaysOverwrite"*).
  Bir sonraki kişi o satırı "gereksiz" sanıp silmesin.
- Yeni NuGet bağımlılığı eklemeden önce issue'da konuşun. Eklenen paketler Dependabot gruplarına
  ([`dependabot.yml`](.github/dependabot.yml)) uygun düşmelidir.

## Yeni bir EWS işlemi eklemek

Postacı, EWS Managed API yerine SOAP isteklerini `System.Xml.Linq` ile kendisi kurar. Böylece bağımlılık yoktur ve
istek biçimi testlerde birebir doğrulanabilir. Yeni bir işlem şu adımlarla eklenir.

### 1. Doğru dosyayı seçin

`EwsClient` bir `partial` sınıftır:

| Dosya | Konu |
|---|---|
| `EwsClient.cs` | Klasörler, posta listeleme/okuma, okundu, silme, taşıma, ekler, gönderme, taslak, ad çözümleme, SOAP altyapısı |
| `EwsClient.Calendar.cs` | Randevu listeleme/okuma/oluşturma/güncelleme/silme, toplantı yanıtı, uygunluk |
| `EwsClient.Contacts.cs` | Kişiler, dizin araması |
| `EwsClient.AutoReply.cs` | Otomatik yanıt (Out of Office) |

Yeni bir alan (ör. görevler) ekliyorsanız `EwsClient.Tasks.cs` gibi yeni bir partial dosya açın ve modelleri
`Models/` altına koyun.

### 2. İsteği kurun

`Xml` yardımcılarını `using static ExchangeMail.Core.Ews.Xml;` ile kullanın: `M(...)` mesaj ad alanında,
`T(...)` tip ad alanında öğe üretir; `Field`, `ItemIdElement`, `FolderIdElement`, `DistinguishedFolder`,
`MailboxElement`, `ParseItemId` tekrar eden parçaları kapsar (tarih biçimi için `EwsClient` içindeki `EwsDate`).

Örnek (bir iletiye takip bayrağı koymak — projede henüz olmayan, açıklama amaçlı bir işlem):

```csharp
/// <summary>Sets or clears the follow-up flag and returns the item's new id.</summary>
public async Task<EwsItemId> SetFlagAsync(EwsItemId id, bool flagged, CancellationToken ct = default)
{
    // Without a change key Exchange only accepts AlwaysOverwrite; a flag change never needs merging.
    var request = M("UpdateItem",
        new XAttribute("MessageDisposition", "SaveOnly"),
        new XAttribute("ConflictResolution", "AlwaysOverwrite"),
        M("ItemChanges", T("ItemChange",
            ItemIdElement(id with { ChangeKey = null }),
            T("Updates", T("SetItemField", Field("item:Flag"),
                T("Message", T("Flag", T("FlagStatus", flagged ? "Flagged" : "NotFlagged"))))))));

    var message = Single(await ExecuteAsync(request, ct));
    return ParseItemId(message.Element(MNs + "Items")?.Elements().FirstOrDefault()?.Element(TNs + "ItemId"));
}
```

Dikkat edilecekler:

- **Şema sırası önemlidir.** EWS, öğeleri XSD'deki sırayla bekler; sıra bozulursa `ErrorSchemaValidation` döner.
  Bir öğeye alan eklerken [EWS XML başvurusundaki](https://learn.microsoft.com/exchange/client-developer/web-service-reference/ews-xml-elements-in-exchange)
  sırayı izleyin (ör. `CalendarItem` içinde `Subject` → `Body` → … → `Start` → `End` → … → `Location`).
- **ChangeKey.** `UpdateItem` isteğinde ChangeKey göndermiyorsanız `ConflictResolution="AlwaysOverwrite"` zorunludur;
  `AutoResolve` bu durumda hata verir. ChangeKey her güncellemede değişir: işlem yeni kimliği döndürüyorsa
  çağıran tarafın sakladığı kimliği güncelleyin.
- **Okundu bilgisi.** Okundu bayrağını değiştiren isteklerde `SuppressReadReceipts="true"` kullanın; aksi halde
  kullanıcı istemeden okundu bilgisi gönderebilir.
- **Saat dilimi.** Takvim isteklerinde `ExecuteAsync(request, ct, withTimeZone: true)` çağırın; `TimeZoneContext`
  başlığı yalnızca o zaman eklenir. Tüm gün etkinliklerinde tarihi `LocalDateTime.Date` ile alın, UTC değerini
  doğrudan kullanmayın.
- **Yinelenen toplantılar.** Takvim görünümünde `FindItem` + `CalendarView` kullanın; yinelemeler sunucuda açılır.
- **Hata işleme.** Yanıtı her zaman `Single(response)` veya `ResponseMessages(response).Select(Ok)` ile işleyin;
  böylece `ResponseClass="Error"` olan iletiler `EwsException`'a dönüşür. Bazı işlemler (ör. OOF) tek bir
  `ResponseMessage` döndürür; onlar için ayrı yardımcı yazın (`EnsureOofSuccess` gibi).
- **Yoğun sunucu.** `ErrorServerBusy` otomatik olarak geri çekilme süresine uyularak yeniden denenir; ayrıca
  döngü yazmayın.
- **Sürüm.** İstekler `Exchange2013_SP1` şemasıyla gönderilir. Daha yeni şema gerektiren bir öğe kullanmayın;
  Exchange 2016/2019/SE'nin hepsinde çalışmalıdır.
- **Kullanıcıya gidecek hata metinleri** Türkçe olmalıdır (`throw new EwsException("Yanıtta ... yok.")`).

### 3. Yanıtı modele çevirin

Modeller `Models/` altında değişmez `record` tipleridir. Ayrıştırırken eksik öğelere dayanıklı olun
(`(string?)element ?? ""`, `(bool?)element ?? false`); sunucular bazı alanları hiç göndermeyebilir.

### 4. Test yazın

Her yeni işlem için en az bir [sahte sunucu testi](#test-yazma) ekleyin. Test hem **isteğin biçimini** (doğru
öğeler, öznitelikler, sıra) hem de **yanıtın doğru ayrıştırıldığını** doğrulamalıdır. Hata yanıtı için de bir test
ekleyin.

### 5. ViewModel ve arayüz

İşlemi ilgili ViewModel'e komut olarak bağlayın, ardından App'te düğmesini/menüsünü ekleyin. ViewModel davranışını
da test edin (ör. işlem sonrası listenin güncellenmesi, hata mesajının gösterilmesi).

### 6. Gerçek sunucuda deneme

Mümkünse değişikliği bir test posta kutusunda deneyin ve PR açıklamasına Exchange sürümünü yazın. Gerçek posta
kutusundan alınan hiçbir veriyi depoya eklemeyin.

## Arayüz (WinUI) geliştirme

- **XAML yalnızca Windows'ta derlenir.** Linux/macOS'ta çalışıyorsanız XAML değişiklikleriniz ancak CI'daki
  Windows işinde derlenir. PR'ınızdaki `app` işinin yeşil olduğundan emin olun ve mümkünse bir Windows makinede
  çalıştırarak deneyin.
- Bağlamalarda `{Binding}` yerine **`x:Bind`** kullanın (derleme zamanında denetlenir). Değişen değerler için
  `Mode=OneWay` eklemeyi unutmayın. `x:Bind` ifadelerinde karmaşık mantık yerine ViewModel'de hesaplanmış bir
  özellik veya `Helpers/Ui` içinde bir yardımcı fonksiyon kullanın.
- Sayfalar `NavigationCacheMode="Required"` ile önbelleğe alınır ve ViewModel'lerini `Frame.Navigate` parametresi
  olarak alır. Sayfaya yeniden dönüldüğünde `OnNavigatedTo` tekrar çalışır; olay aboneliklerini çift eklemeyin.
- **Tema.** Her değişikliği hem **açık** hem **koyu** temada kontrol edin. Sabit renk yerine `ThemeResource`
  fırçaları (`TextFillColorSecondaryBrush`, `CardBackgroundFillColorDefaultBrush` vb.) kullanın.
- **Yazı tipleri.** Arayüz yazı tipi ve boyutu Ayarlar'dan değiştirilebilir. Kodda sabit `FontSize` vermeniz
  gerekiyorsa `Ui.TextScale` ile çarpın; XAML'de varsayılan metin stillerini (`BodyTextBlockStyle`,
  `CaptionTextBlockStyle`…) kullanın, bunlar başlangıçta kullanıcının ayarlarına göre yeniden tanımlanır.
- **Erişilebilirlik.** Yalnızca ikon içeren düğmelere `ToolTipService.ToolTip` ve
  `AutomationProperties.Name` verin. Klavye ile ulaşılabilirliği kontrol edin; yeni kısayolları README'deki
  kısayol tablosuna ekleyin. Kısayolu olabildiğince dar kapsamlı bağlayın (ör. Delete tuşu yalnızca listede).
- **ContentDialog.** Diyaloglar `XamlRoot` ve `RequestedTheme` atanarak açılır (`Helpers/Ui` içindeki
  `Dialogs.ConfirmAsync`'e bakın). Aynı anda tek diyalog açılabilir.
- **WebView2 güvenliği.** Posta içeriği güvenilmeyen veridir:
  - Okuma bölmesinde betikler **kapalı** kalmalı ve CSP korunmalıdır. Dış görseller yalnızca kullanıcı izin
    verdiğinde yüklenir.
  - Yazma düzenleyicisinde (`HtmlEditor`) sayfa betikleri kapalıdır; araç çubuğu `ExecuteScriptAsync` ile çalışır.
    Komutlara kullanıcı girdisi eklerken **her zaman** `JsonSerializer.Serialize` ile kaçışlayın.
  - Bağlantılar WebView2 içinde açılmaz; varsayılan tarayıcıya yönlendirilir. Yalnızca `http`, `https`, `mailto`
    şemalarına izin verilir.
  - Host object, web message, devtools ve otomatik doldurma kapalı kalmalıdır.
- **Kimlik bilgileri** yalnızca Windows Kimlik Bilgisi Yöneticisi'nde (`CredentialManagerStore`) tutulur; ayar
  dosyasına, günlüğe veya hata mesajına parola yazmayın.

## Test yazma

Testler `tests/ExchangeMail.Core.Tests` altındadır (xUnit v3). **Testler hiçbir zaman gerçek bir sunucuya
bağlanmaz**; CI'da ağ erişimi gerekmez.

### FakeEwsServer

`FakeEwsServer` bir `HttpMessageHandler`'dır. Her SOAP işlemine sırayla verilecek yanıtları kuyruğa alır ve gelen
istekleri kaydeder:

```csharp
[Fact]
public async Task Flagging_an_item_overwrites_without_change_key()
{
    var server = new FakeEwsServer()
        .On("UpdateItem", Ok("UpdateItem", """<m:Items><t:Message><t:ItemId Id="A1" ChangeKey="k2" /></t:Message></m:Items>"""));
    using var client = server.CreateClient();

    var id = await client.SetFlagAsync(new EwsItemId("A1", "k1"), flagged: true, TestContext.Current.CancellationToken);

    Assert.Equal(new EwsItemId("A1", "k2"), id);
    var request = server.Request("UpdateItem");
    Assert.Equal("AlwaysOverwrite", (string?)request.Attribute("ConflictResolution"));
    Assert.Null(request.Descendants(T + "ItemId").Single().Attribute("ChangeKey"));
    Assert.Equal("Flagged", request.Descendants(T + "FlagStatus").Single().Value);
}
```

Kullanışlı üyeler:

| Üye | Ne işe yarar |
|---|---|
| `On(operation, bodyXml)` | İşlem için başarılı bir SOAP gövdesi kuyruğa ekler (`m:` / `t:` önekleriyle) |
| `OnRaw(operation, factory)` | HTTP 401, SOAP fault veya bozuk yanıt gibi özel durumlar için |
| `Requests` / `Request(op)` | Gönderilen işlem öğeleri (`Request` tam olarak bir tane bekler) |
| `Operations` | İşlem adlarının sırası (ör. `["UpdateItem", "DeleteAttachment", "GetItem"]`) |
| `Headers` | SOAP başlıkları (ör. `TimeZoneContext` yalnızca takvim isteklerinde mi?) |
| `Envelope`, `Xml` | Elle yanıt üretmek için |

Kuyrukta yanıtı olmayan bir işlem çağrılırsa test `No canned response for ...` hatasıyla düşer; bu, beklenmeyen bir
isteğin gönderildiğini gösterir.

### ViewModel testleri

ViewModel'ler `MailSession` üzerinden sahte sunucuyla oluşturulur
(`new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" })`). Arka planda yüklenen
veriler için sabit `Task.Delay` yerine koşul bekleyin:

```csharp
await WaitUntil(() => vm.Messages.Count == 2 && !vm.IsLoadingList);
```

### İyi bir test

- Adı davranışı anlatır: `Saving_an_existing_draft_overwrites_it_and_removes_attachments`.
- Tek bir davranışı doğrular; hem istek biçimini hem sonucu kontrol eder.
- Uydurma veri kullanır (`sirket.com`, `A1`, `k1`), Türkçe karakterli metinleri de dener (ör. `"Güncel"`).
- `TestContext.Current.CancellationToken` geçirir.
- Saat dilimine, kültüre ve işletim sistemine bağlı değildir (CI hem Ubuntu hem Windows'ta çalışır).

## Commit ve dal kuralları

- `main` her zaman derlenebilir ve testleri geçer durumda olmalıdır. Doğrudan `main`'e push etmeyin; dal açın.
- Dal adı: `ozellik/kisa-aciklama`, `duzeltme/kisa-aciklama`, `belge/...`, `bakim/...`
  (ör. `duzeltme/okundu-isareti`).
- Commit mesajı: ilk satır en fazla ~72 karakter, emir kipinde ve ne yaptığını anlatır; gerekiyorsa boş satırdan
  sonra nedenini açıklayın. Türkçe veya İngilizce olabilir; bir PR içinde tutarlı olun.

  ```text
  Takvim: tüm gün etkinliklerini yerel tarihle göster

  Exchange tüm gün etkinliklerini UTC gece yarısı olarak döndürüyor; UTC+3'te
  bir önceki güne kayıyordu. LocalDateTime.Date kullanılıyor.

  Fixes #42
  ```

- Bir commit bir mantıksal değişiklik içersin. Biçimlendirme değişikliklerini işlev değişiklikleriyle karıştırmayın.

## Pull request süreci

1. Depoyu fork'layın (veya yazma izniniz varsa dal açın) ve `main`'den güncel bir dal oluşturun.
2. Değişikliği yapın, test ekleyin, `dotnet test --project tests/ExchangeMail.Core.Tests` ile yerelde çalıştırın.
3. Kullanıcıya görünen bir değişiklikse README'yi (özellikler, kısayollar, sorun giderme) güncelleyin.
4. PR'ı açın; [şablondaki](.github/pull_request_template.md) bölümleri doldurun. Arayüz değişikliklerinde açık ve
   koyu temada ekran görüntüsü ekleyin (gerçek veri olmadan!).
5. CI'ın iki işi de yeşil olmalıdır:
   - **test** — Ubuntu ve Windows'ta kütüphaneler + testler,
   - **app** — Windows'ta uygulamanın x64 ve ARM64 için yayımlanması (XAML burada derlenir).

   PR'ın "Checks" sekmesinden `Postaci-win-x64` artifact'ini indirip değişikliği deneyebilirsiniz.
6. İnceleme yorumlarını yeni commit'lerle karşılayın (force-push yerine); birleştirirken bakımcı commit'leri
   gerekirse birleştirir (squash).

### İnceleme kontrol listesi

- [ ] Katman kuralları korunmuş (Core/ViewModels WinUI'ye bağımlı değil)
- [ ] Yeni EWS istekleri şema sırasına uyuyor ve testle doğrulanıyor
- [ ] Hatalar kullanıcıya Türkçe ve anlaşılır biçimde ulaşıyor; yutulan hata yok
- [ ] Uzun süren işlemler UI iş parçacığını kilitlemiyor, iptal edilebiliyor
- [ ] Açık/koyu tema ve büyütülmüş yazı boyutu ile arayüz bozulmuyor
- [ ] WebView2 güvenlik ayarlarına dokunulmamış (veya dokunulduysa gerekçesi yazılmış)
- [ ] Gerçek kişi, adres, sunucu adı veya posta içeriği yok
- [ ] Yapay zekâ kullanıldıysa PR'da belirtilmiş ve kod yazar tarafından anlaşılmış/denenmiş

## Sürüm yayınlama (bakımcılar için)

Sürümler [SemVer](https://semver.org/lang/tr/) izler: hata düzeltmesi `1.0.1`, geriye uyumlu yeni özellik
`1.1.0`, ayar/veri biçimini bozan değişiklik `2.0.0`.

```bash
git switch main && git pull
git tag v1.1.0          # ön sürüm için: v1.1.0-beta.1
git push origin v1.1.0
```

[`release.yml`](.github/workflows/release.yml) testleri çalıştırır, sürüm numarasını etiketten alarak x64 ve
ARM64 zip'lerini üretir, `SHA256SUMS.txt` hesaplar ve sürüm notlarını PR başlıklarından oluşturarak GitHub sürümünü
yayınlar. Etikette `-` varsa sürüm "pre-release" olarak işaretlenir. Projedeki `Version` değeri yalnızca yerel
derlemelerde kullanılır.

Yayından önce:

- [ ] `main` üzerindeki son CI çalışması yeşil
- [ ] En az bir Windows makinede `Postaci-win-x64` artifact'i ile giriş, posta okuma, gönderme ve takvim denendi
- [ ] Ayar dosyası biçimi değiştiyse eski ayarlarla açılış denendi (geçiş kodu var mı?)

## İkonları yeniden üretmek

İkon kaynakları ve betikleri [`assets/icon`](assets/icon) altındadır:

```bash
cd assets/icon
python3 make_svg.py   # SVG'leri üretir (büyük/küçük boyut, okunmamış rozetli)
node render.js        # Playwright Chromium ile 16–256 px PNG'ler
python3 make_ico.py   # Postaci.ico ve Postaci-unread.ico
```

Çıktıları `src/ExchangeMail.App/Assets/` altına kopyalayın ve `onizleme.png`'yi güncelleyin. ICO dosyaları ≤128 px
için BMP, 256 px için PNG kayıtları içerir; Windows'un tepsi ve görev çubuğunda keskin görünmesi için bu düzeni
koruyun.

## Sık karşılaşılan sorunlar

| Belirti | Neden / çözüm |
|---|---|
| `dotnet test` "VSTest is not supported" benzeri hata veriyor | Klasör yerine `--project tests/ExchangeMail.Core.Tests` verin; `global.json`'u silmeyin |
| Linux'ta çözüm derlenmiyor (XAML hatası) | Beklenen durum; yalnızca Core, ViewModels ve test projesini derleyin |
| `ErrorSchemaValidation` | İstekteki öğe sırası veya ad alanı (`m:`/`t:`) yanlış; XSD sırasını kontrol edin |
| `ErrorChangeKeyRequiredForWriteOperations` | ChangeKey göndermeyen `UpdateItem`'da `ConflictResolution="AlwaysOverwrite"` eksik |
| Tüm gün etkinliği bir gün kayıyor | UTC tarih kullanılmış; `LocalDateTime.Date` kullanın ve takvim isteğinde `withTimeZone: true` verin |
| Test `No canned response for X` ile düşüyor | Kod beklenmeyen bir `X` isteği gönderiyor ya da `On("X", ...)` eksik |
| Uygulama açılışta WebView2 hatası veriyor | WebView2 Runtime kurulu değil; [Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) kurun |

Takıldığınız bir yer olursa issue açmaktan çekinmeyin. Katkılarınız için teşekkürler!
