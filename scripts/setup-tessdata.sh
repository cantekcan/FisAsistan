#!/usr/bin/env bash
# Tesseract icin Turkce (tur) ve Ingilizce (eng) egitim verisini resmi, acik kaynak
# tesseract-ocr/tessdata deposundan indirir. Yerel gelistirme icin bir kez calistirin.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET_DIR="$SCRIPT_DIR/../src/FisAsistan.Api/tessdata"
mkdir -p "$TARGET_DIR"

for f in tur.traineddata eng.traineddata; do
  dest="$TARGET_DIR/$f"
  if [ -f "$dest" ]; then
    echo "$f zaten mevcut, atlaniyor."
    continue
  fi
  url="https://github.com/tesseract-ocr/tessdata/raw/main/$f"
  echo "Indiriliyor: $url"
  curl -L -o "$dest" "$url"
done

echo "Tamamlandi. tessdata klasoru: $TARGET_DIR"
