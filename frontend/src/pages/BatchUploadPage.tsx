import { useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import axios from 'axios';
import * as batchesApi from '../api/batchesApi';
import { ReceiptBatchStatus, BATCH_STATUS_LABELS, type ReceiptBatchDetailDto, type ReceiptBatchRegionDto } from '../api/types';
import { BatchRegionEditor } from '../components/BatchRegionEditor';
import { StatusBadge } from '../components/StatusBadge';

const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/bmp'];
const MAX_SIZE_BYTES = 10 * 1024 * 1024;

type UiPhase = 'idle' | 'uploading' | 'detecting' | 'reviewing' | 'processing' | 'done' | 'error';

export function BatchUploadPage() {
  const navigate = useNavigate();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [phase, setPhase] = useState<UiPhase>('idle');
  const [error, setError] = useState<string | null>(null);
  const [batch, setBatch] = useState<ReceiptBatchDetailDto | null>(null);
  const [regions, setRegions] = useState<ReceiptBatchRegionDto[]>([]);
  const [saving, setSaving] = useState(false);

  function validate(file: File): string | null {
    if (!ALLOWED_TYPES.includes(file.type)) {
      return 'Desteklenmeyen dosya türü. Yalnızca JPG, PNG veya BMP yükleyebilirsiniz.';
    }
    if (file.size > MAX_SIZE_BYTES) {
      return 'Dosya boyutu 10 MB sınırını aşıyor.';
    }
    return null;
  }

  async function handleFile(file: File) {
    const validationError = validate(file);
    if (validationError) {
      setError(validationError);
      setPhase('error');
      return;
    }

    setError(null);
    setPhase('uploading');

    try {
      const result = await batchesApi.uploadBatch(file);
      setBatch(result);

      if (result.status === ReceiptBatchStatus.RegionsProposed) {
        setRegions(result.pendingRegions);
        setPhase('reviewing');
      } else {
        setPhase('error');
        setError(result.message ?? 'Fişler otomatik olarak tespit edilemedi.');
      }
    } catch (err) {
      setPhase('error');
      setError(axios.isAxiosError(err) ? err.response?.data?.message ?? 'Yükleme sırasında bir hata oluştu.' : 'Yükleme sırasında bir hata oluştu.');
    }
  }

  async function handleSaveRegions() {
    if (!batch) return;
    setSaving(true);
    try {
      const updated = await batchesApi.updateBatchRegions(batch.id, regions);
      setBatch(updated);
      setRegions(updated.pendingRegions);
    } catch (err) {
      setError(axios.isAxiosError(err) ? err.response?.data?.message ?? 'Bölgeler kaydedilemedi.' : 'Bölgeler kaydedilemedi.');
    } finally {
      setSaving(false);
    }
  }

  async function handleProcessAll() {
    if (!batch) return;
    setError(null);
    setSaving(true);
    try {
      // Önce güncel bölge listesini kaydet, sonra işle.
      await batchesApi.updateBatchRegions(batch.id, regions);
      setPhase('processing');
      const processed = await batchesApi.processBatch(batch.id);
      setBatch(processed);
      setPhase('done');
    } catch (err) {
      setPhase('reviewing');
      setError(axios.isAxiosError(err) ? err.response?.data?.message ?? 'İşleme sırasında bir hata oluştu.' : 'İşleme sırasında bir hata oluştu.');
    } finally {
      setSaving(false);
    }
  }

  function handleRetry() {
    setBatch(null);
    setRegions([]);
    setError(null);
    setPhase('idle');
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1>Çoklu Fiş Yükle</h1>
      </div>

      {phase === 'idle' && (
        <>
          <div className="tips-box">
            <strong>Daha iyi sonuç için:</strong>
            <ul>
              <li>Fişleri üst üste koymayın.</li>
              <li>Fişler arasında boşluk bırakın.</li>
              <li>Koyu ve düz bir zemin kullanın (fişler zeminle kontrastlı olmalı).</li>
              <li>Fotoğrafı mümkün olduğunca tepeden (dik açıyla) çekin.</li>
              <li>Fişlerin tamamının görüntü içinde olduğundan emin olun.</li>
            </ul>
            <p className="muted">
              Tespit tamamen yerel görüntü işleme (OpenCV) ile yapılır — hiçbir yapay zeka/bulut
              servisi kullanılmaz. Bu nedenle sonuç kesin değildir; bir sonraki adımda mutlaka
              gözden geçirip onaylamanız gerekir.
            </p>
          </div>

          <div className="dropzone" onClick={() => fileInputRef.current?.click()}>
            <p className="dropzone-icon">🧾🧾🧾</p>
            <p>Birden fazla fiş içeren fotoğrafı seçin</p>
            <p className="muted">JPG, PNG veya BMP — en fazla 10 MB</p>
            <input
              ref={fileInputRef}
              type="file"
              accept="image/jpeg,image/png,image/bmp"
              hidden
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) handleFile(file);
              }}
            />
          </div>
        </>
      )}

      {phase === 'uploading' && (
        <div className="status-panel">
          <p>Fotoğraf yükleniyor ve fişler tespit ediliyor…</p>
          <div className="spinner" />
        </div>
      )}

      {phase === 'error' && (
        <div className="status-panel">
          <div className="alert alert-danger">{error}</div>
          <div className="detail-actions">
            <button className="btn btn-secondary" onClick={handleRetry}>
              Tekrar Dene
            </button>
            <button className="btn btn-primary" onClick={() => navigate('/upload')}>
              Tek Fiş Olarak Devam Et
            </button>
          </div>
        </div>
      )}

      {phase === 'reviewing' && batch && (
        <div>
          <p>
            <strong>{regions.length} fiş tespit edildi.</strong> Lütfen gözden geçirin — yanlış
            algılanan bölgeleri silin, eksik olanları elle çizin.
          </p>
          {error && <div className="alert alert-danger">{error}</div>}
          <BatchRegionEditor batchId={batch.id} regions={regions} onChange={setRegions} />
          <div className="detail-actions">
            <button className="btn btn-secondary" disabled={saving} onClick={handleSaveRegions}>
              Değişiklikleri Kaydet
            </button>
            <button className="btn btn-primary" disabled={saving || regions.length === 0} onClick={handleProcessAll}>
              Tümünü İşle ({regions.length} fiş)
            </button>
            <button className="btn btn-ghost" onClick={handleRetry}>
              İptal / Yeni Fotoğraf
            </button>
          </div>
        </div>
      )}

      {phase === 'processing' && (
        <div className="status-panel">
          <p>{regions.length} fiş işleniyor (OCR + alan çıkarımı)… Bu biraz sürebilir.</p>
          <div className="spinner" />
        </div>
      )}

      {phase === 'done' && batch && (
        <div>
          <div className="alert alert-info">
            {BATCH_STATUS_LABELS[batch.status]} — {batch.message}
          </div>
          <table className="table">
            <thead>
              <tr>
                <th>Dosya</th>
                <th>Satıcı</th>
                <th>Tutar</th>
                <th>Durum</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {batch.receipts.map((r) => (
                <tr key={r.id}>
                  <td>{r.originalFileName}</td>
                  <td>{r.merchantName ?? <span className="muted">—</span>}</td>
                  <td>{r.totalAmount ? `${r.totalAmount} ${r.currency ?? ''}` : <span className="muted">—</span>}</td>
                  <td>
                    <StatusBadge status={r.status} />
                  </td>
                  <td>
                    <a className="btn btn-ghost btn-sm" href={`/receipts/${r.id}`}>
                      Görüntüle
                    </a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="detail-actions">
            <button className="btn btn-primary" onClick={() => navigate('/')}>
              Panele Dön
            </button>
            <button className="btn btn-secondary" onClick={handleRetry}>
              Yeni Toplu Yükleme
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
