# Postacı ikonu / Postacı icon

Kaynaklar ve üretim betikleri. Çıktılar `src/ExchangeMail.App/Assets/` altına kopyalanır.
Sources and generator scripts. Outputs are copied to `src/ExchangeMail.App/Assets/`.

```bash
python3 make_svg.py          # postaci*.svg (büyük ve küçük boyut çizimleri, okunmamış rozetli sürüm)
                             # (large and small size drawings, variant with unread badge)
node render.js               # Chromium (Playwright) ile 16–256 px PNG'ler / 16–256 px PNGs via Chromium (Playwright)
python3 make_ico.py          # Postaci.ico, Postaci-unread.ico (≤128 px BMP, 256 px PNG)
```

16–32 px için çizim sadeleştirilmiştir (hareket çizgileri yok, zarf daha büyük, okunmamış noktası daha iri).
For 16–32 px the drawing is simplified (no motion lines, larger envelope, bigger unread dot).

`onizleme.png` tüm boyutların önizlemesidir. / `onizleme.png` is a preview of all sizes.
