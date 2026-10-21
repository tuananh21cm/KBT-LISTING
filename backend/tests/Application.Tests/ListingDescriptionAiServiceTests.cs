using Application.DTOs.Listing;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingDescriptionAiServiceTests
{
    [Fact]
    public void MarkdownLiteToHtml_RendersHeadingsAndLeftAlignedPlainBullets()
    {
        var markdown = "A breezy two-piece set.\n\n"
                       + "## Why You Will Love It\n"
                       + "- Soft — breathable fabric\n"
                       + "- Relaxed vacation fit\n\n"
                       + "Thanks for shopping with us.";

        var html = ListingDescriptionAiService.MarkdownLiteToHtml(markdown);

        Assert.Equal(
            "<p>A breezy two-piece set.</p>"
            + "<h3>Why You Will Love It</h3>"
            + "<p>- Soft — breathable fabric</p>"
            + "<p>- Relaxed vacation fit</p>"
            + "<p>Thanks for shopping with us.</p>",
            html);
    }

    [Fact]
    public void MarkdownLiteToHtml_SplitsHeadingFromBulletsWithoutBlankLine()
    {
        // Models routinely omit the blank line — bullets must still become left-aligned lines.
        var html = ListingDescriptionAiService.MarkdownLiteToHtml(
            "**Product Details**\n- Cotton blend\n- Machine washable");

        Assert.Equal(
            "<h3>Product Details</h3>"
            + "<p>- Cotton blend</p>"
            + "<p>- Machine washable</p>",
            html);
    }

    [Fact]
    public void MarkdownLiteToHtml_FlattensBoldMarkersToPlainText()
    {
        // TikTok Shop strips emphasis; leftover **markers** would look uneven.
        var html = ListingDescriptionAiService.MarkdownLiteToHtml(
            "**Thank you for shopping with us!** Support is here if you have questions.");

        Assert.Equal(
            "<p>Thank you for shopping with us! "
            + "Support is here if you have questions.</p>",
            html);
    }

    [Fact]
    public void MarkdownLiteToHtml_BoldedSentenceAloneStaysAParagraph()
    {
        // Trailing punctuation marks it as a sentence — only label-like lines become headings.
        var html = ListingDescriptionAiService.MarkdownLiteToHtml(
            "**Thank you for shopping with us!**");

        Assert.Equal("<p>Thank you for shopping with us!</p>", html);
    }

    [Fact]
    public void MarkdownLiteToHtml_FlattensBoldInsideBullets()
    {
        var html = ListingDescriptionAiService.MarkdownLiteToHtml(
            "- **Soft woven** — holds its shape all day");

        Assert.Equal("<p>- Soft woven — holds its shape all day</p>", html);
    }

    [Fact]
    public void MarkdownLiteToHtml_EncodesHtmlFromModelOutput()
    {
        var html = ListingDescriptionAiService.MarkdownLiteToHtml("<script>alert(1)</script>");

        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>", html);
    }

    [Fact]
    public void NormalizeBulletLeadIns_SeparatesBoldLabelFromExplanation()
    {
        // Without the dash the strong element runs straight into a capitalized clause,
        // which reads as two sentences crushed together.
        var normalized = ListingDescriptionAiService.NormalizeBulletLeadIns(
            "- **Easy two-piece style** Takes the guesswork out of getting dressed.");

        Assert.Equal(
            "- **Easy two-piece style** — takes the guesswork out of getting dressed.",
            normalized);
    }

    [Fact]
    public void NormalizeBulletLeadIns_LeavesExistingSeparatorsAndNonBulletsAlone()
    {
        var markdown = "- **Textured woven fabric** — holds its shape all day\n"
                       + "- Colors: Red, Black\n"
                       + "**Thank you for shopping with us!** Support is here for you.";

        Assert.Equal(markdown, ListingDescriptionAiService.NormalizeBulletLeadIns(markdown));
    }

    [Fact]
    public void NormalizeBulletLeadIns_KeepsBoldedSentenceBulletUntouched()
    {
        // Too long to be a feature label, so the bold is deliberate emphasis.
        const string line = "- **This whole clause was bolded on purpose** and continues here.";

        Assert.Equal(line, ListingDescriptionAiService.NormalizeBulletLeadIns(line));
    }

    [Fact]
    public void CapitalizeLineStarts_FixesBlocksHeadingsAndBullets()
    {
        // The model opens with the primary keyword and writes it lower case like a search term.
        var markdown = "women's strapless ruffle pants set — a bandeau top and slim pants.\n\n"
                       + "## why you will love it\n"
                       + "- **made to mix** — wear both pieces together\n"
                       + "- colors: Red, Black";

        Assert.Equal(
            "Women's strapless ruffle pants set — a bandeau top and slim pants.\n\n"
            + "## Why you will love it\n"
            + "- **Made to mix** — wear both pieces together\n"
            + "- Colors: Red, Black",
            ListingDescriptionAiService.CapitalizeLineStarts(markdown));
    }

    [Fact]
    public void CapitalizeLineStarts_FixesParagraphAfterHeading()
    {
        // The Great For paragraph carries the keyword and came out lower case too.
        var html = ListingDescriptionAiService.CapitalizeLineStarts(
            "## Great For\nwomen's ruffle pants set works for beach trips.");

        Assert.Equal("## Great For\nWomen's ruffle pants set works for beach trips.", html);
    }

    [Fact]
    public void CapitalizeLineStarts_LeavesSentenceContinuationsAlone()
    {
        // A second line inside the same paragraph continues the sentence above it.
        const string markdown = "This set travels well,\nand it packs down small.";

        Assert.Equal(markdown, ListingDescriptionAiService.CapitalizeLineStarts(markdown));
    }

    [Fact]
    public void CapitalizeLineStarts_KeepsBulletExplanationLowerCase()
    {
        // Only the label may be capitalized — the text after the dash stays a continuation.
        const string line = "- **Textured woven** — holds its shape all day";

        Assert.Equal(line, ListingDescriptionAiService.CapitalizeLineStarts(line));
    }

    [Fact]
    public void BuildProductFacts_ListsCuratedDataAndDropsNoBrand()
    {
        var facts = ListingDescriptionAiService.BuildProductFacts(new RewriteListingDescriptionRequest
        {
            Title = "Ruffled top set",
            Description = "x",
            CategoryName = "Womenswear > Sets",
            BrandName = "No Brand",
            Colors = ["Black", "black", " White "],
            Sizes = ["S", "M"],
            Attributes =
            [
                new RewriteListingAttributeDto { Name = "Material", Value = "95% Polyester" },
                new RewriteListingAttributeDto { Name = "Fit", Value = "  " }
            ]
        });

        Assert.Equal(
            "Category: Womenswear > Sets\n"
            + "Available colors: Black, White\n"
            + "Available sizes: S, M\n"
            + "Material: 95% Polyester",
            facts);
    }

    [Fact]
    public void BuildProductFacts_DropsValuesNoShopperSearchesFor()
    {
        var facts = ListingDescriptionAiService.BuildProductFacts(new RewriteListingDescriptionRequest
        {
            Title = "Ruffled top set",
            Description = "x",
            Colors = ["Multicolor", "As Picture", "Navy Blue"],
            Attributes =
            [
                new RewriteListingAttributeDto { Name = "Pattern", Value = "None" },
                new RewriteListingAttributeDto { Name = "Material", Value = "Woven" }
            ]
        });

        Assert.Equal(
            "Available colors: Navy Blue\nMaterial: Woven",
            facts);
    }

    [Fact]
    public void BuildProductFacts_DropsFitSpecThatContradictsTheTitle()
    {
        var facts = ListingDescriptionAiService.BuildProductFacts(new RewriteListingDescriptionRequest
        {
            Title = "Women's Asymmetrical Bodycon Mini Dress",
            Description = "x",
            Attributes =
            [
                new RewriteListingAttributeDto { Name = "Fit", Value = "Regular" },
                new RewriteListingAttributeDto { Name = "Material", Value = "Polyester" }
            ]
        });

        Assert.Equal("Material: Polyester", facts);
    }

    [Fact]
    public void BuildProductFacts_KeepsFitSpecWhenTheTitleSaysNothingAboutShape()
    {
        var facts = ListingDescriptionAiService.BuildProductFacts(new RewriteListingDescriptionRequest
        {
            Title = "Women's Asymmetrical Mini Dress",
            Description = "x",
            Attributes = [new RewriteListingAttributeDto { Name = "Fit", Value = "Regular" }]
        });

        Assert.Equal("Fit: Regular", facts);
    }

    [Fact]
    public void BuildProductFacts_KeepsFitSpecTheTitleAgreesWith()
    {
        // "Wide Leg" and "Wide-Leg" are the same shape, so there is nothing to reconcile.
        var facts = ListingDescriptionAiService.BuildProductFacts(new RewriteListingDescriptionRequest
        {
            Title = "Women's Wide-Leg Linen Pants",
            Description = "x",
            Attributes = [new RewriteListingAttributeDto { Name = "Fit Type", Value = "Wide Leg" }]
        });

        Assert.Equal("Fit Type: Wide Leg", facts);
    }

    [Fact]
    public void BuildProductFacts_DropsMaterialThatContradictsTheTitle()
    {
        // TikTok rejects linen-in-title + polyester-in-attribute; facts must not feed the model polyester.
        var facts = ListingDescriptionAiService.BuildProductFacts(new RewriteListingDescriptionRequest
        {
            Title = "Women's Oversized Linen Sweatshirt",
            Description = "x",
            Attributes =
            [
                new RewriteListingAttributeDto { Name = "Material", Value = "Polyester" },
                new RewriteListingAttributeDto { Name = "Season", Value = "Fall" }
            ]
        });

        Assert.Equal("Season: Fall", facts);
        Assert.DoesNotContain("Polyester", facts);
    }

    [Fact]
    public void BuildProductFacts_KeepsMaterialWhenTitleNamesNoFibre()
    {
        var facts = ListingDescriptionAiService.BuildProductFacts(new RewriteListingDescriptionRequest
        {
            Title = "Women's Oversized Crewneck Sweatshirt",
            Description = "x",
            Attributes = [new RewriteListingAttributeDto { Name = "Material", Value = "Polyester" }]
        });

        Assert.Equal("Material: Polyester", facts);
    }

    [Fact]
    public void BuildProductFacts_KeepsMaterialWhenTitleAndAttributeAgree()
    {
        var facts = ListingDescriptionAiService.BuildProductFacts(new RewriteListingDescriptionRequest
        {
            Title = "Women's Wide-Leg Linen Pants",
            Description = "x",
            Attributes = [new RewriteListingAttributeDto { Name = "Material", Value = "Linen" }]
        });

        Assert.Equal("Material: Linen", facts);
    }

    [Fact]
    public void HasMaterialConflict_DetectsLinenTitleAgainstPolyesterAttribute()
    {
        Assert.True(ListingDescriptionAiService.HasMaterialConflict(new RewriteListingDescriptionRequest
        {
            Title = "Women's Oversized Linen Sweatshirt",
            Description = "x",
            Attributes = [new RewriteListingAttributeDto { Name = "Material", Value = "Polyester" }]
        }));
    }

    [Fact]
    public void StripFabricMentionLines_DropsFabricBulletsKeepsOthers()
    {
        var text = "Women's linen sweatshirt for fall weekends.\n\n"
                   + "## Product Details\n"
                   + "- Soft crew neck — sits clean under a jacket\n"
                   + "- Knit polyester fabric — holds its shape all day\n"
                   + "- Available colors: Black, Cream\n";

        var stripped = ListingDescriptionAiService.StripFabricMentionLines(text);

        Assert.DoesNotContain("Knit polyester", stripped);
        Assert.Contains("Soft crew neck", stripped);
        Assert.Contains("Available colors", stripped);
    }

    [Fact]
    public void DropFabricMetaLines_RemovesTermsThatNameAFibre()
    {
        var kept = ListingDescriptionAiService.DropFabricMetaLines(
        [
            "women's oversized sweatshirt",
            "linen sweatshirt",
            "crew neck pullover"
        ]);

        Assert.Equal(
            ["women's oversized sweatshirt", "crew neck pullover"],
            kept);
    }

    [Fact]
    public void StripShippingSection_RemovesShippingBlock()
    {
        var text = "Opening hook.\n\n## Care Tips\n- Machine wash cold\n\n"
                   + "Browse the color options to find your favorite.\n\n"
                   + "## Shipping\n\nDelivery typically takes 6–8 days. If you need it within 2–3 days "
                   + "for an event, deadline, or last-minute plans, please order with that timing in mind "
                   + "since rush needs may not arrive in time—thank you for understanding.\n\n"
                   + "**Thank you for shopping with us!** Support is here for you.";

        var stripped = ListingDescriptionAiService.StripShippingSection(text);

        Assert.Equal(
            "Opening hook.\n\n## Care Tips\n- Machine wash cold\n\n"
            + "Browse the color options to find your favorite.\n\n"
            + "**Thank you for shopping with us!** Support is here for you.",
            stripped);
    }

    [Fact]
    public void StripShippingSection_LeavesDescriptionWithoutShippingAlone()
    {
        var text = "Opening hook.\n\n## Care Tips\n- Machine wash cold\n\n"
                   + "Pick your color and size today.\n\n"
                   + "**Thank you for shopping with us!** Support is here for you.";

        Assert.Equal(text, ListingDescriptionAiService.StripShippingSection(text));
    }

    [Fact]
    public void StripCareTipsSection_RemovesCareTipsBlock()
    {
        var text = "Opening hook.\n\n"
                   + "## Care Tips\n"
                   + "- Machine wash cold — tumble dry low\n"
                   + "- Do not bleach\n\n"
                   + "Thank you for shopping with us! Support is here.\n\n"
                   + "#womentanktop #tanktop #haltertop";

        var stripped = ListingDescriptionAiService.StripCareTipsSection(text);

        Assert.DoesNotContain("Care Tips", stripped);
        Assert.DoesNotContain("Machine wash", stripped);
        Assert.Contains("Opening hook", stripped);
        Assert.Contains("#womentanktop", stripped);
    }

    [Fact]
    public void StripColorsLines_DropsColorsSpecButKeepsProse()
    {
        var text = "A soft knit top in a solid color finish.\n\n"
                   + "## Product Details\n"
                   + "- Soft knit — holds its shape\n"
                   + "- Colors: Red, Black, Navy\n"
                   + "- Available colours: Pink / Blue\n"
                   + "- Sizes: S, M, L\n\n"
                   + "Thank you for shopping with us!";

        var stripped = ListingDescriptionAiService.StripColorsLines(text);

        Assert.DoesNotContain("Colors:", stripped);
        Assert.DoesNotContain("Available colours:", stripped);
        Assert.Contains("solid color finish", stripped);
        Assert.Contains("Sizes: S, M, L", stripped);
    }

    [Fact]
    public void StripSizesLines_DropsSizesSpecButKeepsFitAdvice()
    {
        var text = "A soft knit top for layering.\n\n"
                   + "## Product Details\n"
                   + "- Soft knit — holds its shape\n"
                   + "- True to size — the woven has no stretch, so size up if you are between sizes\n"
                   + "- Sizes: XS, S, M, L\n"
                   + "- Available sizes: S / M / L\n"
                   + "Sizes: One Size\n\n"
                   + "Thank you for shopping with us!";

        var stripped = ListingDescriptionAiService.StripSizesLines(text);

        Assert.DoesNotContain("Sizes: XS, S, M, L", stripped);
        Assert.DoesNotContain("Available sizes:", stripped);
        Assert.DoesNotContain("Sizes: One Size", stripped);
        Assert.Contains("size up if you are between sizes", stripped);
        Assert.Contains("Soft knit", stripped);
    }

    [Fact]
    public void StripForbiddenDescriptionSections_ChainsAllStrips()
    {
        var text = "Hook.\n\n"
                   + "## Care Tips\n\n- Wash cold\n\n"
                   + "## Shipping\n\nArrives in a week.\n\n"
                   + "- Colors: Red, Black\n\n"
                   + "- Sizes: XS, S, M, L\n\n"
                   + "- Soft knit — breathable\n\n"
                   + "Thank you for shopping with us!";

        var stripped = ListingDescriptionAiService.StripForbiddenDescriptionSections(text);

        Assert.DoesNotContain("Care Tips", stripped);
        Assert.DoesNotContain("Shipping", stripped);
        Assert.DoesNotContain("Colors:", stripped);
        Assert.DoesNotContain("Sizes:", stripped);
        Assert.Contains("Soft knit", stripped);
    }

    [Fact]
    public void TrimPreservingClosing_KeepsThankYouWhenDroppingMiddle()
    {
        var text = "Opening hook.\n\n"
                   + "## Why You Will Love It\n- Soft fabric\n\n"
                   + "## Product Details\n- Easy care\n\n"
                   + "**Thank you for shopping with us!** Support is here for you.";

        var trimmed = ListingDescriptionAiService.TrimPreservingClosing(text, 160);

        Assert.Contains("**Thank you for shopping with us!**", trimmed);
        Assert.StartsWith("Opening hook.", trimmed);
    }

    [Fact]
    public void TrimPreservingClosing_DropsMiddleBlocksAndKeepsClosing()
    {
        var text = "Opening hook.\n\nMiddle one.\n\nMiddle two.\n\nThanks for shopping with us.";

        var trimmed = ListingDescriptionAiService.TrimPreservingClosing(text, 50);

        Assert.StartsWith("Opening hook.", trimmed);
        Assert.EndsWith("Thanks for shopping with us.", trimmed);
        Assert.True(trimmed.Length <= 50);
    }

    [Fact]
    public void MarkdownLiteToHtml_KeepsHashtagLineAsParagraphNotHeading()
    {
        // "# tag …" would match HeadingRegex (# + space); clean "#a #b" must stay a <p>.
        var html = ListingDescriptionAiService.MarkdownLiteToHtml(
            "Opening hook.\n\n"
            + "Thank you for shopping with us! Support is here.\n\n"
            + "#womentanktop #tanktop #haltertop #summeroutfit #sleevelesstop");

        Assert.Contains(
            "<p>#womentanktop #tanktop #haltertop #summeroutfit #sleevelesstop</p>",
            html);
        Assert.DoesNotContain("<h3>", html.Split("</p>").Last());
        Assert.DoesNotContain("<h3>#womentanktop", html);
    }

    [Fact]
    public void CapitalizeLineStarts_LeavesHashtagLineLowercase()
    {
        var markdown = "women's halter tank top — easy summer layer.\n\n"
                       + "#womentanktop #tanktop #haltertop";

        var capitalized = ListingDescriptionAiService.CapitalizeLineStarts(markdown);

        Assert.StartsWith("Women's halter tank top", capitalized);
        Assert.EndsWith("#womentanktop #tanktop #haltertop", capitalized);
        Assert.DoesNotContain("#Womentanktop", capitalized);
    }

    [Fact]
    public void SanitizeHashtagBlock_DropsBannedTagsDedupesAndCaps()
    {
        // Valuable tags first, then filler past the 25-cap, then banned/dupes at the end.
        var tags = "#tanktop #TankTop #womentanktop #haltertop #summeroutfit "
                   + string.Join(' ', Enumerable.Range(1, 30).Select(i => $"#tag{i}"))
                   + " #shein #FreeShipping #2026";

        var sanitized = ListingDescriptionAiService.SanitizeHashtagBlock(
            "Opening hook.\n\nThank you for shopping with us!\n\n" + tags);

        Assert.StartsWith("Opening hook.", sanitized);
        Assert.Contains("Thank you for shopping with us!", sanitized);
        Assert.DoesNotContain("#shein", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#freeshipping", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#2026", sanitized);
        Assert.Contains("#tanktop", sanitized);
        Assert.Contains("#womentanktop", sanitized);
        // StartsWith("temu") must not false-positive costume
        Assert.Contains(
            "#costume",
            ListingDescriptionAiService.SanitizeHashtagBlock(
                "Hook.\n\n#costume #halloweencostume #shein"));

        var hashLine = sanitized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)[^1];
        var count = hashLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(count <= 25, $"expected ≤25 hashtags, got {count}");
        // #tanktop and #TankTop collapse to one
        Assert.Equal(1, hashLine.Split(' ').Count(t =>
            t.Equals("#tanktop", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void StripFabricFromHashtagLine_DropsFibreTagsOnMaterialConflict()
    {
        var text = "Opening hook.\n\n"
                   + "Thank you for shopping with us!\n\n"
                   + "#womentanktop #linendress #tanktop #polyesterblend #summeroutfit";

        var cleaned = ListingDescriptionAiService.StripFabricFromHashtagLine(text);

        Assert.DoesNotContain("#linendress", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#polyesterblend", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#womentanktop", cleaned);
        Assert.Contains("#tanktop", cleaned);
        Assert.Contains("#summeroutfit", cleaned);
    }

    [Fact]
    public void TrimPreservingClosing_KeepsHashtagLineWhenCutting()
    {
        const string hashLine =
            "#womentanktop #tanktop #haltertop #summeroutfit #sleevelesstop #camisole #knittop";
        var text = "Opening hook with enough words to survive.\n\n"
                   + "## Why You Will Love It\n- Soft fabric detail here\n\n"
                   + "## Product Details\n- Easy care wash cold\n\n"
                   + "## Great For\nWear it to brunch or pack it for a beach weekend trip.\n\n"
                   + "Thank you for shopping with us! Support is here for colors and sizing.\n\n"
                   + hashLine;

        var trimmed = ListingDescriptionAiService.TrimPreservingClosing(text, 220);

        Assert.EndsWith(hashLine, trimmed);
        Assert.Contains("Thank you for shopping with us!", trimmed);
        Assert.StartsWith("Opening hook", trimmed);
    }

    [Fact]
    public void SanitizeTitle_StripsMarketplaceNames()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "SHEIN Women's Strapless Ruffle Pants Set for Beach Vacation on Lazada",
            "Women's Strapless Ruffle Pants Set for Beach Vacation");

        Assert.Equal(
            "Women's Strapless Ruffle Pants Set for Beach Vacation",
            title);
    }

    [Fact]
    public void SanitizeTitle_StripsEmojiAndPromoWording()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "🔥 HOT SALE 50% OFF Women's Linen Tube Top Matching Set for Summer Vacation 🔥",
            "Women's Linen Tube Top Matching Set for Summer Vacation");

        Assert.Equal(
            "Women's Linen Tube Top Matching Set for Summer Vacation",
            title);
    }

    [Fact]
    public void SanitizeTitle_AppliesTitleCaseKeepingPrepositionsLower()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "WOMENS RIBBED KNIT WIDE-LEG PANTS SET FOR TRAVEL AND WEEKEND",
            "Womens Ribbed Knit Wide-Leg Pants Set for Travel and Weekend");

        Assert.Equal(
            "Womens Ribbed Knit Wide-Leg Pants Set for Travel and Weekend",
            title);
    }

    [Fact]
    public void SanitizeTitle_TrimsOverlongTitleAtWordBoundary()
    {
        var raw = "Women's Strapless Ruffle Pants Set "
                  + string.Join(' ', Enumerable.Range(1, 40).Select(i => $"Filler{i}"));

        var title = ListingDescriptionAiService.SanitizeTitle(raw, "fallback");

        Assert.True(raw.Length > 255);
        Assert.True(title.Length <= 255);
        Assert.StartsWith("Women's Strapless Ruffle Pants Set", title, StringComparison.Ordinal);

        // The cut may not land mid-word: the last token must still be a whole source word.
        var lastWord = title.Split(' ')[^1];
        Assert.Contains($" {lastWord} ", $" {raw} ", StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeTitle_KeepsTitlesBetween150And255()
    {
        var raw =
            "Women's Wide Leg Cargo Pants Drawstring Waist Ankle Ties Relaxed Fit "
            + "Parachute Trousers Side Pockets Hiking Travel Beach Vacation Weekend "
            + "Getaway Outdoor Walking Casual Lounge";

        var title = ListingDescriptionAiService.SanitizeTitle(raw, raw);

        Assert.True(raw.Length > 150);
        Assert.True(raw.Length <= 255);
        Assert.True(title.Length > 150);
        Assert.True(title.Length <= 255);
        Assert.StartsWith("Women's Wide Leg Cargo Pants", title, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeTitle_FallsBackToCleanedOriginalWhenModelReturnsNothing()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            string.Empty,
            "Shein Women's Floral Chiffon Midi Dress for Summer Weddings");

        Assert.Equal(
            "Women's Floral Chiffon Midi Dress for Summer Weddings",
            title);
    }

    [Fact]
    public void SanitizeTitle_KeepsShortModelTitleOverEmptyFallback()
    {
        var title = ListingDescriptionAiService.SanitizeTitle("Linen Tube Top Set", "   ");

        Assert.Equal("Linen Tube Top Set", title);
    }

    [Fact]
    public void SanitizeTitle_DropsBoyQualifierThatMeansUnderwearInTheUs()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "Women's Mesh Side Phone Pocket Boy Shorts for Gym Workout and Yoga",
            "fallback");

        Assert.Equal(
            "Women's Mesh Side Phone Pocket Shorts for Gym Workout and Yoga",
            title);
    }

    [Fact]
    public void SanitizeTitle_RestoresHighWaistWhenTheSourceUsedIt()
    {
        // high rise is trade vocabulary; high waisted is what US shoppers type.
        var title = ListingDescriptionAiService.SanitizeTitle(
            "Women's High-Rise Cycling Shorts with Side Stripes for Gym Workouts",
            "Easithlete Women's High Waist Cycling Shorts, Side Stripe");

        Assert.Equal(
            "Women's High Waist Cycling Shorts with Side Stripes for Gym Workouts",
            title);
    }

    [Fact]
    public void SanitizeTitle_LeavesHighRiseAloneWhenTheSourceNeverSaidHighWaist()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "Women's High Rise Denim Bermuda Shorts with Button Front for Casual Wear",
            "Lady Denim Bermuda Shorts Button Front Summer");

        Assert.Contains("High Rise", title, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRepeatedWord_CatchesTheHeadNounSpentTwice()
    {
        Assert.Equal(
            "Shorts",
            ListingDescriptionAiService.FindRepeatedWord(
                "Women's High Waist Yoga Shorts - Mesh Side Phone Pocket Gym Shorts"));
    }

    [Fact]
    public void FindRepeatedWord_TreatsSingularAndPluralAsTheSameWord()
    {
        Assert.Equal(
            "Short",
            ListingDescriptionAiService.FindRepeatedWord(
                "Women's Biker Shorts - Seamless Wide Waistband Yoga Workout Short"));
    }

    [Fact]
    public void FindRepeatedWord_IgnoresRepeatedPrepositions()
    {
        Assert.Null(
            ListingDescriptionAiService.FindRepeatedWord(
                "Women's Ribbed Knit Tube Top Set for Beach and for Brunch"));
    }

    [Fact]
    public void FindWeakLongTail_CatchesDailyWearFiller()
    {
        Assert.Equal(
            "Daily Wear",
            ListingDescriptionAiService.FindWeakLongTail(
                "Maternity Round Neck Short Sleeve T-Shirt with Pleats for Daily Wear"));
    }

    [Fact]
    public void FindWeakLongTail_CatchesLayeringWearAndAthleticStyle()
    {
        Assert.Equal(
            "Layering Wear",
            ListingDescriptionAiService.FindWeakLongTail(
                "Women's Shapewear Shorts High Waist Button Front for Casual Layering Wear"));

        Assert.Equal(
            "Athletic Style",
            ListingDescriptionAiService.FindWeakLongTail(
                "Women's High Waist Sports Shorts Breathable Stretch Athletic Style"));
    }

    [Fact]
    public void FindWeakLongTail_AllowsRealUsUseCases()
    {
        Assert.Null(
            ListingDescriptionAiService.FindWeakLongTail(
                "Plus Size Women's Striped Halter Dress Tie Side Mini for Beach Vacation"));

        Assert.Null(
            ListingDescriptionAiService.FindWeakLongTail(
                "Women's High Waist Active Shorts Seamless Scrunch Bum for Pilates Workout"));
    }

    [Fact]
    public void FindDroppedSourceHeadlineToken_CatchesPaisleyDroppedFromSource()
    {
        const string source =
            "EZwear Summer Y2k Vintage Grunge Vacation Brown Paisley Top Women Retro Print Through Top.";
        const string rewritten =
            "Women's Halter Backless Top Asymmetrical Cami Y2K Vintage Grunge Retro Print "
            + "Boho Knit Polyester Slim Fit Regular Length Non Sheer Medium Stretch for Vacation";

        Assert.Equal(
            "Paisley",
            ListingDescriptionAiService.FindDroppedSourceHeadlineToken(rewritten, source),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindDroppedSourceHeadlineToken_AllowsBrandSeasonAndColorToDrop()
    {
        Assert.Null(
            ListingDescriptionAiService.FindDroppedSourceHeadlineToken(
                "Women's Paisley Top Y2K Vintage Grunge Retro Print Through for Vacation",
                "EZwear Summer Y2k Vintage Grunge Vacation Brown Paisley Top Women Retro Print Through Top."));
    }

    [Fact]
    public void FindSpecSheetJunk_CatchesNonSheerAndRegularLength()
    {
        Assert.Equal(
            "Regular Length",
            ListingDescriptionAiService.FindSpecSheetJunk(
                "Women's Paisley Top Regular Length Non Sheer Medium Stretch Polyester",
                "EZwear Summer Y2k Vintage Grunge Vacation Brown Paisley Top"),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindSpecSheetJunk_CatchesSlimFitting()
    {
        Assert.Equal(
            "Slim-Fitting",
            ListingDescriptionAiService.FindSpecSheetJunk(
                "Women's Blue Lace Patchwork Backless Halter Top Slim-Fitting for Vacation",
                "Women's Blue Lace Patchwork Backless Casual Halter Top."),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindInventedGarmentType_CatchesMatchingSetOnASingleTop()
    {
        Assert.Equal(
            "matching set",
            ListingDescriptionAiService.FindInventedGarmentType(
                "Women's Bandeau Top Lace Scarf Hem Matching Set Top for Vacation",
                "Women Summer Vacation Lace Bandeau Top With Scarf Hem."),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindInventedGarmentType_CatchesCamiOnABandeau()
    {
        Assert.Equal(
            "cami",
            ListingDescriptionAiService.FindInventedGarmentType(
                "Women's Bandeau Top Lace Scarf Hem Cropped Cami Sleeveless for Vacation",
                "Women Summer Vacation Lace Bandeau Top With Scarf Hem."),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindInventedGarmentType_AllowsBandeauTubeSynonymAndSourcePhrases()
    {
        Assert.Null(
            ListingDescriptionAiService.FindInventedGarmentType(
                "Women's Lace Bandeau Top Scarf Hem Summer Vacation Lace Bandeau Scarf Hem Top Tube Top for Vacation",
                "Women Summer Vacation Lace Bandeau Top With Scarf Hem."));
    }

    [Fact]
    public void FindInventedGarmentType_AllowsPantsTrousersSynonym()
    {
        Assert.Null(
            ListingDescriptionAiService.FindInventedGarmentType(
                "Women's Wide Leg Cargo Pants Parachute Trousers for Hiking Travel",
                "Women's Wide Leg Cargo Pants Drawstring Waist"));
    }

    [Fact]
    public void FindInventedGarmentType_CatchesDressOnATop()
    {
        Assert.Equal(
            "dress",
            ListingDescriptionAiService.FindInventedGarmentType(
                "Women's Lace Bandeau Top Mini Dress for Vacation",
                "Women Summer Vacation Lace Bandeau Top With Scarf Hem."),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindInventedGarmentType_DoesNotTreatShortSleeveAsShorts()
    {
        Assert.Null(
            ListingDescriptionAiService.FindInventedGarmentType(
                "Women's Lace Bandeau Top Scarf Hem Short Sleeve Tube Top for Vacation",
                "Women Summer Vacation Lace Bandeau Top With Scarf Hem."));
    }

    [Fact]
    public void FindInventedGarmentType_AllowsMatchingSetWhenSourceIsASet()
    {
        Assert.Null(
            ListingDescriptionAiService.FindInventedGarmentType(
                "Women's Linen Tube Top Matching Set for Summer Vacation",
                "Women's Linen Tube Top Matching Set"));
    }

    [Fact]
    public void FindInventedGarmentType_TreatsTwoPiecePhraseAsASet()
    {
        Assert.Null(
            ListingDescriptionAiService.FindInventedGarmentType(
                "Women's 2 Piece Bandeau Matching Set for Vacation",
                "Women's 2 Piece Lace Bandeau Top With Scarf"));
    }

    [Fact]
    public void PickSaferTitle_PrefersSameProductOverLongerInventedTitle()
    {
        const string source = "Women Summer Vacation Lace Bandeau Top With Scarf Hem.";
        const string invented =
            "Women's Bandeau Top Lace Scarf Hem Asymmetrical Neck Cropped Cami Sleeveless Matching Set Top for Vacation";
        const string sameProduct =
            "Women's Lace Bandeau Top Scarf Hem Summer Vacation Lace Bandeau Scarf Hem Top Tube Top for Vacation";

        Assert.Equal(
            sameProduct,
            ListingDescriptionAiService.PickSaferTitle(source, invented, sameProduct));
    }

    [Fact]
    public void FindSpecSheetJunk_AllowsPhraseWhenSourceAlreadyUsesIt()
    {
        Assert.Null(
            ListingDescriptionAiService.FindSpecSheetJunk(
                "Women's Polyester Blend Track Pants for Gym Training",
                "Shein Women's Polyester Blend Track Pants"));
    }

    [Fact]
    public void FindMissingSourceKeyword_CatchesTheAttributeTheSourceAdvertised()
    {
        // The supplier said Seamless twice; seamless set is a query in its own right.
        Assert.Equal(
            "Seamless",
            ListingDescriptionAiService.FindMissingSourceKeyword(
                "Women's Running Set - Backless Tank Top and Shorts 2 Piece Outfit",
                "Dewbera 2pcs Women's Seamless Ribbed Backless Sports Tank Top Seamless Shorts Set"));
    }

    [Fact]
    public void FindMissingSourceKeyword_ReportsNothingWhenTheAttributeSurvived()
    {
        Assert.Null(
            ListingDescriptionAiService.FindMissingSourceKeyword(
                "Women's Seamless Ribbed Running Set - Backless Tank Top and Shorts 2 Piece Outfit",
                "Dewbera 2pcs Women's Seamless Ribbed Backless Sports Tank Top Seamless Shorts Set"));
    }

    [Fact]
    public void FindMissingSourceKeyword_IgnoresAttributesTheSourceNeverClaimed()
    {
        Assert.Null(
            ListingDescriptionAiService.FindMissingSourceKeyword(
                "Women's Running Set - Backless Tank Top and Shorts 2 Piece Outfit",
                "Lady 2pcs Sports Tank Top Shorts Set"));
    }

    [Fact]
    public void FindMissingSourceKeyword_MatchesHighWaistedAgainstHighWaist()
    {
        Assert.Equal(
            "High Waist",
            ListingDescriptionAiService.FindMissingSourceKeyword(
                "Women's Biker Shorts - Wide Waistband Yoga Workout Bottoms",
                "Slayform Women's High Waisted Gym Biker Shorts for Cycling"));
    }

    [Fact]
    public void SanitizeTitle_KeepsALineSpelledAsTheSearchedTerm()
    {
        // Title casing treats the lone A as an article; a-line dress is a real query.
        Assert.Equal(
            "Women's Floral Mini Dress - Spaghetti Strap Lace Trim A-Line Outfit",
            ListingDescriptionAiService.SanitizeTitle(
                "Women's Floral Mini Dress - Spaghetti Strap Lace Trim A Line Summer Outfit",
                "SHEIN Women's Floral A-Line Mini Dress"));
    }

    [Fact]
    public void SanitizeTitle_KeepsVNeckHyphenated()
    {
        Assert.Contains(
            "V-Neck",
            ListingDescriptionAiService.SanitizeTitle(
                "Plus Size Yellow Mesh V Neck Dress with Ruffled Sleeve for Weddings",
                "Plus Size Yellow Mesh V-Neck Dress"));
    }

    [Fact]
    public void SanitizeTitle_StripsCatalogYears()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "Women's Off Shoulder Mini Dress 2026 Asymmetric Ruched Ruffle Summer Dress for Holiday",
            "Sweetina 2026 Women's Off Shoulder Pale Yellow Summer Mini Dress");

        Assert.DoesNotContain("2026", title);
        Assert.Contains("Off Shoulder", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeTitle_StripsConcatenatedSupplierSlogans()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "Plus Size Floral Print Camisole Nightdress Summer Casual PerfectSummer for Vacation",
            "Serenescape Plus Size Floral Print Camisole Nightdress, Summer Casual PerfectSummer");

        Assert.DoesNotContain("PerfectSummer", title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Perfect Summer", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nightdress", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeTitle_FoldsDecadeApostropheAfterTitleCase()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "Women's Twist Asymmetric Hem Midi Dress 70's Beach Club Holiday Party Disco Dress for Wedding Guest",
            "Soleia Women's Twist Asymmetric Hem Spaghetti Strap White Summer 70's Beach Club Midi Dress");

        Assert.Contains("70's", title);
        Assert.DoesNotContain("70'S", title);
    }

    [Fact]
    public void SanitizeTitle_NormalizesOverSizeToOversized()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "Women's Plus Size Graphic T-Shirt OverSize T-Shirt Vintage Graphic Tee for Date Night",
            "Coolane Women's Plus Size Graphic Pattern Comfy Cotton OverSize T-Shirt");

        Assert.Contains("Oversized", title);
        Assert.DoesNotContain("OverSize", title);
        Assert.DoesNotContain("Over Size", title);
    }

    [Fact]
    public void StripUnsourcedOccasions_DropsHalloweenTheSourceNeverNamed()
    {
        var title = ListingDescriptionAiService.StripUnsourcedOccasions(
            "Women's Off-The-Shoulder Dress Plus Size Asymmetrical Black Mini Dress for Halloween",
            "SHEIN Essnce Plus-Size Women's Off-The-Shoulder Dress, Diagonal Dress, Black Mini Dress");

        Assert.DoesNotContain("Halloween", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mini Dress", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripUnsourcedOccasions_KeepsChristmasWhenTheSourceAdvertisedIt()
    {
        var title = ListingDescriptionAiService.StripUnsourcedOccasions(
            "Women's Off the Shoulder Long Sleeve T-Shirt Knit Top for Christmas",
            "Coolane Women's Autumn Grunge Knit Off The Shoulder Dark Brown Long Sleeve T-Shirt, Christmas, Top");

        Assert.Contains("Christmas", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripUnsourcedOccasions_DropsBackToSchoolTheSourceNeverNamed()
    {
        var title = ListingDescriptionAiService.StripUnsourcedOccasions(
            "Women's Trench Coat Double-Breasted Casual Long Sleeve Short Coat for Back-To-School",
            "Muchica Women's Woven Solid Color Double-Breasted Casual Long Sleeve Trench Coat, Autumn,Fall");

        Assert.DoesNotContain("School", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripUnsourcedOccasions_DropsVacationAndSummerTheSourceNeverNamed()
    {
        var title = ListingDescriptionAiService.StripUnsourcedOccasions(
            "Women's Blue Lace Patchwork Backless Halter Top for Vacation Sexy Summer Top",
            "Women's Blue Lace Patchwork Backless Casual Halter Top.");

        Assert.DoesNotContain("Vacation", title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Summer", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Halter", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Backless", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripUnsourcedOccasions_DoesNotTreatPerfectSummerAsSummerEvidence()
    {
        var title = ListingDescriptionAiService.StripUnsourcedOccasions(
            "Plus Size Floral Print Camisole Nightdress Summer Casual for Vacation",
            "Serenescape Plus Size Floral Print Camisole Nightdress, Casual PerfectSummer");

        Assert.DoesNotContain("Summer", title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vacation", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nightdress", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripUnsourcedClaims_DropsSexyWhenTheSourceNeverUsedIt()
    {
        var title = ListingDescriptionAiService.StripUnsourcedClaims(
            "Women's Blue Lace Patchwork Backless Casual Halter Sexy Top",
            "Women's Blue Lace Patchwork Backless Casual Halter Top.");

        Assert.DoesNotContain("Sexy", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Halter", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripUnsourcedClaims_KeepsSleevelessOnACami()
    {
        var title = ListingDescriptionAiService.StripUnsourcedClaims(
            "Women's Plus Size Cami Top Sexy Elegant Lace Patchwork Sleeveless Top",
            "SHEIN ICON Plus Size Sexy & Elegant Solid Color Lace Patchwork Ruched Slim Cami Top, For Summer");

        Assert.Contains("Sleeveless", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cami", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripUnsourcedClaims_DropsSleevelessOnALongSleeveTee()
    {
        var title = ListingDescriptionAiService.StripUnsourcedClaims(
            "Women's Plus Size Graphic T-Shirt Oversized Sleeveless Top",
            "Coolane Women's Plus Size Graphic Pattern Comfy Cotton OverSize T-Shirt");

        Assert.DoesNotContain("Sleeveless", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("T-Shirt", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CountNovelTitleStems_TreatsAReshuffleAsZeroCoverage()
    {
        Assert.Equal(
            0,
            ListingDescriptionAiService.CountNovelTitleStems(
                "Women's Dark Brown Knitted Drape Collar Halter Tank Top Sexy Easter Summer Club Party",
                "Easter Summer Women's Dark Brown Knitted Drape Collar Halter Tank Top Club Party Sexy."));
    }

    [Fact]
    public void CountNovelTitleStems_CountsSameProductSynonyms()
    {
        Assert.True(
            ListingDescriptionAiService.CountNovelTitleStems(
                "Women's Halter Tank Top Dark Brown Knitted Drape Collar Halter Neck Knit Tank Sleeveless Top Sexy Easter Summer Club Party",
                "Easter Summer Women's Dark Brown Knitted Drape Collar Halter Tank Top Club Party Sexy.")
            >= 2);
    }

    [Fact]
    public void StripUnsourcedShortDress_DropsWhenSourceIsMini()
    {
        var title = ListingDescriptionAiService.StripUnsourcedShortDress(
            "Women's Mini Dress Floral Print Spaghetti Strap Short Dress Sundress",
            "Floral Print Mini Dress Spaghetti Strap Lace Trim A Line Dress Cute Summer Outfit For Women");

        Assert.DoesNotContain("Short Dress", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mini Dress", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripUnsourcedShortDress_KeepsWhenSourceSaysShort()
    {
        var title = ListingDescriptionAiService.StripUnsourcedShortDress(
            "Plus Size Women's Mini Dress Halter Neck Floral A-Line Short Dress",
            "Travachic Plus Size Women's Halter Neck Sleeveless Floral A-Line Mini Dress, Spring Short");

        Assert.Contains("Short Dress", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeTitle_DropsTrailingWomenWhenWomensAlreadyLeads()
    {
        var title = ListingDescriptionAiService.SanitizeTitle(
            "Women's Mini Dress Floral Print Spaghetti Strap Lace Trim A-Line Cute Summer Outfit Dress Women",
            "Floral Print Mini Dress Spaghetti Strap Lace Trim A Line Dress Cute Summer Outfit For Women");

        Assert.False(title.TrimEnd().EndsWith("Women", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("Women's", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FinishRewriteTitle_DoesNotLeaveOverSizeGlued()
    {
        var title = ListingDescriptionAiService.FinishRewriteTitle(
            "Women's Plus Size Graphic T-Shirt Oversized Comfy Cotton OverSize",
            "Coolane Women's Plus Size Graphic Pattern Comfy Cotton OverSize T-Shirt");

        Assert.DoesNotContain("OverSize", title, StringComparison.Ordinal);
        Assert.Contains("Oversized", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FinishRewriteTitle_DoesNotReinsertDailyWearFromSource()
    {
        var title = ListingDescriptionAiService.FinishRewriteTitle(
            "Women's Off the Shoulder Grunge Knit Long Sleeve T-Shirt Christmas Dark Brown",
            "Coolane Women's Autumn Grunge Sexy Daily Wear Going Out Knit Off The Shoulder Dark Brown Long Sleeve T-Shirt, Christmas, Top");

        Assert.DoesNotContain("Daily Wear", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Christmas", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FillFromSourceTokens_InsertsDroppedSourceWordBeforeTheLongTail()
    {
        var filled = ListingDescriptionAiService.FillFromSourceTokens(
            "Women's Corset Cami Top Hook and Eye Front Mesh Insert Cami Top for Concerts",
            "Sweetina White Corset Hook And Eye Front Mesh Insert Cami Top");

        Assert.Contains("White", filled, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("for Concerts", filled, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sweetina", filled, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FillFromSourceTokens_LeavesATitleAlreadyInBandAlone()
    {
        var title =
            "Women's Plus Size Graphic T-Shirt Oversized T-Shirt Vintage Graphic Tee Asymmetrical Neck "
            + "Oversized T-Shirt Y2K Vintage Streetwear Casual Graphic Pattern Comfy Cotton Summer Fall "
            + "Graphic T-Shirt for Date Night";

        Assert.True(title.Length >= 150);
        Assert.Equal(
            title,
            ListingDescriptionAiService.FillFromSourceTokens(
                title,
                "Coolane Women's Plus Size Summer Fall Y2K Vintage Streetwear Casual Graphic Pattern Comfy Cotton OverSize T-Shirt White Date Night"));
    }

    [Fact]
    public void SplitTitleBlock_SeparatesTheDescriptionFromTheTitle()
    {
        var (body, title) = ListingDescriptionAiService.SplitTitleBlock(
            "A breezy set.\n\n## Care Tips\n- Machine wash — keeps it simple\n\n"
            + "===SEO_TITLE===\n"
            + "Women's Linen Halter Blouse - Cinched Waist Top for Vacation\n");

        Assert.Equal("Women's Linen Halter Blouse - Cinched Waist Top for Vacation", title);
        Assert.DoesNotContain("SEO_TITLE", body);
        Assert.Contains("## Care Tips", body);
    }

    [Fact]
    public void SplitTitleBlock_StripsTheLabelAndQuotesTheModelAddsAnyway()
    {
        var (_, title) = ListingDescriptionAiService.SplitTitleBlock(
            "Body copy.\n===SEO_TITLE===\nTitle: \"Women's Ribbed Tank Top for Summer\".");

        Assert.Equal("Women's Ribbed Tank Top for Summer", title);
    }

    [Fact]
    public void SplitTitleBlock_ReportsNoTitleWhenTheModelSkipsTheSentinel()
    {
        // Falls back to the dedicated title call rather than mining the body for a title.
        var (body, title) = ListingDescriptionAiService.SplitTitleBlock("Just the description.");

        Assert.Equal("Just the description.", body);
        Assert.Equal(string.Empty, title);
    }

    [Fact]
    public void SplitRewriteBlocks_SeparatesTitleSearchTermsAndHighlights()
    {
        var blocks = ListingDescriptionAiService.SplitRewriteBlocks(
            "A breezy set.\n\n## Care Tips\n- Machine wash — keeps it simple\n\n"
            + "===SEO_TITLE===\n"
            + "Women's Linen Halter Blouse\n"
            + "===SEARCH_TERMS===\n"
            + "women's linen blouse\n"
            + "halter blouse\n"
            + "linen halter top\n"
            + "vacation blouse\n"
            + "===PRODUCT_HIGHLIGHTS===\n"
            + "Halter neckline stays put when you move\n"
            + "Linen weave breathes on hot days\n"
            + "Cinched waist shapes without squeezing\n");

        Assert.Contains("## Care Tips", blocks.Body);
        Assert.DoesNotContain("SEO_TITLE", blocks.Body);
        Assert.DoesNotContain("SEARCH_TERMS", blocks.Body);
        Assert.Equal("Women's Linen Halter Blouse", blocks.Title);
        Assert.Equal(
            ["women's linen blouse", "halter blouse", "linen halter top", "vacation blouse"],
            blocks.SearchTerms);
        Assert.Equal(3, blocks.ProductHighlights.Count);
        Assert.Equal("Halter neckline stays put when you move", blocks.ProductHighlights[0]);
    }

    [Fact]
    public void SplitRewriteBlocks_CapturesMetaWhenStripBrandsSkipsTheTitle()
    {
        var blocks = ListingDescriptionAiService.SplitRewriteBlocks(
            "Opening hook about the set.\n\n"
            + "===SEARCH_TERMS===\n"
            + "- Women's ruffle pants set\n"
            + "Primary: matching two piece set\n"
            + "===PRODUCT_HIGHLIGHTS===\n"
            + "1. Strapless top stays put when you bend\n"
            + "Ruffle hem moves with you\n");

        Assert.Equal("Opening hook about the set.\n\n", blocks.Body);
        Assert.Equal(string.Empty, blocks.Title);
        Assert.Equal(["women's ruffle pants set", "matching two piece set"], blocks.SearchTerms);
        Assert.Equal(
            ["Strapless top stays put when you bend", "Ruffle hem moves with you"],
            blocks.ProductHighlights);
    }

    [Fact]
    public void SanitizeSearchTerms_DropsMarketplacePromoAndDedupes()
    {
        var cleaned = ListingDescriptionAiService.SanitizeSearchTerms(
        [
            "Women's Halter Tank",
            "WOMEN'S HALTER TANK",
            "SHEIN halter tank",
            "hot sale tank top",
            "halter neck tank top",
            "knit tank top",
            "sleeveless top",
            "summer tank",
            "extra seventh term",
            "eighth should be dropped"
        ]);

        Assert.Equal(
            [
                "women's halter tank",
                "halter neck tank top",
                "knit tank top",
                "sleeveless top",
                "summer tank",
                "extra seventh term"
            ],
            cleaned);
        Assert.DoesNotContain(cleaned, t => t.Contains("shein", StringComparison.Ordinal));
        Assert.DoesNotContain(cleaned, t => t.Contains("hot sale", StringComparison.Ordinal));
        Assert.DoesNotContain(cleaned, t => t.Contains("eighth", StringComparison.Ordinal));
    }

    [Fact]
    public void SanitizeProductHighlights_CapsAtFiveAndKeepsShortGroundedLines()
    {
        var cleaned = ListingDescriptionAiService.SanitizeProductHighlights(
        [
            "Halter neckline stays put when you move",
            "Knit holds its shape all day",
            "best seller guaranteed results",
            "Drape collar sits flat at the neck",
            "Packs flat for a weekend bag",
            "Layers under a blazer for work",
            "One more that should be dropped"
        ]);

        Assert.Equal(5, cleaned.Count);
        Assert.Equal("Halter neckline stays put when you move", cleaned[0]);
        Assert.DoesNotContain(cleaned, h => h.Contains("best seller", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Layers under a blazer for work", cleaned[4]);
    }

    [Fact]
    public void ParseTitleMode_UnknownFallsBackToStripBrands()
    {
        Assert.Equal("strip_brands", ListingDescriptionAiService.ParseTitleMode(null));
        Assert.Equal("strip_brands", ListingDescriptionAiService.ParseTitleMode(""));
        Assert.Equal("strip_brands", ListingDescriptionAiService.ParseTitleMode("nope"));
        Assert.Equal("rewrite", ListingDescriptionAiService.ParseTitleMode("rewrite"));
        Assert.Equal("rewrite", ListingDescriptionAiService.ParseTitleMode("Rewrite"));
    }

    [Fact]
    public void ParseAllowRepeat_OnlyTrueUnlocksRepeat()
    {
        Assert.False(ListingDescriptionAiService.ParseAllowRepeat(null));
        Assert.False(ListingDescriptionAiService.ParseAllowRepeat(""));
        Assert.False(ListingDescriptionAiService.ParseAllowRepeat("false"));
        Assert.False(ListingDescriptionAiService.ParseAllowRepeat("nope"));
        Assert.True(ListingDescriptionAiService.ParseAllowRepeat("true"));
        Assert.True(ListingDescriptionAiService.ParseAllowRepeat("True"));
    }

    [Fact]
    public void StripBrandTokens_RemovesSumwonAndKeepsTheRest()
    {
        var stripped = ListingDescriptionAiService.StripBrandTokens(
            "SUMWON WOMEN Wide Leg Cargo Pants With Drawstring Waist And Ankle Ties, "
            + "Relaxed Fit Parachute Trousers With Side Pockets",
            ["SUMWON"]);

        Assert.Equal(
            "WOMEN Wide Leg Cargo Pants With Drawstring Waist And Ankle Ties, "
            + "Relaxed Fit Parachute Trousers With Side Pockets",
            stripped);
    }

    [Fact]
    public void ApplyStrippedTitle_RemovesRomweKeepsStreetLife()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "ROMWE Street Life Women's Punk Music Festival Star Print Mini Dress",
            ["ROMWE"]);

        Assert.DoesNotContain("ROMWE", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Street Life", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Women's", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyStrippedTitle_RemovesScarluxKeepsY2kAndWomens()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "SCARLUX Autumn Y2K Women's Basic Fitted Black Long Sleeve Top, "
            + "Lace Patchwork V-Neck T-Shirt, Suitable For Back To School Casual Wear",
            ["SCARLUX"]);

        Assert.DoesNotContain("SCARLUX", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Y2K", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Women's", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyStrippedTitle_RemovesSheinKeepsFrenchStyle()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "SHEIN Women's Elegant French Style Women Contrast Trim Notched Neck Mini Dress "
            + "3/4 Roll-Cuff Sleeve Pleated A-Line Dress With Pockets Elegant Daily Outfit",
            ["SHEIN"]);

        Assert.DoesNotContain("SHEIN", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("French Style", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Women's", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripBrandTokens_RemovesSheinBaeWhenModelOnlyFlaggedShein()
    {
        var stripped = ListingDescriptionAiService.StripBrandTokens(
            "SHEIN BAE Women's Orange Red Halter Tank Top,Summer Casual Night Beach Cover Up,"
            + "Adorable Crop Tops,Graphic Tees,Asymmetrical Festival Cute T Shirts",
            ["SHEIN"]);

        Assert.DoesNotContain("SHEIN", stripped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BAE", stripped, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Women's", stripped, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Halter Tank Top", stripped, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyStrippedTitle_RemovesSheinBaeLineNotJustShein()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "SHEIN BAE Women's Orange Red Halter Tank Top,Summer Casual Night Beach Cover Up,"
            + "Adorable Crop Tops,Graphic Tees,Asymmetrical Festival Cute T Shirts",
            ["SHEIN"]);

        Assert.DoesNotContain("SHEIN", title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BAE", title, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Women's", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Halter Tank Top", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Graphic Tees", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyStrippedTitle_EmptyBrandsStillRemovesSheinBae()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "SHEIN BAE Women's Orange Red Halter Tank Top",
            []);

        Assert.DoesNotContain("SHEIN", title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BAE", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Women's", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Halter Tank Top", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyStrippedTitle_RemovesUnknownSheinLineBeforeWomens()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "SHEIN FLOOP Women's Orange Halter Tank Top",
            ["SHEIN"]);

        Assert.DoesNotContain("SHEIN", title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FLOOP", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Women's", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Halter", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyStrippedTitle_KeepsFloralWhenItFollowsSheinBeforeWomens()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "SHEIN Floral Women's Halter Tank Top",
            ["SHEIN"]);

        Assert.DoesNotContain("SHEIN", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Floral", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Women's", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyStrippedTitle_RemovesSheinIconKeepsPlusSize()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "SHEIN ICON Plus Size Sexy & Elegant Solid Color Lace Patchwork Ruched Slim Cami Top",
            ["SHEIN"]);

        Assert.DoesNotContain("SHEIN", title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ICON", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plus Size", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cami Top", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyStrippedTitle_EmptyBrandsStillStripsKnownMarketplace()
    {
        var title = ListingDescriptionAiService.ApplyStrippedTitle(
            "SHEIN Women's Elegant French Style Mini Dress",
            []);

        Assert.DoesNotContain("SHEIN", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Women's", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseBrandList_ReadsJsonAndMarkdownFences()
    {
        var brands = ListingDescriptionAiService.ParseBrandList(
            "```json\n{\"brands\":[\"SUMWON\",\"SHEIN\"]}\n```");

        Assert.Equal(["SUMWON", "SHEIN"], brands);
    }

    [Fact]
    public void ParseBrandList_EmptyOnGarbage()
    {
        Assert.Empty(ListingDescriptionAiService.ParseBrandList("not json"));
        Assert.Empty(ListingDescriptionAiService.ParseBrandList(null));
    }

    [Fact]
    public void NeedsSeoTitleFallback_TrueWhenUnderTikTokFloor()
    {
        Assert.True(ListingDescriptionAiService.NeedsSeoTitleFallback(""));
        Assert.True(ListingDescriptionAiService.NeedsSeoTitleFallback("Women Floral Mini Dress"));
        Assert.True(ListingDescriptionAiService.NeedsSeoTitleFallback(new string('A', 39)));
    }

    [Fact]
    public void NeedsSeoTitleFallback_FalseWhenAtLeastFortyCharacters()
    {
        Assert.False(ListingDescriptionAiService.NeedsSeoTitleFallback(new string('A', 40)));
        Assert.False(ListingDescriptionAiService.NeedsSeoTitleFallback(
            "Women Floral Print A-Line Mini Dress Summer Casual"));
    }

    [Fact]
    public void EnsureWomenAudienceLead_RewritesWomensToWomenAtStart()
    {
        var title = ListingDescriptionAiService.EnsureWomenAudienceLead(
            "Women's Floral Mini Dress Ruffle Hem",
            "SHEIN Women's Floral Mini Dress",
            "Category: Womenswear > Dresses");

        Assert.StartsWith("Women ", title, StringComparison.Ordinal);
        Assert.DoesNotContain("Women's", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Floral Mini Dress", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureWomenAudienceLead_PrependsWomenWhenAudienceIsMissing()
    {
        var title = ListingDescriptionAiService.EnsureWomenAudienceLead(
            "Floral Mini Dress Ruffle Hem A-Line",
            "SHEIN Floral Mini Dress",
            "Category: Dresses");

        Assert.Equal("Women Floral Mini Dress Ruffle Hem A-Line", title);
    }

    [Fact]
    public void EnsureWomenAudienceLead_InsertsWomenAfterPlusSize()
    {
        var title = ListingDescriptionAiService.EnsureWomenAudienceLead(
            "Plus Size Lace Patchwork Ruched Cami Top",
            "SHEIN ICON Plus Size Lace Patchwork Ruched Cami Top",
            "Category: Plus Size");

        Assert.StartsWith("Plus Size Women ", title, StringComparison.Ordinal);
        Assert.Contains("Cami Top", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureWomenAudienceLead_LeavesPlusSizeWomenAlone()
    {
        const string source = "Plus Size Women Lace Cami Top";
        var title = ListingDescriptionAiService.EnsureWomenAudienceLead(
            source, source, "Category: Plus Size");

        Assert.Equal(source, title);
    }

    [Fact]
    public void EnsureWomenAudienceLead_LeavesMensTitlesAlone()
    {
        const string source = "Men's Cargo Pants Drawstring Waist";
        var title = ListingDescriptionAiService.EnsureWomenAudienceLead(
            source, source, "Category: Menswear > Pants");

        Assert.Equal(source, title);
    }

    [Fact]
    public void EnsureWomenAudienceLead_CollapsesDuplicateWomen()
    {
        var title = ListingDescriptionAiService.EnsureWomenAudienceLead(
            "Women Women Floral Mini Dress",
            "Women Floral Mini Dress",
            "");

        Assert.Equal("Women Floral Mini Dress", title);
    }

    [Fact]
    public void IsWomensProduct_DefaultsToWomenUnlessMensOrUnisex()
    {
        Assert.True(ListingDescriptionAiService.IsWomensProduct(
            "Floral Mini Dress", "Category: Dresses"));
        Assert.True(ListingDescriptionAiService.IsWomensProduct(
            "SHEIN Women's Halter Tank", "Category: Tops"));
        Assert.False(ListingDescriptionAiService.IsWomensProduct(
            "Men's Cargo Pants", "Category: Menswear"));
        Assert.False(ListingDescriptionAiService.IsWomensProduct(
            "Unisex Cotton Crew Tee", "Category: T-Shirts"));
        Assert.False(ListingDescriptionAiService.IsWomensProduct(
            "Girls Floral Mini Dress", "Category: Kids"));
    }
}
