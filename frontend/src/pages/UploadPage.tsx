import { useRef, useState, type DragEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import axios from 'axios';
import * as api from '../api/receiptsApi';

const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/bmp'];
const MAX_SIZE_BYTES = 10 * 1024 * 1024;

type UiState = 'idle' | 'uploading' | 'processing' | 'error';

export function UploadPage() {
  const navigate = useNavigate();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [state, setState] = useState<UiState>('idle');
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState(false);

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
      setState('error');
      return;
    }

    setError(null);
    setPreviewUrl(URL.createObjectURL(file));
    setState('uploading');
    setProgress(0);

    try {
      const receipt = await api.uploadReceipt(file, (p) => {
        setProgress(p);
        if (p >= 100) {
          setState('processing');
        }
      });
      navigate(`/receipts/${receipt.id}`);
    } catch (err) {
      setState('error');
      if (axios.isAxiosError(err)) {
        setError(err.response?.data?.message ?? 'Yükleme sırasında bir hata oluştu.');
      } else {
        setError('Yükleme sırasında bir hata oluştu.');
      }
    }
  }

  function onDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (file) {
      handleFile(file);
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1>Fiş Yükle</h1>
      </div>

      <div
        className={dragOver ? 'dropzone dropzone-active' : 'dropzone'}
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={onDrop}
        onClick={() => fileInputRef.current?.click()}
      >
        {previewUrl ? (
          <img src={previewUrl} alt="Önizleme" className="dropzone-preview" />
        ) : (
          <>
            <p className="dropzone-icon">📷</p>
            <p>Fiş fotoğrafını sürükleyip bırakın veya tıklayıp seçin</p>
            <p className="muted">JPG, PNG veya BMP — en fazla 10 MB</p>
          </>
        )}
        <input
          ref={fileInputRef}
          type="file"
          accept="image/jpeg,image/png,image/bmp"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) {
              handleFile(file);
            }
          }}
        />
      </div>

      {state === 'uploading' && (
        <div className="status-panel">
          <p>Yükleniyor… %{progress}</p>
          <div className="progress-bar">
            <div className="progress-bar-fill" style={{ width: `${progress}%` }} />
          </div>
        </div>
      )}

      {state === 'processing' && (
        <div className="status-panel">
          <p>🔎 OCR ile fiş okunuyor ve alanlar çıkarılıyor… Bu birkaç saniye sürebilir.</p>
          <div className="spinner" />
        </div>
      )}

      {state === 'error' && error && <div className="alert alert-danger">{error}</div>}
    </div>
  );
}
