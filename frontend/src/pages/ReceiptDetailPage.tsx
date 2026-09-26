import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import axios from 'axios';
import * as api from '../api/receiptsApi';
import {
  FIELD_LABELS,
  IssueSeverity,
  ReceiptFieldName,
  ReceiptStatus,
  ValueSource,
  type ReceiptDetailDto,
  type ReceiptVatLineDto,
} from '../api/types';
import { StatusBadge } from '../components/StatusBadge';

const EDITABLE_FIELD_ORDER: ReceiptFieldName[] = [
  ReceiptFieldName.MerchantName,
  ReceiptFieldName.MerchantAddress,
  ReceiptFieldName.TaxId,
  ReceiptFieldName.ReceiptDate,
  ReceiptFieldName.ReceiptTime,
  ReceiptFieldName.DocumentNumber,
  ReceiptFieldName.SubTotal,
  ReceiptFieldName.TotalAmount,
  ReceiptFieldName.Currency,
  ReceiptFieldName.PaymentMethod,
];

export function ReceiptDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [receipt, setReceipt] = useState<ReceiptDetailDto | null>(null);
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [fieldValues, setFieldValues] = useState<Record<number, string>>({});
  const [vatLines, setVatLines] = useState<ReceiptVatLineDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState('');
  const [showRejectBox, setShowRejectBox] = useState(false);

  async function load() {
    if (!id) return;
    setLoading(true);
    try {
      const data = await api.getReceipt(id);
      setReceipt(data);
      const values: Record<number, string> = {};
      data.fields.forEach((f) => {
        values[f.fieldName] = f.currentValue ?? '';
      });
      setFieldValues(values);
      setVatLines(data.vatLines);

      const url = await api.fetchReceiptImageBlobUrl(id);
      setImageUrl(url);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  if (loading) {
    return <p className="muted page">Yükleniyor…</p>;
  }

  if (!receipt) {
    return <p className="page">Fiş bulunamadı.</p>;
  }

  const isReadOnly = receipt.status === ReceiptStatus.Approved || receipt.status === ReceiptStatus.Rejected;

  function fieldFor(name: ReceiptFieldName) {
    return receipt!.fields.find((f) => f.fieldName === name);
  }

  function updateVatLine(index: number, patch: Partial<ReceiptVatLineDto>) {
    setVatLines((prev) => prev.map((v, i) => (i === index ? { ...v, ...patch } : v)));
  }

  function addVatLine() {
    setVatLines((prev) => [
      ...prev,
      { id: `new-${Date.now()}`, ratePercent: 0, baseAmount: null, vatAmount: 0, source: ValueSource.User },
    ]);
  }

  function removeVatLine(index: number) {
    setVatLines((prev) => prev.filter((_, i) => i !== index));
  }

  async function handleSave(markConfirmed: boolean) {
    if (!id) return;
    setSaving(true);
    setError(null);
    try {
      const fields = EDITABLE_FIELD_ORDER.map((name) => ({
        fieldName: name,
        value: fieldValues[name]?.trim() ? fieldValues[name] : null,
        isConfirmed: markConfirmed,
      }));

      const vatPayload = vatLines.map((v) => ({
        id: v.id.startsWith('new-') ? undefined : v.id,
        ratePercent: Number(v.ratePercent),
        baseAmount: v.baseAmount === null ? null : Number(v.baseAmount),
        vatAmount: Number(v.vatAmount),
      }));

      const updated = await api.updateReceipt(id, fields, vatPayload);
      setReceipt(updated);
      const values: Record<number, string> = {};
      updated.fields.forEach((f) => {
        values[f.fieldName] = f.currentValue ?? '';
      });
      setFieldValues(values);
      setVatLines(updated.vatLines);
    } catch (err) {
      setError(extractError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleApprove() {
    if (!id) return;
    setSaving(true);
    setError(null);
    try {
      await handleSave(true);
      const updated = await api.approveReceipt(id);
      setReceipt(updated);
    } catch (err) {
      setError(extractError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleReject() {
    if (!id || !rejectReason.trim()) return;
    setSaving(true);
    setError(null);
    try {
      const updated = await api.rejectReceipt(id, rejectReason.trim());
      setReceipt(updated);
      setShowRejectBox(false);
    } catch (err) {
      setError(extractError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete() {
    if (!id) return;
    if (!window.confirm('Bu fişi kalıcı olarak silmek istediğinize emin misiniz?')) return;
    await api.deleteReceipt(id);
    navigate('/');
  }

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1>{receipt.originalFileName}</h1>
          <StatusBadge status={receipt.status} />
          {receipt.ocrAverageConfidence !== null && (
            <span className="muted" style={{ marginLeft: 12 }}>
              OCR güveni: %{receipt.ocrAverageConfidence.toFixed(0)}
            </span>
          )}
        </div>
        <div className="page-header-actions">
          <button className="btn btn-ghost" onClick={() => navigate('/')}>
            ← Listeye Dön
          </button>
          <button className="btn btn-danger" onClick={handleDelete}>
            Sil
          </button>
        </div>
      </div>

      {receipt.rejectionReason && (
        <div className="alert alert-danger">Reddedilme sebebi: {receipt.rejectionReason}</div>
      )}

      {receipt.validationIssues.length > 0 && (
        <div className="issues-panel">
          {receipt.validationIssues.map((issue, i) => (
            <div
              key={i}
              className={`alert ${issue.severity === IssueSeverity.Error ? 'alert-danger' : issue.severity === IssueSeverity.Warning ? 'alert-warning' : 'alert-info'}`}
            >
              {issue.fieldName !== null ? `${FIELD_LABELS[issue.fieldName]}: ` : ''}
              {issue.message}
            </div>
          ))}
        </div>
      )}

      <div className="detail-grid">
        <div className="detail-image-panel">
          {imageUrl && <img src={imageUrl} alt="Fiş görseli" className="receipt-image" />}
        </div>

        <div className="detail-form-panel">
          <h2>Çıkarılan Alanlar</h2>
          <p className="muted">
            OCR/parser tarafından bulunan değerler otomatik dolduruldu. Lütfen görselle karşılaştırıp
            gerekirse düzeltin ve onaylayın.
          </p>

          <div className="field-grid">
            {EDITABLE_FIELD_ORDER.map((name) => {
              const field = fieldFor(name);
              const isFromParser = field?.source === ValueSource.Parser;
              const currentlyEmpty = !fieldValues[name]?.trim();
              const notFound = !field?.parserValue && currentlyEmpty;
              return (
                <label className="field" key={name}>
                  <span className="field-label">
                    {FIELD_LABELS[name]}
                    {isFromParser && field?.currentValue && (
                      <span className="badge badge-neutral badge-xs" title={field.parserExplanation ?? ''}>
                        OCR
                      </span>
                    )}
                    {!isFromParser && (
                      <span className="badge badge-info badge-xs">Kullanıcı</span>
                    )}
                    {notFound && <span className="badge badge-warning badge-xs">Bulunamadı</span>}
                  </span>
                  <input
                    value={fieldValues[name] ?? ''}
                    disabled={isReadOnly}
                    onChange={(e) => setFieldValues((prev) => ({ ...prev, [name]: e.target.value }))}
                    placeholder={notFound ? 'Manuel giriniz' : ''}
                  />
                  {field?.parserExplanation && (
                    <span className="field-hint" title={field.parserExplanation}>
                      ℹ️ {field.parserExplanation}
                    </span>
                  )}
                </label>
              );
            })}
          </div>

          <h3>KDV Satırları</h3>
          <table className="table table-compact">
            <thead>
              <tr>
                <th>Oran (%)</th>
                <th>Matrah</th>
                <th>KDV Tutarı</th>
                <th>Kaynak</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {vatLines.map((v, i) => (
                <tr key={v.id}>
                  <td>
                    <input
                      type="number"
                      value={v.ratePercent}
                      disabled={isReadOnly}
                      onChange={(e) => updateVatLine(i, { ratePercent: Number(e.target.value) })}
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      value={v.baseAmount ?? ''}
                      disabled={isReadOnly}
                      onChange={(e) =>
                        updateVatLine(i, { baseAmount: e.target.value === '' ? null : Number(e.target.value) })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      value={v.vatAmount}
                      disabled={isReadOnly}
                      onChange={(e) => updateVatLine(i, { vatAmount: Number(e.target.value) })}
                    />
                  </td>
                  <td className="muted">{v.source === ValueSource.Parser ? 'OCR' : 'Kullanıcı'}</td>
                  <td>
                    {!isReadOnly && (
                      <button className="btn btn-ghost btn-sm" onClick={() => removeVatLine(i)}>
                        Sil
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!isReadOnly && (
            <button className="btn btn-ghost btn-sm" onClick={addVatLine}>
              + KDV satırı ekle
            </button>
          )}

          {receipt.rawOcrText && (
            <details className="raw-text-details">
              <summary>Ham OCR metni</summary>
              <pre>{receipt.rawOcrText}</pre>
            </details>
          )}

          {error && <div className="alert alert-danger">{error}</div>}

          {!isReadOnly && (
            <div className="detail-actions">
              <button className="btn btn-secondary" disabled={saving} onClick={() => handleSave(false)}>
                Taslak Olarak Kaydet
              </button>
              <button className="btn btn-primary" disabled={saving} onClick={handleApprove}>
                Onayla
              </button>
              <button className="btn btn-danger" disabled={saving} onClick={() => setShowRejectBox((s) => !s)}>
                Reddet
              </button>
            </div>
          )}

          {showRejectBox && (
            <div className="reject-box">
              <textarea
                placeholder="Reddetme sebebini yazın…"
                value={rejectReason}
                onChange={(e) => setRejectReason(e.target.value)}
              />
              <button className="btn btn-danger" disabled={saving || !rejectReason.trim()} onClick={handleReject}>
                Reddi Onayla
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function extractError(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data;
    if (typeof data?.message === 'string') return data.message;
    if (data?.issues) return (data.issues as string[]).join(', ');
    if (data?.errors) return JSON.stringify(data.errors);
  }
  return 'Bir hata oluştu.';
}
