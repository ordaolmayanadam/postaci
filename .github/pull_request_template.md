## Ne değişti?

<!-- Değişikliği ve nedenini kısaca anlatın. İlgili issue: #... -->

## Nasıl test edildi?

- [ ] `dotnet test --project tests/ExchangeMail.Core.Tests` geçiyor
- [ ] Yeni/değişen EWS işlemleri için sahte sunucu (`FakeEwsServer`) testi eklendi
- [ ] Arayüz değişikliği Windows'ta çalıştırılarak denendi (açık ve koyu tema)
- [ ] Gerçek bir Exchange sunucusunda denendi (sürüm: ...)

## Kontrol listesi

- [ ] Katman kuralları korundu (Core ve ViewModels WinUI'ye bağımlı değil)
- [ ] Kullanıcıya görünen metinler Türkçe
- [ ] README / CONTRIBUTING gerekiyorsa güncellendi
- [ ] Ekran görüntülerinde ve test verilerinde gerçek kişi, adres veya posta içeriği yok
