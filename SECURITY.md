# Güvenlik politikası

## Güvenlik açığı bildirme

Güvenlik açıklarını **herkese açık issue olarak açmayın.** Bunun yerine deponun **Security → Report a vulnerability**
sayfasından (GitHub'ın gizli güvenlik açığı bildirimi) bildirin. Bu özellik depo ayarlarında
*Settings → Code security → Private vulnerability reporting* altından açılmalıdır.

Bildirimde şunları belirtin:

- Etkilenen sürüm veya commit
- Açığın etkisi (ör. betik çalıştırma, kimlik bilgisinin sızması, kimlik doğrulamasının atlanması)
- Yeniden oluşturma adımları; gerçek posta verisi veya kimlik bilgisi **içermeden**

## Kapsam

Özellikle şu alanlardaki bulgular önemlidir:

- Okuma bölmesinde (WebView2) ileti içeriğinden betik çalışması veya Content-Security-Policy'nin aşılması
- Parolanın Windows Kimlik Bilgisi Yöneticisi dışında bir yere yazılması veya sızması
- TLS sertifika doğrulamasının devre dışı kalması
- Ek indirirken dosya adı üzerinden yol aşımı (path traversal)

## Desteklenen sürümler

Yalnızca en son yayımlanan sürüm güvenlik düzeltmesi alır.
