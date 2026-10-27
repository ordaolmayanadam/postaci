# Gizlilik ilkesi

**Türkçe** · [English](PRIVACY.en.md)

Son güncelleme: 28 Ekim 2026

Postacı, şirket içi (on-premises) Exchange Server posta kutularına bağlanan bağımsız ve açık kaynaklı bir Windows
uygulamasıdır. Bu ilke, uygulamanın hangi verileri işlediğini ve nerede tuttuğunu açıklar.

## Kısaca

- Postacı **hiçbir kişisel veriyi toplamaz, uygulamanın geliştiricisine ya da üçüncü kişilere göndermez.**
- **Telemetri, reklam, analiz ya da kullanım istatistiği yoktur.**
- Posta, takvim ve kişi verileriniz yalnızca sizin yapılandırdığınız Exchange sunucusu ile bilgisayarınız arasında
  aktarılır.

## İşlenen veriler

Uygulama, çalışabilmek için şu verileri **yalnızca bilgisayarınızda** işler:

| Veri | Nerede tutulur |
|---|---|
| Hesap ayarları (e-posta adresi, kullanıcı adı, EWS adresi) | Uygulamanın veri klasörü (`settings.json`) |
| Tercihler (tema, dil, sütun genişlikleri, imza vb.) | Uygulamanın veri klasörü (`preferences.json`) |
| Parola | Windows Kimlik Bilgisi Yöneticisi (DPAPI ile korunur); diske düz metin yazılmaz |
| İleti gövdelerini göstermek için tarayıcı profili | Uygulamanın veri klasörü (`WebView2`) |
| Okuma bölmesi geçici dosyaları | `%TEMP%\Postaci` (her açılışta temizlenir) |
| Hata günlüğü | Uygulamanın veri klasörü (`postaci.log`); siz paylaşmadıkça bilgisayarınızdan çıkmaz |

Posta, takvim ve kişi içerikleri Exchange sunucusundan gerektiğinde okunur; uygulama bunların kalıcı bir kopyasını
tutmaz.

Uygulamayı kaldırdığınızda Windows, uygulamanın veri klasörünü siler. Parolayı Windows Kimlik Bilgisi Yöneticisi'nden
(Genel kimlik bilgileri → `Postaci:<kullanıcı>`) ya da uygulamada oturumu kapatarak silebilirsiniz.

## Ağ bağlantıları

Postacı yalnızca şu bağlantıları kurar:

- **Yapılandırdığınız Exchange sunucusu:** posta, takvim ve kişi işlemleri (Exchange Web Services). İlk kurulumda
  sunucu adresi, e-posta adresinizin etki alanındaki Autodiscover adreslerinden bulunabilir.
- **İleti içindeki uzak resimler:** varsayılan olarak engellidir. Bir ileti için resimlere izin verirseniz, resimler
  iletiyi gönderenin belirttiği sunuculardan indirilir. Bu, gönderenin iletiyi açtığınızı öğrenmesine yol açabilir.
- **Bağlantılar:** iletilerdeki bağlantılar varsayılan tarayıcınızda açılır; uygulamanın içinde açılmaz.

İleti gövdeleri, Windows'un bir bileşeni olan Microsoft Edge WebView2 ile gösterilir. WebView2'nin Microsoft'a
gönderebileceği tanılama verileri, Windows ve Microsoft Edge ayarlarınıza bağlıdır ve Microsoft'un gizlilik
bildirimine tabidir. Microsoft Store üzerinden yükleme ve güncellemeler de Microsoft'un koşullarına tabidir.

## Bildirimler ve başlangıç

Yeni posta bildirimleri Windows bildirim sistemiyle yerel olarak gösterilir; bildirim içeriği (gönderen ve konu)
bilgisayarınızdan çıkmaz. Windows açılışında başlatma, yalnızca siz açarsanız etkinleşir.

## Çocuklar

Postacı bir iş uygulamasıdır ve çocuklara yönelik değildir; çocuklardan bilerek veri toplamaz.

## Değişiklikler

Bu ilke değişirse güncel sürümü bu sayfada yayımlanır. Değişikliklerin geçmişi deponun commit geçmişinde görülebilir.

## İletişim

Gizlilikle ilgili sorularınızı deponun [Issues](https://github.com/ordaolmayanadam/postaci/issues) sayfasından
sorabilirsiniz. Güvenlik açıkları için [güvenlik politikasına](SECURITY.md) bakın.
