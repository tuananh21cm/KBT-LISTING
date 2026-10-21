namespace Shared.Constants;

/// <summary>Where a <c>TikTokAttributeValueRejection</c> row came from.</summary>
public static class TikTokAttributeRejectionSources
{
    /// <summary>Shipped with the database, from failures observed before the learning existed.</summary>
    public const string Seed = "seed";

    /// <summary>Parsed from a publish failure returned by TikTok via 4Seller.</summary>
    public const string PublishError = "publish_error";

    /// <summary>Entered by an admin.</summary>
    public const string Manual = "manual";

    /// <summary>
    /// TikTok reported the value as a custom name, but the payload we sent carried no numeric
    /// <c>valueId</c> for it — that is our own mapping gap, not proof TikTok withdrew the value.
    /// Recorded for diagnosis and deliberately kept out of the denylist.
    /// </summary>
    public const string MissingValueId = "missing_value_id";

    /// <summary>
    /// The rejection carried a numeric <c>valueId</c>, but listings **live on TikTok** in the same
    /// category still use that exact id — so the value was not withdrawn and the refusal came from
    /// the payload we built, not from the enum. Recorded for diagnosis, kept out of the denylist:
    /// blocking a value that hundreds of live listings use empties a mandatory attribute and turns
    /// "custom value" into "missing required Specifics" for every later publish.
    /// </summary>
    public const string ContradictedByLive = "contradicted_by_live";
}
