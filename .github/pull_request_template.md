<!-- Türkçe veya İngilizce yazabilirsiniz. / You may write in English or Turkish. -->

## Ne değişti? / What changed?

<!-- Değişikliği ve nedenini kısaca anlatın. İlgili issue: #...
     Briefly describe the change and why. Related issue: #... -->

## Nasıl test edildi? / How was it tested?

- [ ] `dotnet test --project tests/ExchangeMail.Core.Tests` geçiyor / passes
- [ ] Yeni/değişen EWS işlemleri için sahte sunucu (`FakeEwsServer`) testi eklendi / Fake server (`FakeEwsServer`) tests added for new/changed EWS operations
- [ ] Arayüz değişikliği Windows'ta çalıştırılarak denendi (açık ve koyu tema) / UI change tried by running on Windows (light and dark theme)
- [ ] Gerçek bir Exchange sunucusunda denendi (sürüm: ...) / Tried on a real Exchange server (version: ...)

## Yapay zekâ kullanımı / AI usage

<!-- Yapay zekâ araçlarıyla hazırlanan katkılar kabul edilir; yalnızca belirtin.
     Contributions prepared with AI tools are welcome; just disclose it. -->

- [ ] Kullanılmadı / Not used
- [ ] Kullanıldı — araç: ..., hangi kısımlar için: ... / Used — tool: ..., for which parts: ...
- [ ] Yapay zekânın ürettiği her satırı okudum, anladım ve denedim / I read, understood and tested every AI-generated line

## Kontrol listesi / Checklist

- [ ] Katman kuralları korundu (Core ve ViewModels WinUI'ye bağımlı değil) / Layering rules respected (Core and ViewModels don't depend on WinUI)
- [ ] Kullanıcıya görünen metinler Türkçe / User-facing text is in Turkish
- [ ] README / CONTRIBUTING gerekiyorsa güncellendi / README / CONTRIBUTING updated if needed
- [ ] Eklenen kod kendi yazdığım ya da MIT ile uyumlu lisanslı (kaynağı belirtildi) / Added code is my own or MIT-compatible (source named)
- [ ] Ekran görüntülerinde ve test verilerinde gerçek kişi, adres veya posta içeriği yok / No real people, addresses or mail content in screenshots or test data
