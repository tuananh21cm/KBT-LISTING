using Npgsql;
var cs = Environment.GetEnvironmentVariable("CONN")!;
var ids = Environment.GetEnvironmentVariable("MIGRATION_IDS")!.Split('|', StringSplitOptions.RemoveEmptyEntries);
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();
await using var tx = await conn.BeginTransactionAsync();
var inserted = 0;
foreach (var id in ids)
{
  await using var cmd = new NpgsqlCommand(@"
INSERT INTO public.""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
VALUES (@id, '8.0.11')
ON CONFLICT (""MigrationId"") DO NOTHING;", conn, (NpgsqlTransaction)tx);
  cmd.Parameters.AddWithValue("id", id);
  inserted += await cmd.ExecuteNonQueryAsync();
  Console.WriteLine($"upsert:{id} rows={inserted}");
}
await using (var cmd = new NpgsqlCommand(@"SELECT COUNT(*) FROM public.""__EFMigrationsHistory"";", conn, (NpgsqlTransaction)tx))
  Console.WriteLine("history_count=" + await cmd.ExecuteScalarAsync());
await tx.CommitAsync();
Console.WriteLine("done");
