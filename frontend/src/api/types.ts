export enum ReceiptFieldName {
  MerchantName = 0,
  MerchantAddress = 1,
  TaxId = 2,
  ReceiptDate = 3,
  ReceiptTime = 4,
  DocumentNumber = 5,
  SubTotal = 6,
  TotalAmount = 7,
  Currency = 8,
  PaymentMethod = 9,
}

export enum ReceiptStatus {
  Uploaded = 0,
  Processing = 1,
  PendingReview = 2,
  Approved = 3,
  Rejected = 4,
  Failed = 5,
}

export enum IssueSeverity {
  Info = 0,
  Warning = 1,
  Error = 2,
}

export enum ValueSource {
  Parser = 0,
  User = 1,
}

export const FIELD_LABELS: Record<ReceiptFieldName, string> = {
  [ReceiptFieldName.MerchantName]: 'Satıcı Adı',
  [ReceiptFieldName.MerchantAddress]: 'Satıcı Adresi',
  [ReceiptFieldName.TaxId]: 'VKN / TCKN',
  [ReceiptFieldName.ReceiptDate]: 'Fiş Tarihi',
  [ReceiptFieldName.ReceiptTime]: 'Fiş Saati',
  [ReceiptFieldName.DocumentNumber]: 'Belge / Fiş No',
  [ReceiptFieldName.SubTotal]: 'Ara Toplam',
  [ReceiptFieldName.TotalAmount]: 'Genel Toplam',
  [ReceiptFieldName.Currency]: 'Para Birimi',
  [ReceiptFieldName.PaymentMethod]: 'Ödeme Yöntemi',
};

export const STATUS_LABELS: Record<ReceiptStatus, string> = {
  [ReceiptStatus.Uploaded]: 'Yüklendi',
  [ReceiptStatus.Processing]: 'İşleniyor',
  [ReceiptStatus.PendingReview]: 'Onay Bekliyor',
  [ReceiptStatus.Approved]: 'Onaylandı',
  [ReceiptStatus.Rejected]: 'Reddedildi',
  [ReceiptStatus.Failed]: 'İşlenemedi',
};

export interface ReceiptFieldDto {
  fieldName: ReceiptFieldName;
  parserValue: string | null;
  currentValue: string | null;
  source: ValueSource;
  parserExplanation: string | null;
  isConfirmed: boolean;
}

export interface ReceiptVatLineDto {
  id: string;
  ratePercent: number;
  baseAmount: number | null;
  vatAmount: number;
  source: ValueSource;
}

export interface ReceiptValidationIssueDto {
  fieldName: ReceiptFieldName | null;
  severity: IssueSeverity;
  message: string;
  ruleCode: string;
  isResolved: boolean;
}

export interface ReceiptListItemDto {
  id: string;
  originalFileName: string;
  status: ReceiptStatus;
  uploadedAtUtc: string;
  merchantName: string | null;
  totalAmount: string | null;
  currency: string | null;
  openIssueCount: number;
}

export interface ReceiptDetailDto {
  id: string;
  originalFileName: string;
  contentType: string;
  fileSizeBytes: number;
  status: ReceiptStatus;
  uploadedAtUtc: string;
  processedAtUtc: string | null;
  rawOcrText: string | null;
  ocrAverageConfidence: number | null;
  rejectionReason: string | null;
  fields: ReceiptFieldDto[];
  vatLines: ReceiptVatLineDto[];
  validationIssues: ReceiptValidationIssueDto[];
}

export interface AuthResponse {
  token: string;
  email: string;
  fullName: string;
  expiresAtUtc: string;
}
