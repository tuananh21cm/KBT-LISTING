namespace Application.DTOs.Fulfil;

/// <summary>Request to delete multiple fulfil ledger transactions.</summary>
public class BulkDeleteFulfilTransactionsRequest
{
    /// <summary>Transaction ids to delete.</summary>
    public List<Guid> Ids { get; set; } = [];
}
