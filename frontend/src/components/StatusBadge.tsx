import { ReceiptStatus, STATUS_LABELS } from '../api/types';

const STATUS_CLASS: Record<ReceiptStatus, string> = {
  [ReceiptStatus.Uploaded]: 'badge badge-neutral',
  [ReceiptStatus.Processing]: 'badge badge-info',
  [ReceiptStatus.PendingReview]: 'badge badge-warning',
  [ReceiptStatus.Approved]: 'badge badge-success',
  [ReceiptStatus.Rejected]: 'badge badge-danger',
  [ReceiptStatus.Failed]: 'badge badge-danger',
};

export function StatusBadge({ status }: { status: ReceiptStatus }) {
  return <span className={STATUS_CLASS[status]}>{STATUS_LABELS[status]}</span>;
}
