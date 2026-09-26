import { apiClient } from './client';
import type {
  AuthResponse,
  ReceiptDetailDto,
  ReceiptListItemDto,
  ReceiptFieldName,
  ReceiptStatus,
} from './types';

export async function register(email: string, password: string, fullName: string) {
  const { data } = await apiClient.post<AuthResponse>('/api/auth/register', { email, password, fullName });
  return data;
}

export async function login(email: string, password: string) {
  const { data } = await apiClient.post<AuthResponse>('/api/auth/login', { email, password });
  return data;
}

export async function uploadReceipt(file: File, onProgress?: (percent: number) => void) {
  const formData = new FormData();
  formData.append('file', file);
  const { data } = await apiClient.post<ReceiptDetailDto>('/api/receipts/upload', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: (evt) => {
      if (onProgress && evt.total) {
        onProgress(Math.round((evt.loaded / evt.total) * 100));
      }
    },
  });
  return data;
}

export interface ReceiptListFilters {
  status?: ReceiptStatus;
  search?: string;
}

export async function listReceipts(filters: ReceiptListFilters) {
  const { data } = await apiClient.get<ReceiptListItemDto[]>('/api/receipts', { params: filters });
  return data;
}

export async function getReceipt(id: string) {
  const { data } = await apiClient.get<ReceiptDetailDto>(`/api/receipts/${id}`);
  return data;
}

export interface UpdateFieldItem {
  fieldName: ReceiptFieldName;
  value: string | null;
  isConfirmed: boolean;
}

export interface UpdateVatLineItem {
  id?: string;
  ratePercent: number;
  baseAmount: number | null;
  vatAmount: number;
}

export async function updateReceipt(id: string, fields: UpdateFieldItem[], vatLines: UpdateVatLineItem[]) {
  const { data } = await apiClient.put<ReceiptDetailDto>(`/api/receipts/${id}`, { fields, vatLines });
  return data;
}

export async function approveReceipt(id: string) {
  const { data } = await apiClient.post<ReceiptDetailDto>(`/api/receipts/${id}/approve`);
  return data;
}

export async function rejectReceipt(id: string, reason: string) {
  const { data } = await apiClient.post<ReceiptDetailDto>(`/api/receipts/${id}/reject`, { reason });
  return data;
}

export async function deleteReceipt(id: string) {
  await apiClient.delete(`/api/receipts/${id}`);
}

/// Backend görsel endpoint'i JWT'yi yalnızca Authorization header'ından kabul ettiği için
/// doğrudan <img src="..."> kullanılamaz; blob olarak indirip nesne URL'sine çeviriyoruz.
export async function fetchReceiptImageBlobUrl(id: string) {
  const { data } = await apiClient.get(`/api/receipts/${id}/image`, { responseType: 'blob' });
  return URL.createObjectURL(data as Blob);
}

export function exportUrl(format: 'csv' | 'xlsx', status?: ReceiptStatus) {
  const params = new URLSearchParams();
  if (status !== undefined) {
    params.set('status', String(status));
  }
  return `/api/receipts/export/${format}?${params.toString()}`;
}

export async function downloadExport(format: 'csv' | 'xlsx', status?: ReceiptStatus) {
  const response = await apiClient.get(exportUrl(format, status), { responseType: 'blob' });
  const blob = new Blob([response.data]);
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `fisler.${format}`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
