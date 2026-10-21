using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())
    .Options;
await using var db = new ApplicationDbContext(options);
var from = Guid.NewGuid();
var to = Guid.NewGuid();
var platformId = Guid.NewGuid();
db.Platforms.Add(new Platform { Id = platformId, Code = "TIKTOK", Name = "TikTok", IsActive = true });
var shop = new Shop { OwnerId = from, Name = "S", PlatformId = platformId, FourSellerShopId = "4242", Status = "Active" };
db.Shops.Add(shop);
await db.SaveChangesAsync();
var listing = new Listing { OwnerId = from, ShopId = shop.Id, FourSellerListingId = 1, FourSellerShopId = 4242, ProductName = "L", PageType = "published" };
listing.Variations.Add(new ListingVariation { SellerSku = "SKU-BACKFILL-1", Quantity = 1 });
db.Listings.Add(listing);
var order = new Order { OwnerId = from, ShopId = shop.Id, FourSellerOrderId = 1, FourSellerShopId = 4242, PlatformOrderId = "PO", OrderStatus = "to_ship" };
order.Items.Add(new OrderItem { SellerSku = "SKU-BACKFILL-1", SourceUrl = "https://us.shein.com/item-p-111.html", Quantity = 1, Title = "T" });
db.Orders.Add(order);
await db.SaveChangesAsync();
Console.WriteLine($"vars={await db.ListingVariations.CountAsync()} items={await db.OrderItems.CountAsync()} listingId={listing.Id}");
shop.OwnerId = to;
await db.SaveChangesAsync();
var svc = new ShopOwnershipTransferService(db, NullLogger<ShopOwnershipTransferService>.Instance);
await svc.TransferAsync(shop.Id, from, to);
await db.SaveChangesAsync();
var updated = await db.Listings.SingleAsync();
Console.WriteLine($"owner={updated.OwnerId==to} url={updated.SourceUrl}");
