using Application.DTOs.Order;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Multi-session fulfil claim / lease + attempt history.
/// </summary>
public class FulfilOrderClaimTests
{
    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OwnerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Session1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Session2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Session3 = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Claim_FreeOrder_Ok()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);

        var result = await svc.ClaimAsync(orderId, Session1, "P1", null, UserA);

        Assert.Equal(FulfilClaimStates.Held, result.Claim.State);
        Assert.Equal(UserA, result.Claim.ClaimedByUserId);
        Assert.Equal(1, result.Claim.SessionCount);
        Assert.Contains(Session1, result.Claim.SessionIds);

        var attempt = Assert.Single(await db.OrderFulfilAttempts.ToListAsync());
        Assert.Equal(Session1, attempt.SessionId);
        Assert.Null(attempt.ClosedAt);
    }

    [Fact]
    public async Task Claim_OtherUser_Conflict409()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ClaimAsync(orderId, Session2, null, null, UserB));
        Assert.Contains("giữ", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Claim_SameSession_RenewsWithoutNewAttempt()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);

        var first = await svc.ClaimAsync(orderId, Session1, null, null, UserA);
        var started = first.Claim.StartedAt;
        await Task.Delay(20);
        var second = await svc.ClaimAsync(orderId, Session1, null, null, UserA);

        Assert.Equal(started, second.Claim.StartedAt);
        Assert.Single(await db.OrderFulfilAttempts.ToListAsync());
        Assert.True(second.Claim.ExpiresAt >= first.Claim.ExpiresAt);
    }

    [Fact]
    public async Task ListActiveClaims_EnforcesSingleBatchPerSession()
    {
        await using var db = await SeedAsync();
        db.Orders.Add(NewOrder("ORD-2"));
        db.Orders.Add(NewOrder("ORD-3"));
        await db.SaveChangesAsync();
        var orders = await db.Orders.OrderBy(o => o.PlatformOrderId).ToListAsync();
        var svc = NewService(db);

        // Simulate stale multi-focus (pre-exclusive) by claiming then reopening older attempts.
        await svc.ClaimAsync(orders[0].Id, Session1, null, null, UserA);
        await svc.ClaimAsync(orders[1].Id, Session1, null, null, UserA);

        // Tamper: reopen first order attempt as if exclusive never ran.
        var closed = await db.OrderFulfilAttempts.SingleAsync(a => a.OrderId == orders[0].Id);
        closed.ClosedAt = null;
        closed.Outcome = null;
        closed.DurationSeconds = null;
        closed.LastRenewedAt = DateTime.UtcNow.AddMinutes(-5);
        var o0 = await db.Orders.SingleAsync(o => o.Id == orders[0].Id);
        o0.FulfilClaimedByUserId = UserA;
        o0.FulfilClaimedAt = closed.StartedAt;
        o0.FulfilClaimExpiresAt = closed.ExpiresAt;
        o0.FulfilClaimSessionCount = 1;
        await db.SaveChangesAsync();

        var active = await svc.ListActiveClaimsAsync();
        Assert.DoesNotContain(active.Items, i => i.OrderId == orders[0].Id);
        Assert.Contains(active.Items, i => i.OrderId == orders[1].Id);
        Assert.NotNull(
            (await db.OrderFulfilAttempts.SingleAsync(a => a.OrderId == orders[0].Id)).ClosedAt);
    }

    [Fact]
    public async Task Claim_SameSession_NewOrder_ReleasesPreviousFocus()
    {
        await using var db = await SeedAsync();
        db.Orders.Add(NewOrder("ORD-2"));
        await db.SaveChangesAsync();
        var orders = await db.Orders.OrderBy(o => o.PlatformOrderId).ToListAsync();
        var first = orders[0];
        var second = orders[1];
        var svc = NewService(db);

        await svc.ClaimAsync(first.Id, Session1, null, null, UserA);
        var result = await svc.ClaimAsync(second.Id, Session1, null, null, UserA);

        Assert.Equal(FulfilClaimStates.Held, result.Claim.State);
        Assert.Equal(second.Id, result.Claim.OrderId);
        var released = Assert.Single(result.ReleasedClaims);
        Assert.Equal(first.Id, released.OrderId);
        Assert.Equal(FulfilClaimStates.Released, released.State);

        Assert.Equal(
            FulfilAttemptOutcomes.Released,
            (await db.OrderFulfilAttempts.SingleAsync(a => a.OrderId == first.Id)).Outcome);
        Assert.Null((await db.Orders.SingleAsync(o => o.Id == first.Id)).FulfilClaimedByUserId);
        Assert.Equal(UserA, (await db.Orders.SingleAsync(o => o.Id == second.Id)).FulfilClaimedByUserId);
    }

    [Fact]
    public async Task Claim_SameSession_SiblingBatch_DoesNotReleaseSibling()
    {
        await using var db = await SeedAsync(sibling: true);
        var orders = await db.Orders.OrderBy(o => o.PlatformOrderId).ToListAsync();
        var primary = orders[0];
        var sibling = orders[1];
        var svc = NewService(db);

        var result = await svc.ClaimAsync(
            primary.Id,
            Session1,
            null,
            [sibling.Id],
            UserA);

        Assert.Empty(result.ReleasedClaims);
        Assert.Equal(2, await db.OrderFulfilAttempts.CountAsync(a => a.ClosedAt == null));
        Assert.Equal(FulfilClaimStates.Held, result.Claim.State);
        Assert.Single(result.SiblingClaims);
    }

    [Fact]
    public async Task Claim_SameSession_NewOrder_OtherSessionKeepsPreviousLease()
    {
        await using var db = await SeedAsync();
        db.Orders.Add(NewOrder("ORD-2"));
        await db.SaveChangesAsync();
        var orders = await db.Orders.OrderBy(o => o.PlatformOrderId).ToListAsync();
        var first = orders[0];
        var second = orders[1];
        var svc = NewService(db);

        await svc.ClaimAsync(first.Id, Session1, null, null, UserA);
        await svc.ClaimAsync(first.Id, Session2, null, null, UserA);
        var result = await svc.ClaimAsync(second.Id, Session1, null, null, UserA);

        Assert.Single(result.ReleasedClaims);
        Assert.Equal(FulfilClaimStates.Held, result.ReleasedClaims[0].State);
        Assert.Equal(1, result.ReleasedClaims[0].SessionCount);
        Assert.Contains(Session2, result.ReleasedClaims[0].SessionIds);

        var firstOrder = await db.Orders.SingleAsync(o => o.Id == first.Id);
        Assert.Equal(UserA, firstOrder.FulfilClaimedByUserId);
        Assert.Equal(1, firstOrder.FulfilClaimSessionCount);
    }

    [Fact]
    public async Task Claim_SameUserOtherSession_IncrementsSessionCount()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);

        await svc.ClaimAsync(orderId, Session1, "P1", null, UserA);
        var second = await svc.ClaimAsync(orderId, Session2, "P2", null, UserA);

        Assert.Equal(2, second.Claim.SessionCount);
        Assert.Equal(2, await db.OrderFulfilAttempts.CountAsync(a => a.ClosedAt == null));
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(2, order.FulfilClaimSessionCount);
        Assert.Equal(UserA, order.FulfilClaimedByUserId);
    }

    [Fact]
    public async Task Release_OneOfTwoSessions_KeepsLease()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);
        await svc.ClaimAsync(orderId, Session2, null, null, UserA);

        var released = await svc.ReleaseClaimAsync(
            orderId, Session1, UserA, isAdmin: false, allSessions: false);

        Assert.Equal(FulfilClaimStates.Held, released.Claim.State);
        Assert.Equal(1, released.Claim.SessionCount);
        Assert.DoesNotContain(Session1, released.Claim.SessionIds);
        Assert.Contains(Session2, released.Claim.SessionIds);

        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(UserA, order.FulfilClaimedByUserId);
        Assert.Equal(1, order.FulfilClaimSessionCount);
    }

    [Fact]
    public async Task Release_LastSession_ClearsLease()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);

        var released = await svc.ReleaseClaimAsync(
            orderId, Session1, UserA, isAdmin: false, allSessions: false);

        Assert.Equal(FulfilClaimStates.Released, released.Claim.State);
        Assert.Equal(0, released.Claim.SessionCount);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Null(order.FulfilClaimedByUserId);
        Assert.Equal(0, order.FulfilClaimSessionCount);
    }

    [Fact]
    public async Task ReleaseSession_ClearsWhenLastSession_KeepsOtherSessionLease()
    {
        await using var db = await SeedAsync(sibling: true);
        var orders = await db.Orders.OrderBy(o => o.PlatformOrderId).ToListAsync();
        var svc = NewService(db);

        await svc.ClaimAsync(orders[0].Id, Session1, null, [orders[1].Id], UserA);
        // Second session only on primary — sibling stays Session1-only.
        await svc.ClaimAsync(orders[0].Id, Session2, null, null, UserA);

        var result = await svc.ReleaseSessionClaimsAsync(Session1, UserA);

        Assert.Equal(2, result.Items.Count);
        var primary = Assert.Single(result.Items, i => i.OrderId == orders[0].Id);
        var sibling = Assert.Single(result.Items, i => i.OrderId == orders[1].Id);

        Assert.Equal(FulfilClaimStates.Held, primary.State);
        Assert.Equal(1, primary.SessionCount);
        Assert.Contains(Session2, primary.SessionIds);
        Assert.DoesNotContain(Session1, primary.SessionIds);

        Assert.Equal(FulfilClaimStates.Released, sibling.State);
        Assert.Equal(0, sibling.SessionCount);

        Assert.Equal(0, await db.OrderFulfilAttempts.CountAsync(
            a => a.SessionId == Session1 && a.ClosedAt == null));
        Assert.Equal(UserA, (await db.Orders.SingleAsync(o => o.Id == orders[0].Id)).FulfilClaimedByUserId);
        Assert.Null((await db.Orders.SingleAsync(o => o.Id == orders[1].Id)).FulfilClaimedByUserId);
    }

    [Fact]
    public async Task ReleaseSession_LastSession_ClearsLease()
    {
        await using var db = await SeedAsync(sibling: true);
        var orders = await db.Orders.OrderBy(o => o.PlatformOrderId).ToListAsync();
        var svc = NewService(db);

        await svc.ClaimAsync(orders[0].Id, Session1, null, [orders[1].Id], UserA);

        var result = await svc.ReleaseSessionClaimsAsync(Session1, UserA);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal(FulfilClaimStates.Released, i.State));
        Assert.All(result.Items, i => Assert.Equal(0, i.SessionCount));
        Assert.Null((await db.Orders.SingleAsync(o => o.Id == orders[0].Id)).FulfilClaimedByUserId);
        Assert.Null((await db.Orders.SingleAsync(o => o.Id == orders[1].Id)).FulfilClaimedByUserId);
    }

    [Fact]
    public async Task ReleaseSession_OnlyOwnUserAttempts()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);

        // Acting as UserB on Session1 must not close UserA's attempt (filter by userId).
        var result = await svc.ReleaseSessionClaimsAsync(Session1, UserB);
        Assert.Empty(result.Items);
        Assert.Null((await db.OrderFulfilAttempts.SingleAsync(a => a.SessionId == Session1)).ClosedAt);
        Assert.Equal(UserA, (await db.Orders.SingleAsync(o => o.Id == orderId)).FulfilClaimedByUserId);
    }

    [Fact]
    public async Task Release_AllSessions_ClearsEverything()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);
        await svc.ClaimAsync(orderId, Session2, null, null, UserA);

        var released = await svc.ReleaseClaimAsync(
            orderId, null, UserA, isAdmin: false, allSessions: true);

        Assert.Equal(FulfilClaimStates.Released, released.Claim.State);
        Assert.Equal(0, await db.OrderFulfilAttempts.CountAsync(a => a.ClosedAt == null));
    }

    [Fact]
    public async Task Purchase_ClosesSessionAsPurchased_OthersSuperseded()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);
        await svc.ClaimAsync(orderId, Session2, null, null, UserA);
        await svc.ClaimAsync(orderId, Session3, null, null, UserA);

        var purchasedAt = DateTime.UtcNow;
        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { FulfilPurchasedAt = purchasedAt },
            UserA,
            CancellationToken.None,
            Session2);

        var attempts = await db.OrderFulfilAttempts.ToListAsync();
        Assert.Equal(3, attempts.Count);
        var purchased = Assert.Single(attempts, a => a.Outcome == FulfilAttemptOutcomes.Purchased);
        Assert.Equal(Session2, purchased.SessionId);
        Assert.NotNull(purchased.CompletedAt);
        Assert.NotNull(purchased.DurationSeconds);
        Assert.Equal(2, attempts.Count(a => a.Outcome == FulfilAttemptOutcomes.Superseded));

        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Null(order.FulfilClaimedByUserId);
        Assert.Equal(UserA, order.PaidByUserId);
        Assert.NotNull(order.FulfilPurchasedAt);
    }

    [Fact]
    public async Task Purchase_OtherUserHolding_ConflictUnlessForce()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.UpdateMilestonesAsync(
                orderId,
                new UpdateOrderMilestonesRequest { FulfilPurchasedAt = DateTime.UtcNow },
                UserB,
                CancellationToken.None,
                Session2));

        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { FulfilPurchasedAt = DateTime.UtcNow, Force = true },
            UserB,
            CancellationToken.None,
            Session2);

        var attempts = await db.OrderFulfilAttempts.ToListAsync();
        Assert.Contains(attempts, a => a.Outcome == FulfilAttemptOutcomes.TakenOver);
        Assert.Contains(attempts, a => a.Outcome == FulfilAttemptOutcomes.Purchased && a.UserId == UserB);
    }

    [Fact]
    public async Task Purchase_WithoutClaim_DurationNull()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);

        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { FulfilPurchasedAt = DateTime.UtcNow },
            UserA);

        var attempt = Assert.Single(await db.OrderFulfilAttempts.ToListAsync());
        Assert.Equal(FulfilAttemptOutcomes.Purchased, attempt.Outcome);
        Assert.Null(attempt.DurationSeconds);
    }

    [Fact]
    public async Task ClearPurchase_RevertsAttempt()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);
        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { FulfilPurchasedAt = DateTime.UtcNow },
            UserA,
            CancellationToken.None,
            Session1);

        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { ClearFulfilPurchasedAt = true },
            UserA);

        var attempt = Assert.Single(await db.OrderFulfilAttempts.ToListAsync());
        Assert.Equal(FulfilAttemptOutcomes.Reverted, attempt.Outcome);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Null(order.PaidByUserId);
        Assert.Null(order.FulfilPurchasedAt);
    }

    [Fact]
    public async Task Purchase_AfterSheinStamp_StillClaimsBuyer()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        // Sync đã stamp mua + gán nhầm người sync (UserB) trước cú bấm.
        OrderFulfilTimeline.Stamp(
            order,
            OrderFulfilTimeline.Milestone.FulfilPurchased,
            DateTime.UtcNow.AddMinutes(-10),
            OrderFulfilTimeline.Source.Shein);
        order.PaidByUserId = UserB;
        await db.SaveChangesAsync();

        var svc = NewService(db);
        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { FulfilPurchasedAt = DateTime.UtcNow },
            UserA,
            CancellationToken.None,
            Session1);

        order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(UserA, order.PaidByUserId);
        Assert.Contains(
            await db.OrderFulfilAttempts.ToListAsync(),
            a => a.Outcome == FulfilAttemptOutcomes.Purchased && a.UserId == UserA);
    }

    [Fact]
    public async Task Purchase_AfterClear_RestoresBuyer_EvenWithOldPurchasedAttempt()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);

        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { FulfilPurchasedAt = DateTime.UtcNow },
            UserA);
        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { ClearFulfilPurchasedAt = true },
            UserA);

        // Revert chỉ đổi outcome attempt mới nhất; attempt Purchased có thể còn sót (force-takeover).
        // Thêm attempt Purchased cũ giả lập — claimsPurchase phải dựa OR PaidByUserId is null.
        db.OrderFulfilAttempts.Add(new OrderFulfilAttempt
        {
            OrderId = orderId,
            UserId = UserB,
            SessionId = Session3,
            BatchId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow.AddHours(-2),
            LastRenewedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(-1),
            ClosedAt = DateTime.UtcNow.AddHours(-1),
            Outcome = FulfilAttemptOutcomes.Purchased,
            CompletedAt = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
        });
        await db.SaveChangesAsync();

        await svc.UpdateMilestonesAsync(
            orderId,
            new UpdateOrderMilestonesRequest { FulfilPurchasedAt = DateTime.UtcNow },
            UserA);

        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(UserA, order.PaidByUserId);
    }

    [Fact]
    public async Task CloseExpired_OnlyExpiredSession_KeepsOther()
    {
        await using var db = await SeedAsync();
        var orderId = db.Orders.Select(o => o.Id).First();
        var svc = NewService(db);
        await svc.ClaimAsync(orderId, Session1, null, null, UserA);
        await svc.ClaimAsync(orderId, Session2, null, null, UserA);

        var a1 = await db.OrderFulfilAttempts.SingleAsync(a => a.SessionId == Session1);
        a1.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var closed = await svc.CloseExpiredAttemptsAsync();
        Assert.Equal(1, closed);

        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(UserA, order.FulfilClaimedByUserId);
        Assert.Equal(1, order.FulfilClaimSessionCount);
        Assert.Equal(
            FulfilAttemptOutcomes.Expired,
            (await db.OrderFulfilAttempts.SingleAsync(a => a.SessionId == Session1)).Outcome);
        Assert.Null((await db.OrderFulfilAttempts.SingleAsync(a => a.SessionId == Session2)).ClosedAt);
    }

    [Fact]
    public async Task GroupClaim_SamePhoneZip_ClaimsSiblings()
    {
        await using var db = await SeedAsync(sibling: true);
        var orders = await db.Orders.OrderBy(o => o.PlatformOrderId).ToListAsync();
        var primary = orders[0];
        var sibling = orders[1];
        var svc = NewService(db);

        var result = await svc.ClaimAsync(
            primary.Id,
            Session1,
            null,
            [sibling.Id],
            UserA);

        Assert.Equal(FulfilClaimStates.Held, result.Claim.State);
        Assert.Single(result.SiblingClaims);
        Assert.Equal(sibling.Id, result.SiblingClaims[0].OrderId);
        Assert.Equal(2, await db.OrderFulfilAttempts.CountAsync(a => a.ClosedAt == null));
    }

    [Fact]
    public async Task MaxPerUser_BlocksNewOrder_NotExtraSession()
    {
        await using var db = NewDb();
        await SeedUsersAsync(db);
        var orders = Enumerable.Range(0, 11)
            .Select(i => NewOrder($"ORD-{i}"))
            .ToList();
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync();

        var settings = new StubSettings(ttl: 15, maxPerUser: 2);
        var svc = NewService(db, settings);

        // Different sessions so exclusive-focus does not auto-release the first order.
        await svc.ClaimAsync(orders[0].Id, Session1, null, null, UserA);
        await svc.ClaimAsync(orders[1].Id, Session2, null, null, UserA);

        // Extra session on same order does not consume quota.
        var extra = await svc.ClaimAsync(orders[0].Id, Session3, null, null, UserA);
        Assert.Equal(2, extra.Claim.SessionCount);

        // Session1 moves focus to a 3rd order → still blocked: order0 (Session3) + order1 (Session2).
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ClaimAsync(orders[2].Id, Session1, null, null, UserA));
    }

    private static async Task<ApplicationDbContext> SeedAsync(bool sibling = false)
    {
        var db = NewDb();
        await SeedUsersAsync(db);
        db.Orders.Add(NewOrder("ORD-1"));
        if (sibling)
        {
            db.Orders.Add(NewOrder("ORD-2"));
        }

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task SeedUsersAsync(ApplicationDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser
            {
                Id = UserA,
                UserName = "fulfil-a",
                NormalizedUserName = "FULFIL-A",
                FullName = "Fulfil A",
                Email = "a@test.local",
                NormalizedEmail = "A@TEST.LOCAL",
            },
            new ApplicationUser
            {
                Id = UserB,
                UserName = "fulfil-b",
                NormalizedUserName = "FULFIL-B",
                FullName = "Fulfil B",
                Email = "b@test.local",
                NormalizedEmail = "B@TEST.LOCAL",
            },
            new ApplicationUser
            {
                Id = OwnerId,
                UserName = "seller-owner",
                NormalizedUserName = "SELLER-OWNER",
                FullName = "Seller Owner",
                Email = "owner@test.local",
                NormalizedEmail = "OWNER@TEST.LOCAL",
            });
        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static OrderSyncService NewService(
        IApplicationDbContext db,
        ISettingProvider? settings = null) =>
        new(
            db,
            null!,
            null!,
            settings ?? new StubSettings(),
            null!,
            null!,
            new NullFulfilQueueRealtimePublisher(),
            NullLogger<OrderSyncService>.Instance);

    private static Order NewOrder(string platformOrderId) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = OwnerId,
        PlatformOrderId = platformOrderId,
        BuyerPhone = "8312295220",
        PostalCode = "93960",
        BuyerName = "Test Buyer",
        OrderCreateTime = new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc),
        PlatformOrderStatus = "AWAITING_SHIPMENT",
        OrderStatus = "to_ship",
        Currency = "USD",
    };

    private sealed class StubSettings : ISettingProvider
    {
        private readonly int _ttl;
        private readonly int _max;

        public StubSettings(int ttl = 15, int maxPerUser = 10)
        {
            _ttl = ttl;
            _max = maxPerUser;
        }

        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key == FulfilClaimSettingKeys.TtlMinutes)
            {
                return Task.FromResult<string?>(_ttl.ToString());
            }

            if (key == FulfilClaimSettingKeys.MaxPerUser)
            {
                return Task.FromResult<string?>(_max.ToString());
            }

            return Task.FromResult<string?>(null);
        }

        public Task<Dictionary<string, string>> GetValuesAsync(
            IEnumerable<string> keys,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<string, string>());

        public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
