using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// One-shot: TTS payouts auto-Confirmed before card-tail matching existed stay Confirmed
/// even when the note mask does not match the shop card. Move those to Pending so the
/// badge (Chờ duyệt) matches the review actions.
/// </summary>
public static class TikTokPayoutMismatchPendingBackfill
{
    /// <summary>Khoá setting đánh dấu đợt sửa đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.tts_payout_mismatch_pending_v1";

    private const int BatchSize = 500;

    /// <summary>Chạy một lần: Confirmed lệch đuôi thẻ → Pending.</summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done")
        {
            return;
        }

        var scanned = 0;
        var changed = 0;
        var lastId = Guid.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await context.ReceivingBankTransactions
                .Include(t => t.ReceivingBankAccount)
                .Where(t =>
                    t.Source == ReceivingBankTransaction.SourceTikTokPayout
                    && t.ConfirmationStatus == ReceivingBankTransaction.ConfirmationConfirmed
                    && t.Id > lastId)
                .OrderBy(t => t.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            lastId = batch[^1].Id;
            scanned += batch.Count;

            foreach (var txn in batch)
            {
                if (!ReceivingBankCardMask.IsPayoutNoteMismatched(
                        txn.Note,
                        txn.ReceivingBankAccount?.AccountNumber))
                {
                    continue;
                }

                txn.ConfirmationStatus = ReceivingBankTransaction.ConfirmationPending;
                txn.UpdatedAt = DateTime.UtcNow;
                changed++;
            }

            await context.SaveChangesAsync(cancellationToken);

            foreach (var entry in context.ChangeTracker.Entries<ReceivingBankTransaction>().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        if (marker is null)
        {
            context.Settings.Add(new Setting
            {
                Group = "Bảo trì dữ liệu",
                Key = CompletedSettingKey,
                Label = "Đã chuyển payout TTS lệch thẻ sang Chờ duyệt",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đợt một lần đưa TikTokPayout Confirmed lệch đuôi thẻ về Pending."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "TikTokPayoutMismatchPendingBackfill: quét {Scanned} payout Confirmed, chuyển {Changed} sang Pending.",
            scanned,
            changed);
    }
}
