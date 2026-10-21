using System.Data;
using System.Data.Common;
using Application.DTOs.Maintenance;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// Đọc dung lượng thật từ catalog Postgres.
///
/// Lý do tồn tại: export size theo bảng chỉ cho tổng, nên một bảng 630 MB dễ bị quy cho cột jsonb
/// lớn nhất trong khi heap chỉ 40 MB và 590 MB còn lại là index. Endpoint này tách heap / TOAST /
/// index và kèm <c>idx_scan</c> từng index, để quyết định DROP dựa trên số đo chứ không phải suy đoán.
///
/// Hai câu lệnh cho cả báo cáo: một cho bảng, một cho index. Không dùng <c>pgstattuple</c> (phải cài
/// extension và scan toàn bảng); <c>n_dead_tup</c> + <c>n_tup_upd</c> đủ để nhận ra write amplification.
/// </summary>
public sealed class DatabaseStorageStatsService : IDatabaseStorageStatsService
{
    private const int MaxTop = 200;

    private const string TableSql = """
        SELECT c.relname                                                          AS table_name,
               pg_total_relation_size(c.oid)                                      AS total_bytes,
               pg_table_size(c.oid)                                               AS table_bytes,
               CASE WHEN c.reltoastrelid = 0 THEN 0
                    ELSE pg_total_relation_size(c.reltoastrelid) END              AS toast_bytes,
               pg_indexes_size(c.oid)                                             AS index_bytes,
               (SELECT count(*) FROM pg_index i WHERE i.indrelid = c.oid)         AS index_count,
               COALESCE(s.n_live_tup, 0)                                          AS live_tuples,
               COALESCE(s.n_dead_tup, 0)                                          AS dead_tuples,
               COALESCE(s.n_tup_upd, 0)                                           AS update_count,
               COALESCE(s.n_tup_hot_upd, 0)                                       AS hot_update_count,
               s.last_vacuum,
               s.last_autovacuum,
               s.last_analyze
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN pg_stat_user_tables s ON s.relid = c.oid
        WHERE c.relkind = 'r' AND n.nspname = current_schema()
        ORDER BY pg_total_relation_size(c.oid) DESC
        LIMIT @top;
        """;

    private const string IndexSql = """
        SELECT s.relname                                                          AS table_name,
               s.indexrelname                                                     AS index_name,
               am.amname                                                          AS method,
               pg_relation_size(s.indexrelid)                                     AS bytes,
               COALESCE(s.idx_scan, 0)                                            AS scans,
               i.indisunique,
               i.indisprimary,
               regexp_replace(pg_get_indexdef(s.indexrelid), '^.* USING [a-z_]+ ', '') AS definition
        FROM pg_stat_user_indexes s
        JOIN pg_index i ON i.indexrelid = s.indexrelid
        JOIN pg_class ic ON ic.oid = s.indexrelid
        JOIN pg_am am ON am.oid = ic.relam
        WHERE s.schemaname = current_schema()
          AND (@table IS NULL OR lower(s.relname) = @table)
        ORDER BY pg_relation_size(s.indexrelid) DESC
        LIMIT @top;
        """;

    private const string TotalsSql = """
        SELECT pg_database_size(current_database())                               AS database_bytes,
               COALESCE(sum(pg_table_size(c.oid)), 0)                             AS table_bytes,
               COALESCE(sum(pg_indexes_size(c.oid)), 0)                           AS index_bytes
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'r' AND n.nspname = current_schema();
        """;

    private readonly ApplicationDbContext _db;

    /// <summary>Khởi tạo service đọc thống kê dung lượng.</summary>
    public DatabaseStorageStatsService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<DatabaseStorageStatsDto> GetAsync(
        string? tableName,
        int top,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(top, 1, MaxTop);
        var table = string.IsNullOrWhiteSpace(tableName) ? null : tableName.Trim().ToLowerInvariant();

        var result = new DatabaseStorageStatsDto { GeneratedAt = DateTime.UtcNow };

        var connection = _db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = TotalsSql;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    result.DatabaseBytes = ReadInt64(reader, 0);
                    result.TotalTableBytes = ReadInt64(reader, 1);
                    result.TotalIndexBytes = ReadInt64(reader, 2);
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = TableSql;
                AddParameter(command, "top", limit);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Tables.Add(new DatabaseTableStatsDto
                    {
                        TableName = reader.GetString(0),
                        TotalBytes = ReadInt64(reader, 1),
                        TableBytes = ReadInt64(reader, 2),
                        ToastBytes = ReadInt64(reader, 3),
                        IndexBytes = ReadInt64(reader, 4),
                        IndexCount = (int)ReadInt64(reader, 5),
                        LiveTuples = ReadInt64(reader, 6),
                        DeadTuples = ReadInt64(reader, 7),
                        UpdateCount = ReadInt64(reader, 8),
                        HotUpdateCount = ReadInt64(reader, 9),
                        LastVacuum = ReadDateTime(reader, 10),
                        LastAutoVacuum = ReadDateTime(reader, 11),
                        LastAnalyze = ReadDateTime(reader, 12)
                    });
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = IndexSql;
                AddParameter(command, "top", limit);
                AddParameter(command, "table", table, DbType.String);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var scans = ReadInt64(reader, 4);
                    var isUnique = reader.GetBoolean(5);
                    var isPrimary = reader.GetBoolean(6);
                    result.Indexes.Add(new DatabaseIndexStatsDto
                    {
                        TableName = reader.GetString(0),
                        IndexName = reader.GetString(1),
                        Method = reader.GetString(2),
                        Bytes = ReadInt64(reader, 3),
                        Scans = scans,
                        IsUnique = isUnique,
                        IsPrimary = isPrimary,
                        Definition = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                        DropCandidate = scans == 0 && !isUnique && !isPrimary
                    });
                }
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="dbType"/> phải nêu tường minh cho tham số có thể null: <c>@table IS NULL</c>
    /// buộc Postgres biết kiểu của placeholder, mà <c>DBNull</c> thì driver không suy ra được.
    /// </summary>
    private static void AddParameter(DbCommand command, string name, object? value, DbType? dbType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        if (dbType.HasValue)
        {
            parameter.DbType = dbType.Value;
        }

        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// <c>pg_*_size</c> trả <c>bigint</c>, còn <c>n_live_tup</c> ở một số bản là <c>numeric</c> —
    /// đọc qua <c>GetInt64</c> trực tiếp sẽ ném <c>InvalidCastException</c>.
    /// </summary>
    private static long ReadInt64(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0L : Convert.ToInt64(reader.GetValue(ordinal));

    private static DateTime? ReadDateTime(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTime>(ordinal).ToUniversalTime();
}
