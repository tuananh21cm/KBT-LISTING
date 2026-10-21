namespace Application.DTOs.ReceivingBankAccount;

/// <summary>Receiving card source (PingPongX, WorldFirst, …).</summary>
public class ReceivingCardSourceDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    public string? Description { get; set; }

    /// <summary>Whether cards should link a provider account.</summary>
    public bool SupportsAccounts { get; set; }

    /// <summary>Active.</summary>
    public bool IsActive { get; set; }
}

/// <summary>Provider account under a source.</summary>
public class ReceivingSourceAccountDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Source id.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Source name.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Source code.</summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>External account id.</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>Username / email.</summary>
    public string? Username { get; set; }

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>Active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Cards linked.</summary>
    public int CardCount { get; set; }

    /// <summary>Created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create provider account.</summary>
public class CreateReceivingSourceAccountDto
{
    /// <summary>Source id.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>External account id.</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>Username / email.</summary>
    public string? Username { get; set; }

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>Active.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Update provider account.</summary>
public class UpdateReceivingSourceAccountDto
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>External account id.</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>Username / email.</summary>
    public string? Username { get; set; }

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>Active.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Dropdown option for provider accounts.</summary>
public class ReceivingSourceAccountOptionDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Source id.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Username.</summary>
    public string? Username { get; set; }

    /// <summary>Active.</summary>
    public bool IsActive { get; set; }
}
