namespace Application.DTOs.Maintenance;

/// <summary>
/// Phân bổ dung lượng thật của database: tách heap / TOAST / index để không lặp lại chẩn đoán sai
/// "RawPayload làm phình DB" — trong khi phần lớn dung lượng nằm ở index.
/// </summary>
public class DatabaseStorageStatsDto
{
    /// <summary>Tổng dung lượng database (<c>pg_database_size</c>).</summary>
    public long DatabaseBytes { get; set; }

    /// <summary>Tổng heap + TOAST của mọi bảng trong schema.</summary>
    public long TotalTableBytes { get; set; }

    /// <summary>Tổng dung lượng index của mọi bảng trong schema.</summary>
    public long TotalIndexBytes { get; set; }

    /// <summary>Bảng xếp theo tổng dung lượng giảm dần.</summary>
    public List<DatabaseTableStatsDto> Tables { get; set; } = [];

    /// <summary>Index xếp theo dung lượng giảm dần (đã lọc theo <c>table</c> nếu có).</summary>
    public List<DatabaseIndexStatsDto> Indexes { get; set; } = [];

    /// <summary>Mốc chạy truy vấn (UTC).</summary>
    public DateTime GeneratedAt { get; set; }
}

/// <summary>Dung lượng và mức bloat của một bảng.</summary>
public class DatabaseTableStatsDto
{
    /// <summary>Tên bảng.</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>heap + TOAST + index.</summary>
    public long TotalBytes { get; set; }

    /// <summary>heap + TOAST (không gồm index) — đây mới là nơi RawPayload nằm.</summary>
    public long TableBytes { get; set; }

    /// <summary>Riêng TOAST (blob text/jsonb bị đẩy ra ngoài trang).</summary>
    public long ToastBytes { get; set; }

    /// <summary>Tổng mọi index của bảng.</summary>
    public long IndexBytes { get; set; }

    /// <summary>Số index của bảng — bảng nhiều index thì mỗi UPDATE đắt gấp đó lần.</summary>
    public int IndexCount { get; set; }

    /// <summary>Row còn sống theo <c>pg_stat_user_tables</c>.</summary>
    public long LiveTuples { get; set; }

    /// <summary>Row chết chờ VACUUM — tỷ lệ cao là dấu hiệu UPDATE rỗng liên tục.</summary>
    public long DeadTuples { get; set; }

    /// <summary>Tổng số UPDATE từ lần reset thống kê — thước đo write amplification.</summary>
    public long UpdateCount { get; set; }

    /// <summary>Số UPDATE dùng được HOT (không phải ghi lại index). Thấp = mọi UPDATE ghi lại mọi index.</summary>
    public long HotUpdateCount { get; set; }

    /// <summary>Lần VACUUM thủ công gần nhất.</summary>
    public DateTime? LastVacuum { get; set; }

    /// <summary>Lần autovacuum gần nhất.</summary>
    public DateTime? LastAutoVacuum { get; set; }

    /// <summary>Lần ANALYZE gần nhất.</summary>
    public DateTime? LastAnalyze { get; set; }
}

/// <summary>Dung lượng và mức dùng của một index — cơ sở để quyết định DROP.</summary>
public class DatabaseIndexStatsDto
{
    /// <summary>Bảng chứa index.</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>Tên index.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Loại index (<c>btree</c>, <c>gin</c>, …).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Dung lượng index.</summary>
    public long Bytes { get; set; }

    /// <summary>Số lần index được planner dùng (<c>idx_scan</c>).</summary>
    public long Scans { get; set; }

    /// <summary>Index unique — không drop được nếu đang bảo vệ ràng buộc.</summary>
    public bool IsUnique { get; set; }

    /// <summary>Primary key.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Cột / biểu thức của index, để đối chiếu index nào bị composite khác che.</summary>
    public string Definition { get; set; } = string.Empty;

    /// <summary>
    /// Ứng viên DROP: chưa từng được scan, không unique, không PK. Vẫn phải đối chiếu query trước
    /// khi xoá — thống kê reset khi restart Postgres nên <c>0</c> có thể là "vừa reset".
    /// </summary>
    public bool DropCandidate { get; set; }
}
