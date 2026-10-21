namespace Application.DTOs.FourSeller;

/// <summary>
/// One order row from 4Seller order page API.
/// </summary>
public class FourSellerOrderRecordDto
{
    /// <summary>4Seller order id.</summary>
    public long OrderId { get; set; }

    /// <summary>Order amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Total paid.</summary>
    public decimal TotalPaidAmount { get; set; }

    /// <summary>Currency.</summary>
    public string? Currency { get; set; }

    /// <summary>4Seller order status.</summary>
    public string? OrderStatus { get; set; }

    /// <summary>Platform order status.</summary>
    public string? PlatformOrderStatus { get; set; }

    /// <summary>Platform code.</summary>
    public string? Platform { get; set; }

    /// <summary>Platform order id.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Platform order number.</summary>
    public string? PlatformOrderNo { get; set; }

    /// <summary>4Seller shop id.</summary>
    public long? ShopId { get; set; }

    /// <summary>Shop name when present.</summary>
    public string? ShopName { get; set; }

    /// <summary>Buyer memo.</summary>
    public string? BuyerMemo { get; set; }

    /// <summary>Seller memo.</summary>
    public string? SellerMemo { get; set; }

    /// <summary>Internal memo.</summary>
    public string? InternalMemo { get; set; }

    /// <summary>Shipment number.</summary>
    public string? ShipmentNo { get; set; }

    /// <summary>Tracking number.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>Shipping method label.</summary>
    public string? ShippingMethod { get; set; }

    /// <summary>Ship-to address.</summary>
    public FourSellerOrderAddressDto? OrderAddressVo { get; set; }

    /// <summary>Order time fields.</summary>
    public FourSellerOrderTimeInfoDto? TimeInfo { get; set; }

    /// <summary>Shipping service info.</summary>
    public FourSellerOrderShippingServiceDto? ShippingServiceInfo { get; set; }

    /// <summary>Package dimensions / weight.</summary>
    public FourSellerOrderPackageDto? PackageInfo { get; set; }

    /// <summary>Line items.</summary>
    public List<FourSellerOrderItemDto> OrderItemInfoList { get; set; } = [];

    /// <summary>Raw JSON for this record (optional, set by client).</summary>
    public string? RawJson { get; set; }
}

/// <summary>Package info from 4Seller.</summary>
public class FourSellerOrderPackageDto
{
    /// <summary>Weight.</summary>
    public decimal? Weight { get; set; }

    /// <summary>Weight unit.</summary>
    public string? WeightUnit { get; set; }

    /// <summary>Length.</summary>
    public decimal? Length { get; set; }

    /// <summary>Width.</summary>
    public decimal? Width { get; set; }

    /// <summary>Height.</summary>
    public decimal? Height { get; set; }

    /// <summary>Dimension unit.</summary>
    public string? DimensionUnit { get; set; }
}

/// <summary>Ship-to address from 4Seller.</summary>
public class FourSellerOrderAddressDto
{
    /// <summary>Recipient name.</summary>
    public string? Name { get; set; }

    /// <summary>City.</summary>
    public string? City { get; set; }

    /// <summary>Postal code.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Full address.</summary>
    public string? Address { get; set; }

    /// <summary>Address line 1.</summary>
    public string? Address1 { get; set; }

    /// <summary>Phone.</summary>
    public string? Phone { get; set; }

    /// <summary>Email.</summary>
    public string? Email { get; set; }

    /// <summary>Country code.</summary>
    public string? CountryCode { get; set; }

    /// <summary>State.</summary>
    public string? State { get; set; }
}

/// <summary>Time info from 4Seller order.</summary>
public class FourSellerOrderTimeInfoDto
{
    /// <summary>Paid time string.</summary>
    public string? OrderPaidTime { get; set; }

    /// <summary>Create time string.</summary>
    public string? OrderCreateTime { get; set; }

    /// <summary>Latest ship date string.</summary>
    public string? LatestShipDate { get; set; }

    /// <summary>Shipped time string.</summary>
    public string? OrderShippedTime { get; set; }

    /// <summary>Latest delivery date string.</summary>
    public string? LatestDeliveryDate { get; set; }

    /// <summary>Cancel time string (when platform canceled the order).</summary>
    public string? CancelTime { get; set; }
}

/// <summary>Shipping service snippet.</summary>
public class FourSellerOrderShippingServiceDto
{
    /// <summary>Shipping service name.</summary>
    public string? ShippingService { get; set; }
}

/// <summary>Order line item from 4Seller.</summary>
public class FourSellerOrderItemDto
{
    /// <summary>4Seller order item id.</summary>
    public long OrderItemId { get; set; }

    /// <summary>Parent order id.</summary>
    public long OrderId { get; set; }

    /// <summary>Seller SKU.</summary>
    public string? SellerSku { get; set; }

    /// <summary>Title.</summary>
    public string? Title { get; set; }

    /// <summary>Image URL.</summary>
    public string? ImgUrl { get; set; }

    /// <summary>Listing URL.</summary>
    public string? ListingUrl { get; set; }

    /// <summary>Source URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Variant attributes.</summary>
    public string? VariantAttr { get; set; }

    /// <summary>Quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Unit price.</summary>
    public decimal ItemPrice { get; set; }

    /// <summary>Currency.</summary>
    public string? Currency { get; set; }
}
