# Postacı ikonu

Kaynaklar ve üretim betikleri. Çıktılar `src/ExchangeMail.App/Assets/` altına kopyalanır.

```bash
python3 make_svg.py          # postaci*.svg (büyük ve küçük boyut çizimleri, okunmamış rozetli sürüm)
node render.js               # Chromium (Playwright) ile 16–256 px PNG'ler
python3 make_ico.py          # Postaci.ico, Postaci-unread.ico (≤128 px BMP, 256 px PNG)
```

16–32 px için çizim sadeleştirilmiştir (hareket çizgileri yok, zarf daha büyük, okunmamış noktası daha iri).
