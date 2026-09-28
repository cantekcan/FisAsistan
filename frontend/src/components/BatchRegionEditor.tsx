import { useEffect, useRef, useState } from 'react';
import type { ReceiptBatchRegionDto } from '../api/types';
import { fetchBatchOriginalImageBlobUrl } from '../api/batchesApi';

interface Props {
  batchId: string;
  regions: ReceiptBatchRegionDto[];
  onChange: (regions: ReceiptBatchRegionDto[]) => void;
}

/// <summary>
/// Basit bir dikdörtgen/crop editörü — Photoshop benzeri bir şey DEĞİL, MVP için yeterli:
/// orijinal fotoğraf üzerinde algoritmanın önerdiği bölgeler kutu olarak gösterilir,
/// kullanıcı bir kutuya tıklayıp silebilir veya "Yeni Bölge Ekle" ile sürükleyerek
/// yeni bir dikdörtgen çizebilir (eksene hizalı).
/// </summary>
export function BatchRegionEditor({ batchId, regions, onChange }: Props) {
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [naturalSize, setNaturalSize] = useState<{ w: number; h: number } | null>(null);
  const [displaySize, setDisplaySize] = useState<{ w: number; h: number } | null>(null);
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const [drawMode, setDrawMode] = useState(false);
  const [dragStart, setDragStart] = useState<{ x: number; y: number } | null>(null);
  const [dragCurrent, setDragCurrent] = useState<{ x: number; y: number } | null>(null);

  const imgRef = useRef<HTMLImageElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let cancelled = false;
    fetchBatchOriginalImageBlobUrl(batchId).then((url) => {
      if (!cancelled) setImageUrl(url);
    });
    return () => {
      cancelled = true;
    };
  }, [batchId]);

  // ResizeObserver kullanılır çünkü <img>'in onLoad olayı bazen tarayıcı layout'u
  // (clientWidth/clientHeight) henüz kesinleşmeden tetiklenebiliyor — bu durumda
  // 0x0 boyut okunup üstteki bölge kutuları yanlış (0,0) konumunda çiziliyordu.
  useEffect(() => {
    const img = imgRef.current;
    if (!img || !imageUrl) return;

    const updateSizes = () => {
      if (img.naturalWidth > 0 && img.clientWidth > 0) {
        setNaturalSize({ w: img.naturalWidth, h: img.naturalHeight });
        setDisplaySize({ w: img.clientWidth, h: img.clientHeight });
      }
    };

    if (img.complete) {
      updateSizes();
    }

    const resizeObserver = new ResizeObserver(updateSizes);
    resizeObserver.observe(img);
    img.addEventListener('load', updateSizes);

    return () => {
      resizeObserver.disconnect();
      img.removeEventListener('load', updateSizes);
    };
  }, [imageUrl]);

  function boundingBoxOf(region: ReceiptBatchRegionDto) {
    const xs = region.corners.map((c) => c.x);
    const ys = region.corners.map((c) => c.y);
    return { minX: Math.min(...xs), minY: Math.min(...ys), maxX: Math.max(...xs), maxY: Math.max(...ys) };
  }

  function toDisplayStyle(minX: number, minY: number, maxX: number, maxY: number) {
    if (!naturalSize || !displaySize) return {};
    const scaleX = displaySize.w / naturalSize.w;
    const scaleY = displaySize.h / naturalSize.h;
    return {
      left: minX * scaleX,
      top: minY * scaleY,
      width: (maxX - minX) * scaleX,
      height: (maxY - minY) * scaleY,
    };
  }

  function removeRegion(index: number) {
    onChange(regions.filter((r) => r.index !== index).map((r, i) => ({ ...r, index: i })));
    setSelectedIndex(null);
  }

  function getRelativePoint(e: React.MouseEvent) {
    const rect = containerRef.current!.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  }

  function handleMouseDown(e: React.MouseEvent) {
    if (!drawMode) return;
    const p = getRelativePoint(e);
    setDragStart(p);
    setDragCurrent(p);
  }

  function handleMouseMove(e: React.MouseEvent) {
    if (!drawMode || !dragStart) return;
    setDragCurrent(getRelativePoint(e));
  }

  function handleMouseUp() {
    if (!drawMode || !dragStart || !dragCurrent || !naturalSize || !displaySize) {
      setDragStart(null);
      setDragCurrent(null);
      return;
    }

    const scaleX = naturalSize.w / displaySize.w;
    const scaleY = naturalSize.h / displaySize.h;

    const x1 = Math.min(dragStart.x, dragCurrent.x) * scaleX;
    const y1 = Math.min(dragStart.y, dragCurrent.y) * scaleY;
    const x2 = Math.max(dragStart.x, dragCurrent.x) * scaleX;
    const y2 = Math.max(dragStart.y, dragCurrent.y) * scaleY;

    setDragStart(null);
    setDragCurrent(null);
    setDrawMode(false);

    if (x2 - x1 < 15 || y2 - y1 < 15) {
      return; // çok küçük, muhtemelen yanlışlıkla tıklama
    }

    const newRegion: ReceiptBatchRegionDto = {
      index: regions.length,
      corners: [
        { x: x1, y: y1 },
        { x: x2, y: y1 },
        { x: x2, y: y2 },
        { x: x1, y: y2 },
      ],
      isUserModified: true,
    };

    onChange([...regions, newRegion]);
  }

  return (
    <div className="batch-editor">
      <div className="batch-editor-toolbar">
        <button
          className={drawMode ? 'btn btn-primary btn-sm' : 'btn btn-secondary btn-sm'}
          onClick={() => setDrawMode((d) => !d)}
        >
          {drawMode ? 'Çizim modu açık — sürükleyip bırakın' : '+ Yeni Bölge Ekle'}
        </button>
        {selectedIndex !== null && (
          <button className="btn btn-danger btn-sm" onClick={() => removeRegion(selectedIndex)}>
            Seçili Bölgeyi Sil
          </button>
        )}
        <span className="muted">{regions.length} bölge</span>
      </div>

      <div
        className="batch-editor-canvas"
        ref={containerRef}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        style={{ cursor: drawMode ? 'crosshair' : 'default' }}
      >
        {imageUrl && (
          <img ref={imgRef} src={imageUrl} alt="Orijinal fotoğraf" draggable={false} />
        )}

        {regions.map((region) => {
          const box = boundingBoxOf(region);
          const style = toDisplayStyle(box.minX, box.minY, box.maxX, box.maxY);
          const isSelected = selectedIndex === region.index;
          return (
            <div
              key={region.index}
              className={isSelected ? 'batch-region-box selected' : 'batch-region-box'}
              style={style}
              onClick={(e) => {
                e.stopPropagation();
                setSelectedIndex(region.index);
              }}
            >
              <span className="batch-region-label">
                {region.index + 1}
                {region.isUserModified ? ' (elle)' : ''}
              </span>
            </div>
          );
        })}

        {drawMode && dragStart && dragCurrent && (
          <div
            className="batch-region-box drawing"
            style={{
              left: Math.min(dragStart.x, dragCurrent.x),
              top: Math.min(dragStart.y, dragCurrent.y),
              width: Math.abs(dragCurrent.x - dragStart.x),
              height: Math.abs(dragCurrent.y - dragStart.y),
            }}
          />
        )}
      </div>
    </div>
  );
}
