import { apiClient } from './client';
import type { ReceiptBatchDetailDto, ReceiptBatchRegionDto } from './types';

export async function uploadBatch(file: File, onProgress?: (percent: number) => void) {
  const formData = new FormData();
  formData.append('file', file);
  const { data } = await apiClient.post<ReceiptBatchDetailDto>('/api/receipt-batches/upload', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: (evt) => {
      if (onProgress && evt.total) {
        onProgress(Math.round((evt.loaded / evt.total) * 100));
      }
    },
  });
  return data;
}

export async function listBatches() {
  const { data } = await apiClient.get<ReceiptBatchDetailDto[]>('/api/receipt-batches');
  return data;
}

export async function getBatch(id: string) {
  const { data } = await apiClient.get<ReceiptBatchDetailDto>(`/api/receipt-batches/${id}`);
  return data;
}

export async function updateBatchRegions(id: string, regions: ReceiptBatchRegionDto[]) {
  const { data } = await apiClient.put<ReceiptBatchDetailDto>(`/api/receipt-batches/${id}/regions`, { regions });
  return data;
}

export async function processBatch(id: string) {
  const { data } = await apiClient.post<ReceiptBatchDetailDto>(`/api/receipt-batches/${id}/process`);
  return data;
}

export async function deleteBatch(id: string) {
  await apiClient.delete(`/api/receipt-batches/${id}`);
}

/// Backend orijinal fotoğraf/önizleme endpoint'leri JWT'yi yalnızca Authorization header'ından
/// kabul ettiği için doğrudan <img src="..."> kullanılamaz; blob olarak indirip nesne URL'sine çeviriyoruz
/// (bkz. receiptsApi.ts'teki fetchReceiptImageBlobUrl ile aynı yaklaşım).
export async function fetchBatchOriginalImageBlobUrl(id: string) {
  const { data } = await apiClient.get(`/api/receipt-batches/${id}/original-image`, { responseType: 'blob' });
  return URL.createObjectURL(data as Blob);
}

export async function fetchRegionPreviewBlobUrl(batchId: string, regionIndex: number) {
  const { data } = await apiClient.get(`/api/receipt-batches/${batchId}/regions/${regionIndex}/preview`, {
    responseType: 'blob',
  });
  return URL.createObjectURL(data as Blob);
}
