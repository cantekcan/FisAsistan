import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import * as api from '../api/receiptsApi';
import { ReceiptStatus, STATUS_LABELS, type ReceiptListItemDto } from '../api/types';
import { StatusBadge } from '../components/StatusBadge';

const FILTER_TABS: { label: string; status: ReceiptStatus | undefined }[] = [
  { label: 'Tümü', status: undefined },
  { label: STATUS_LABELS[ReceiptStatus.PendingReview], status: ReceiptStatus.PendingReview },
  { label: STATUS_LABELS[ReceiptStatus.Approved], status: ReceiptStatus.Approved },
  { label: STATUS_LABELS[ReceiptStatus.Rejected], status: ReceiptStatus.Rejected },
];

export function DashboardPage() {
  const [receipts, setReceipts] = useState<ReceiptListItemDto[]>([]);
  const [statusFilter, setStatusFilter] = useState<ReceiptStatus | undefined>(undefined);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [exporting, setExporting] = useState(false);

  async function load() {
    setLoading(true);
    try {
      const data = await api.listReceipts({ status: statusFilter, search: search || undefined });
      setReceipts(data);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter]);

  function handleSearchSubmit(e: React.FormEvent) {
    e.preventDefault();
    load();
  }

  async function handleExport(format: 'csv' | 'xlsx') {
    setExporting(true);
    try {
      await api.downloadExport(format, statusFilter);
    } finally {
      setExporting(false);
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1>Fişler</h1>
        <div className="page-header-actions">
          <button className="btn btn-secondary" disabled={exporting} onClick={() => handleExport('csv')}>
            CSV İndir
          </button>
          <button className="btn btn-secondary" disabled={exporting} onClick={() => handleExport('xlsx')}>
            Excel İndir
          </button>
          <Link className="btn btn-secondary" to="/batch-upload">
            + Çoklu Fiş Yükle
          </Link>
          <Link className="btn btn-primary" to="/upload">
            + Fiş Yükle
          </Link>
        </div>
      </div>

      <div className="tabs">
        {FILTER_TABS.map((tab) => (
          <button
            key={tab.label}
            className={statusFilter === tab.status ? 'tab active' : 'tab'}
            onClick={() => setStatusFilter(tab.status)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <form className="search-bar" onSubmit={handleSearchSubmit}>
        <input
          placeholder="Dosya adı veya OCR metninde ara…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <button className="btn btn-secondary" type="submit">
          Ara
        </button>
      </form>

      {loading ? (
        <p className="muted">Yükleniyor…</p>
      ) : receipts.length === 0 ? (
        <p className="muted">Bu kritere uyan fiş bulunamadı.</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Dosya</th>
              <th>Satıcı</th>
              <th>Tutar</th>
              <th>Durum</th>
              <th>Uyarı</th>
              <th>Yüklenme</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {receipts.map((r) => (
              <tr key={r.id}>
                <td>{r.originalFileName}</td>
                <td>{r.merchantName ?? <span className="muted">—</span>}</td>
                <td>
                  {r.totalAmount ? `${r.totalAmount} ${r.currency ?? ''}` : <span className="muted">—</span>}
                </td>
                <td>
                  <StatusBadge status={r.status} />
                </td>
                <td>
                  {r.openIssueCount > 0 ? (
                    <span className="badge badge-warning">{r.openIssueCount} uyarı</span>
                  ) : (
                    <span className="muted">—</span>
                  )}
                </td>
                <td className="muted">{new Date(r.uploadedAtUtc).toLocaleString('tr-TR')}</td>
                <td>
                  <Link className="btn btn-ghost btn-sm" to={`/receipts/${r.id}`}>
                    Görüntüle
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
