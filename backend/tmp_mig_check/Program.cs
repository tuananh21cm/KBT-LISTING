using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Infrastructure.Persistence;

var cfg = new ConfigurationBuilder()
  .SetBasePath(@"G:\Software development\drop.kinhbac.ai\backend\src\Api")
  .AddJsonFile("appsettings.Development.json")
  .Build();
var cs = cfg.GetConnectionString("DefaultConnection")!;
var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs).Options;
await using var db = new ApplicationDbContext(opts);
Console.WriteLine("Applying pending…");
await db.Database.MigrateAsync();
var pending = await db.Database.GetPendingMigrationsAsync();
Console.WriteLine("PENDING_COUNT=" + pending.Count());
await using var cmd = db.Database.GetDbConnection().CreateCommand();
await db.Database.OpenConnectionAsync();
cmd.CommandText = @"SELECT column_name FROM information_schema.columns WHERE table_name='Orders' AND column_name LIKE 'FulfilClaim%' ORDER BY 1;
SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name='OrderFulfilAttempts');";
await using var r = await cmd.ExecuteReaderAsync();
Console.WriteLine("CLAIM_COLS:");
while (await r.ReadAsync()) Console.WriteLine(r.GetString(0));
await r.NextResultAsync();
await r.ReadAsync(); Console.WriteLine("HAS_ATTEMPTS=" + r.GetBoolean(0));
