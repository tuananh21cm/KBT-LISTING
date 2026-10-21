using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingCategoryGuardrailTests
{
    private static readonly ListingCategoryGuardrail.Options Defaults =
        ListingCategoryGuardrail.ParseOptions(
            ListingAutoPublishSettingKeys.DefaultCategoryRiskyAncestorIds,
            ListingAutoPublishSettingKeys.DefaultCategoryRiskyNameKeywords,
            ListingAutoPublishSettingKeys.DefaultCategoryRiskyTitleKeywords,
            blockedRemap: null);

    /// <summary>Defaults plus the production blocked remap (903824 → 903952).</summary>
    private static readonly ListingCategoryGuardrail.Options WithBlocked =
        ListingCategoryGuardrail.ParseOptions(
            ListingAutoPublishSettingKeys.DefaultCategoryRiskyAncestorIds,
            ListingAutoPublishSettingKeys.DefaultCategoryRiskyNameKeywords,
            ListingAutoPublishSettingKeys.DefaultCategoryRiskyTitleKeywords,
            ListingAutoPublishSettingKeys.DefaultCategoryBlockedRemap);

    private static readonly ListingCategoryInfo CostumeTops =
        new("903824", "Costume Tops & Corsets", "601152/842632/843656/903824");

    private static readonly ListingCategoryInfo TanksAndCamis =
        new("843400", "Women's Tanks & Camis", "601152/842248/843400");

    [Fact]
    public void IsRiskyCategory_DetectsConfiguredAncestor()
    {
        Assert.True(ListingCategoryGuardrail.IsRiskyCategory(CostumeTops, Defaults));
        Assert.False(ListingCategoryGuardrail.IsRiskyCategory(TanksAndCamis, Defaults));
    }

    [Fact]
    public void IsRiskyCategory_FallsBackToNameWhenPathNotSynced()
    {
        var unsynced = new ListingCategoryInfo("903824", "Costume Tops & Corsets", null);

        Assert.True(ListingCategoryGuardrail.IsRiskyCategory(unsynced, Defaults));
    }

    [Fact]
    public void Decide_NoCategoryYet_UsesAi()
    {
        var decision = ListingCategoryGuardrail.Decide(
            new ListingCategoryInfo(null, null, null),
            TanksAndCamis,
            aiValidated: true,
            "Women's Lace Corset Top",
            Defaults);

        Assert.Equal(ListingCategoryDecisionKind.Assign, decision.Kind);
    }

    [Fact]
    public void Decide_SameCategory_Confirmed()
    {
        var decision = ListingCategoryGuardrail.Decide(
            TanksAndCamis,
            TanksAndCamis,
            aiValidated: true,
            "Women's Lace Corset Top",
            Defaults);

        Assert.Equal(ListingCategoryDecisionKind.Confirmed, decision.Kind);
    }

    [Fact]
    public void Decide_RiskyCategoryWithPlainTitle_Overrides()
    {
        var decision = ListingCategoryGuardrail.Decide(
            CostumeTops,
            TanksAndCamis,
            aiValidated: true,
            "Women's Lace Trim Corset Top, Summer Night Out",
            Defaults);

        Assert.Equal(ListingCategoryDecisionKind.Override, decision.Kind);
    }

    [Fact]
    public void Decide_OccasionWordOnly_StillOverrides()
    {
        // Scraped titles advertise occasions on ordinary clothing, and 4Seller AI classifies this one
        // as Women's Tanks & Camis — treating "Halloween" as a costume signal kept it failing.
        var decision = ListingCategoryGuardrail.Decide(
            CostumeTops,
            TanksAndCamis,
            aiValidated: true,
            "Vintage Lace Trim Floral Corset Tank Top, Suitable for Music Festivals, Halloween and Other Occasions",
            Defaults);

        Assert.Equal(ListingCategoryDecisionKind.Override, decision.Kind);
    }

    [Theory]
    [InlineData("Women's Halloween Costume Corset Top")]
    [InlineData("Cosplay Bustier for Anime Convention")]
    public void Decide_RiskyCategoryWithCostumeTitle_KeepsWhenNotBlocked(string title)
    {
        // Without blocked remap, a costume title still justifies staying in the costume branch
        // when AI points at a non-costume leaf.
        var decision = ListingCategoryGuardrail.Decide(
            CostumeTops,
            TanksAndCamis,
            aiValidated: true,
            title,
            Defaults);

        Assert.Equal(ListingCategoryDecisionKind.Keep, decision.Kind);
    }

    [Theory]
    [InlineData("Women's Halloween Costume Corset Top")]
    [InlineData("Cosplay Bustier for Anime Convention")]
    public void Decide_BlockedCategory_OverridesEvenWithCostumeTitle(string title)
    {
        // 903824 cannot be published into for some shops; costume title must not pin the listing there.
        var decision = ListingCategoryGuardrail.Decide(
            CostumeTops,
            TanksAndCamis,
            aiValidated: true,
            title,
            WithBlocked);

        Assert.Equal(ListingCategoryDecisionKind.Override, decision.Kind);
        Assert.Contains("blocked remap", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decide_AiNotValidated_KeepsCurrentEvenInRiskyBranch()
    {
        var decision = ListingCategoryGuardrail.Decide(
            CostumeTops,
            new ListingCategoryInfo("999999", "Ghost category", null),
            aiValidated: false,
            "Women's Lace Corset Top",
            Defaults);

        Assert.Equal(ListingCategoryDecisionKind.Keep, decision.Kind);
    }

    [Theory]
    [InlineData("Women's Lace Corset Top")]
    [InlineData("Women's Halloween Costume Corset Top")]
    public void Decide_BothSidesRisky_TakesTheAiLeaf(string title)
    {
        // 903824 cannot be published into: 4Seller lists six values for its mandatory `100401` and
        // TikTok refuses every one as a custom value name. A sibling costume leaf publishes normally,
        // so the AI's answer wins even when the title really does say costume.
        var decision = ListingCategoryGuardrail.Decide(
            CostumeTops,
            new ListingCategoryInfo("904464", "Costume Bodysuits", "601152/842632/843656/904464"),
            aiValidated: true,
            title,
            Defaults);

        Assert.Equal(ListingCategoryDecisionKind.Override, decision.Kind);
    }

    [Fact]
    public void Decide_SafeCategoryDisagreeingWithAi_KeepsCurrent()
    {
        var decision = ListingCategoryGuardrail.Decide(
            new ListingCategoryInfo("601302", "Women's T-shirts", "601152/842248/601302"),
            TanksAndCamis,
            aiValidated: true,
            "Women's Cotton Tee",
            Defaults);

        Assert.Equal(ListingCategoryDecisionKind.Keep, decision.Kind);
    }
}
