namespace FisAsistan.Domain.Enums;

public enum UserRole
{
    Accountant = 0,
    Admin = 1
}

public enum ReceiptStatus
{
    Uploaded = 0,
    Processing = 1,
    PendingReview = 2,
    Approved = 3,
    Rejected = 4,
    Failed = 5
}

/// <summary>
/// Fişten çıkarılabilecek alan tipleri. Her alan ayrı bir ReceiptField satırı olarak saklanır.
/// </summary>
public enum ReceiptFieldName
{
    MerchantName = 0,
    MerchantAddress = 1,
    TaxId = 2,
    ReceiptDate = 3,
    ReceiptTime = 4,
    DocumentNumber = 5,
    SubTotal = 6,
    TotalAmount = 7,
    Currency = 8,
    PaymentMethod = 9
}

public enum IssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>
/// Bir değerin kaynağı: parser tarafından mı çıkarıldı, yoksa kullanıcı tarafından mı düzeltildi/girildi.
/// </summary>
public enum ValueSource
{
    Parser = 0,
    User = 1
}

/// <summary>
/// Çoklu fiş fotoğrafı (bir fotoğrafta birden fazla fiş) yükleme akışının durumu.
/// </summary>
public enum ReceiptBatchStatus
{
    /// <summary>Fotoğraf yüklendi, segmentasyon henüz çalışmadı/tamamlanmadı.</summary>
    Uploaded = 0,

    /// <summary>Segmentasyon tamamlandı, önerilen bölgeler kullanıcı onayını bekliyor.</summary>
    RegionsProposed = 1,

    /// <summary>Segmentasyon hiçbir güvenilir bölge bulamadı; kullanıcı manuel bölge çizmeli veya tekrar denemeli.</summary>
    SegmentationFailed = 2,

    /// <summary>Kullanıcı onayladı, bölgeler OCR/parser pipeline'ından geçiyor.</summary>
    Processing = 3,

    /// <summary>Tüm bölgeler işlendi (her biri kendi Receipt kaydına dönüştü).</summary>
    Completed = 4,

    /// <summary>İşleme sırasında beklenmeyen bir hata oluştu.</summary>
    Failed = 5
}
