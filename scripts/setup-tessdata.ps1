# Tesseract icin Turkce (tur) ve Ingilizce (eng) egitim verisini resmi, acik kaynak
# tesseract-ocr/tessdata deposundan indirir. Yerel gelistirme icin bir kez calistirin.
$ErrorActionPreference = "Stop"

$targetDir = Join-Path $PSScriptRoot "..\src\FisAsistan.Api\tessdata"
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$files = @("tur.traineddata", "eng.traineddata")
foreach ($file in $files) {
    $dest = Join-Path $targetDir $file
    if (Test-Path $dest) {
        Write-Host "$file zaten mevcut, atlaniyor."
        continue
    }
    $url = "https://github.com/tesseract-ocr/tessdata/raw/main/$file"
    Write-Host "Indiriliyor: $url"
    Invoke-WebRequest -Uri $url -OutFile $dest
}

Write-Host "Tamamlandi. tessdata klasoru: $targetDir"
