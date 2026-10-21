using Application.DTOs.FourSeller;
using Application.DTOs.Listing;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingSpecificsMappingEngineTests
{
    private const string SampleDescription = """
        Details:Backless

        Pattern Type:Geometric

        Waist Line:Low Waist

        Occasion:Beach

        Features:High Stretch

        Cup Type:3/4 Cup

        Bikini Bottoms Length:Short

        Festivals:Valentine's Day

        Color:Khaki

        Style:Cute

        Bottom Type:Tie Side

        Bra Type:Wireless Bra

        Neckline:Halter

        Fabric Elasticity:High Stretch

        Care Instructions:Machine wash, do not dry clean

        Chest pad:Removable Padding

        Sheer:No

        Bikini Bottoms Body:Lined

        Bikini Bottoms Lining:95% Polyester,5% Elastane

        Bikini Bottoms Material:Polyester

        Bikini Bottoms&Bikini Tops Composition:82% Polyester,18% Elastane

        The position of the bottom circumference:Regular bottom circumference position
        """;

    private const string InlineDescription =
        "Details:Rib-Knit Body:No Pattern Type:Striped Style:Casual Type:Shirt Occasion:Stage & Concert Lined For Added Warmth:No Festivals:Halloween Color:Black Hem Shaped:Regular Sleeve Type:Regula";

    [Fact]
    public void ParseDescriptionPairs_ParsesSheinSample()
    {
        var pairs = ListingSpecificsMappingEngine.ParseDescriptionPairs(SampleDescription);

        Assert.Contains(pairs, p => p.Name == "Pattern Type" && p.Value == "Geometric");
        Assert.Contains(pairs, p => p.Name == "Care Instructions" && p.Value == "Machine wash, do not dry clean");
        Assert.Contains(pairs, p =>
            p.Name == "Bikini Bottoms&Bikini Tops Composition"
            && p.Value == "82% Polyester,18% Elastane");
        Assert.Contains(pairs, p => p.Name == "Color" && p.Value == "Khaki");
        Assert.True(pairs.Count >= 20);
    }

    [Fact]
    public void ParseDescriptionPairs_HandlesHtmlBreaks()
    {
        var html = "<p>Pattern Type:Geometric<br/>Occasion:Beach</p>";
        var pairs = ListingSpecificsMappingEngine.ParseDescriptionPairs(html);
        Assert.Equal(2, pairs.Count);
        Assert.Equal("Geometric", pairs[0].Value);
        Assert.Equal("Beach", pairs[1].Value);
    }

    [Fact]
    public void ParseDescriptionPairs_SplitsInlineConcatenatedKeys()
    {
        var pairs = ListingSpecificsMappingEngine.ParseDescriptionPairs(InlineDescription);

        Assert.Contains(pairs, p => p.Name == "Pattern Type" && p.Value == "Striped");
        Assert.Contains(pairs, p => p.Name == "Style" && p.Value == "Casual");
        Assert.Contains(pairs, p => p.Name == "Color" && p.Value == "Black");
        Assert.Contains(pairs, p => p.Name == "Body" && p.Value == "No");
        Assert.Contains(pairs, p => p.Name == "Occasion" && p.Value == "Stage & Concert");
        Assert.True(pairs.Count >= 8);
    }

    [Fact]
    public void MatchValueFirst_MapsUniqueValuesIncludingColor()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100198", "Pattern", false, ("1003618", "Geometric"), ("1001184", "Striped"), ("1000931", "Solid")),
            Attr("100392", "Occasion", true, ("3001", "Beach"), ("3002", "Casual")),
            Attr("100000", "Color", false, ("1002344", "Khaki"), ("1001063", "Black")),
            Attr("100398", "Style", false, ("1001124", "Casual"), ("5001", "Cute"), ("5002", "Sexy")),
        };

        var pairs = ListingSpecificsMappingEngine.ParseDescriptionPairs(SampleDescription);
        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Contains(matched, m => m.AttrId == "100198" && m.ValueId == "1003618" && m.AttrValue == "Geometric");
        Assert.Contains(matched, m => m.AttrId == "100392" && m.ValueId == "3001");
        Assert.Contains(matched, m => m.AttrId == "100398" && m.ValueId == "5001");
        Assert.Contains(matched, m => m.AttrId == "100000" && m.ValueId == "1002344" && m.MatchMethod == "ValueExact");
        Assert.DoesNotContain(collisions, c => c.SourceName == "Color");
    }

    [Fact]
    public void MatchValueFirst_InlineSample_MapsStripedCasualBlack()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100198", "Pattern", false, ("1001184", "Striped"), ("1003618", "Geometric")),
            Attr("100398", "Style", false, ("1001124", "Casual"), ("1001175", "Sexy")),
            Attr("100000", "Color", false, ("1001063", "Black"), ("1001079", "White")),
            Attr("100409", "Clothing Type", false, ("1003125", "Dress")),
        };

        var pairs = ListingSpecificsMappingEngine.ParseDescriptionPairs(InlineDescription);
        var (matched, _, _) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Contains(matched, m => m.AttrId == "100198" && m.ValueId == "1001184");
        Assert.Contains(matched, m => m.AttrId == "100398" && m.ValueId == "1001124");
        Assert.Contains(matched, m => m.AttrId == "100000" && m.ValueId == "1001063");
        // Type:Shirt has no schema value → not forced onto Clothing Type
        Assert.DoesNotContain(matched, m => m.AttrId == "100409");
    }

    [Fact]
    public void MatchValueFirst_CollisionWhenValueInMultipleAttrs()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100701", "Material", false, ("1000011", "Polyester")),
            Attr("103499", "Lining Description", false, ("1000011", "Polyester"), ("1001264", "Unlined")),
        };
        var pairs = new List<(string Name, string Value)> { ("Bikini Bottoms Material", "Polyester") };

        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Empty(matched);
        Assert.Empty(remaining);
        Assert.Single(collisions);
        Assert.Equal(2, collisions[0].Candidates.Count);
        Assert.Contains(collisions[0].Candidates, c => c.Attr.AttributeId == "100701");
        Assert.Contains(collisions[0].Candidates, c => c.Attr.AttributeId == "103499");
    }

    [Fact]
    public void MatchValueFirst_MatchesExtraDescriptiveWords()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100399", "Fit", false, ("1001104", "Regular"), ("1003781", "Slim")),
        };
        var pairs = new List<(string Name, string Value)> { ("Fit Type", "Regular Fit") };

        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Contains(matched, m => m.AttrId == "100399" && m.ValueId == "1001104");
        Assert.Empty(collisions);
        Assert.DoesNotContain(remaining, r => r.Name == "Fit Type");
    }

    [Fact]
    public void MatchValueFirst_DoesNotMatchOnGenericHeadNounOnly()
    {
        // Source "Size Type: Plus Size" must NOT invent a hit on Clothing Type (no Plus Size there).
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100409", "Clothing Type", false, ("7001", "Shirt"), ("7002", "Dress")),
        };
        var pairs = new List<(string Name, string Value)> { ("Size Type", "Plus Size") };

        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Empty(matched);
        Assert.Empty(collisions);
        Assert.Contains(remaining, r => r.Name == "Size Type");
    }

    [Fact]
    public void MatchValueFirst_MapsSizeTypeWhenValuePresent()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("101127", "Size Type", false,
                ("1002680", "Plus Size"), ("1002729", "One Size"), ("1000005", "Standard")),
            Attr("100409", "Clothing Type", false, ("7001", "Shirt"), ("7002", "Dress")),
        };
        var pairs = new List<(string Name, string Value)> { ("Size Type", "Plus Size") };

        var (matched, collisions, _) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Contains(matched, m => m.AttrId == "101127" && m.ValueId == "1002680");
        Assert.Empty(collisions);
    }

    [Fact]
    public void MatchValueFirst_DoesNotOverwriteExisting()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100392", "Occasion", true, ("3001", "Beach"), ("3002", "Casual")),
        };
        var pairs = new List<(string Name, string Value)> { ("Occasion", "Beach") };
        var existing = new Dictionary<string, UpdateListingMirrorAttributeDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["100392"] = new() { AttrId = "100392", AttrName = "Occasion", AttrValue = "Casual" }
        };

        var (matched, _, _) = ListingSpecificsMappingEngine.MatchValueFirst(pairs, schema, existing);
        Assert.Single(matched);
        Assert.True(matched[0].SkippedExisting);
    }

    [Fact]
    public void ValidateAiSuggestions_RejectsInventedIds()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100392", "Occasion", true, ("3001", "Beach"), ("3002", "Casual")),
        };
        var remaining = new List<(string Name, string Value)> { ("Occasion", "Beach") };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aiJson = """
            [
              {"sourceName":"Occasion","sourceValue":"Beach","attributeId":"9999","valueId":"3001","valueName":"Beach","confidence":0.99},
              {"sourceName":"Occasion","sourceValue":"Beach","attributeId":"100392","valueId":"8888","valueName":"Beach","confidence":0.99},
              {"sourceName":"Occasion","sourceValue":"Beach","attributeId":"100392","valueId":"3001","valueName":"Beach","confidence":0.91}
            ]
            """;

        var validated = ListingSpecificsMappingEngine.ValidateAiSuggestions(
            aiJson,
            remaining,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            used);

        Assert.Single(validated);
        Assert.Equal("100392", validated[0].AttrId);
        Assert.Equal("3001", validated[0].ValueId);
        Assert.Equal("Ai", validated[0].MatchMethod);
    }

    [Fact]
    public void ValidateAiSuggestions_CollisionRestrictsCandidates()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100701", "Material", false, ("1000011", "Polyester")),
            Attr("103499", "Lining Description", false, ("1000011", "Polyester")),
            Attr("100198", "Pattern", false, ("1000931", "Solid")),
        };
        var remaining = new List<(string Name, string Value)> { ("Material", "Polyester") };
        var allowed = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["material"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "100701", "103499" }
        };
        var aiJson = """
            [
              {"sourceName":"Material","sourceValue":"Polyester","attributeId":"100198","valueId":"1000931","valueName":"Solid","confidence":0.99},
              {"sourceName":"Material","sourceValue":"Polyester","attributeId":"100701","valueId":"1000011","valueName":"Polyester","confidence":0.95}
            ]
            """;

        var validated = ListingSpecificsMappingEngine.ValidateAiSuggestions(
            aiJson,
            remaining,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            matchMethod: "AiCollision",
            allowedAttrIdsBySource: allowed);

        Assert.Single(validated);
        Assert.Equal("100701", validated[0].AttrId);
        Assert.Equal("AiCollision", validated[0].MatchMethod);
    }

    [Fact]
    public void ValidateAiSuggestions_RejectsLowConfidence()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100392", "Occasion", true, ("3001", "Beach")),
        };
        var aiJson = """[{"sourceName":"Occasion","sourceValue":"Beach","attributeId":"100392","valueId":"3001","valueName":"Beach","confidence":0.4}]""";
        var validated = ListingSpecificsMappingEngine.ValidateAiSuggestions(
            aiJson,
            [("Occasion", "Beach")],
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(validated);
    }

    [Fact]
    public void BuildMissingRequired_ExcludesFilledAndCerts()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("1", "Occasion", true, ("a", "Beach")),
            Attr("2", "Style", true, ("b", "Cute")),
            Attr("101619", "Dangerous Goods Or Hazardous Materials", true, ("1000059", "No")),
        };

        var missing = ListingSpecificsMappingEngine.BuildMissingRequired(schema, ["1"]);
        Assert.Single(missing);
        Assert.Equal("2", missing[0].AttrId);
    }

    [Fact]
    public void BuildMissingRequired_ForceRequiredWashingInstruction()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("1", "Occasion", true, ("a", "Beach")),
            Attr("9001", "Washing Instruction", false, ("w1", "Machine Wash")),
        };

        var missing = ListingSpecificsMappingEngine.BuildMissingRequired(
            schema,
            filledAttrIds: ["1"],
            forceRequiredAttrIds: ["9001"]);
        Assert.Single(missing);
        Assert.Equal("9001", missing[0].AttrId);
        Assert.Equal("Washing Instruction", missing[0].AttrName);
    }

    [Fact]
    public void EnsureFashionWashingInstruction_MapsCareInstructions()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100392", "Occasion", false, ("3001", "Beach")),
            Attr(
                "9001",
                "Washing Instruction",
                false,
                ("w1", "Machine Wash"),
                ("w2", "Hand Wash"),
                ("w3", "Dry Clean Only")),
        };
        var matched = new List<MappedListingSpecificDto>();
        var unmatched = new List<UnmappedListingSpecificDto>
        {
            new()
            {
                SourceName = "Care Instructions",
                SourceValue = "Machine wash, do not dry clean",
                Reason = "test"
            }
        };
        var pairs = new List<(string Name, string Value)>
        {
            ("Care Instructions", "Machine wash, do not dry clean"),
            ("Occasion", "Beach"),
        };

        ListingSpecificsMappingEngine.EnsureFashionWashingInstruction(
            matched,
            unmatched,
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            categoryName: "Women's Apparel / Dresses");

        Assert.Single(matched);
        Assert.Equal("9001", matched[0].AttrId);
        Assert.Equal("Washing Instruction", matched[0].AttrName);
        Assert.Equal("Machine Wash", matched[0].AttrValue);
        Assert.True(matched[0].IsRequired);
        Assert.StartsWith("Washing", matched[0].MatchMethod);
        Assert.Empty(unmatched);
    }

    [Fact]
    public void TryMapOntoWashingInstruction_MapsHandWashOnlyToHandWashEnum()
    {
        var washing = Attr(
            "100401",
            "Washing instructions",
            true,
            ("1001", "Machine wash"),
            ("1002", "Hand wash"),
            ("1003", "Dry clean"));

        var mapped = ListingSpecificsMappingEngine.TryMapOntoWashingInstruction(
            "Care Instructions",
            "Hand wash only",
            washing,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.NotNull(mapped);
        Assert.Equal("1002", mapped!.ValueId);
        Assert.Equal("Hand wash", mapped.AttrValue);
        Assert.DoesNotContain("Custom", mapped.MatchMethod, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryMapOntoWashingInstruction_KeepsTheCareValueTheSupplierActuallyStated()
    {
        var washing = Category903824Washing();

        var mapped = ListingSpecificsMappingEngine.TryMapOntoWashingInstruction(
            "Care Instructions",
            "Hand wash only",
            washing,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.NotNull(mapped);
        Assert.Equal("1001195", mapped!.ValueId);
        Assert.Equal("Hand wash only", mapped.AttrValue);
    }

    [Fact]
    public void TryMapOntoWashingInstruction_SkipsDenylistedEnumAndKeepsAWashingMethod()
    {
        var washing = Category903824Washing();
        var denylist = new TikTokAttributeValueDenylist([("100401", "Hand wash only")]);

        var mapped = ListingSpecificsMappingEngine.TryMapOntoWashingInstruction(
            "Care Instructions",
            "Hand wash only",
            washing,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            denylist);

        Assert.NotNull(mapped);
        Assert.Equal("1001200", mapped!.ValueId);
        Assert.Equal("Cold wash", mapped.AttrValue);
    }

    [Theory]
    [InlineData("Do not dry clean")]
    [InlineData("Machine wash, do not dry clean")]
    public void TryMapOntoWashingInstruction_NeverMapsAProhibitionOntoWhatItForbids(string careValue)
    {
        var washing = Attr(
            "100401",
            "Washing instructions",
            true,
            ("1001196", "Dry clean"));

        var mapped = ListingSpecificsMappingEngine.TryMapOntoWashingInstruction(
            "Care Instructions",
            careValue,
            washing,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Null(mapped);
    }

    [Fact]
    public void TryMapOntoWashingInstruction_KeepsTheWashingMethodStatedAlongsideAProhibition()
    {
        var washing = Attr(
            "100401",
            "Washing instructions",
            true,
            ("1001196", "Dry clean"),
            ("1001199", "Machine washable"));

        var mapped = ListingSpecificsMappingEngine.TryMapOntoWashingInstruction(
            "Care Instructions",
            "Machine wash, do not dry clean",
            washing,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.NotNull(mapped);
        Assert.Equal("1001199", mapped!.ValueId);
        Assert.Equal("Machine washable", mapped.AttrValue);
    }

    [Fact]
    public void ResolveDefaultWashingInstructionValue_PrefersAWashingMethodOverAProhibition()
    {
        var def = ListingSpecificsMappingEngine.ResolveDefaultWashingInstructionValue(
            Category903824Washing());

        Assert.NotNull(def);
        Assert.Equal("1001195", def!.ValueId);
        Assert.Equal("Hand wash only", def.ValueName);
    }

    [Fact]
    public void ResolveDefaultWashingInstructionValue_SkipsDenylistedValue()
    {
        var denylist = new TikTokAttributeValueDenylist([("100401", "Hand wash only")]);

        var def = ListingSpecificsMappingEngine.ResolveDefaultWashingInstructionValue(
            Category903824Washing(),
            denylist);

        Assert.NotNull(def);
        Assert.Equal("1001200", def!.ValueId);
        Assert.Equal("Cold wash", def.ValueName);
    }

    [Fact]
    public void ResolveDefaultWashingInstructionValue_FallsBackToDryCleanWhenEveryMethodIsDenied()
    {
        var denylist = new TikTokAttributeValueDenylist(
        [
            ("100401", "Hand wash only"),
            ("100401", "Cold wash"),
            ("100401", "Machine washable")
        ]);

        // This is the state category 903824 reached: only Dry clean and the two prohibitions left.
        // Dry clean is a poor default, but Washing instructions is mandatory — dropping the attribute
        // makes 4Seller refuse the publish outright with `Specifics.Washing instructions`.
        var def = ListingSpecificsMappingEngine.ResolveDefaultWashingInstructionValue(
            Category903824Washing(),
            denylist);

        Assert.NotNull(def);
        Assert.Equal("1001196", def!.ValueId);
        Assert.Equal("Dry clean", def.ValueName);
    }

    [Fact]
    public void ResolveDefaultWashingInstructionValue_PrefersADeniedMethodOverAProhibition()
    {
        var denylist = new TikTokAttributeValueDenylist(
        [
            ("100401", "Hand wash only"),
            ("100401", "Cold wash"),
            ("100401", "Machine washable"),
            ("100401", "Dry clean")
        ]);

        // The state category 903824 actually reached: every washing method denied, only the two
        // prohibitions untouched. TikTok rejects those ("Do not wash" came back as a custom value name)
        // while `Hand wash only` published fine, so a denied method outranks a prohibition — and both
        // outrank an empty mandatory row, which 4Seller refuses with `Specifics.Washing instructions`.
        var def = ListingSpecificsMappingEngine.ResolveDefaultWashingInstructionValue(
            Category903824Washing(),
            denylist);

        Assert.NotNull(def);
        Assert.Equal("1001195", def!.ValueId);
        Assert.Equal("Hand wash only", def.ValueName);
    }

    [Fact]
    public void ResolveDefaultWashingInstructionValue_TakesAProhibitionWhenTheEnumOffersNothingElse()
    {
        var washing = Attr(
            "100401",
            "Washing instructions",
            true,
            ("1001197", "Do not wash"));

        var def = ListingSpecificsMappingEngine.ResolveDefaultWashingInstructionValue(washing);

        Assert.NotNull(def);
        Assert.Equal("1001197", def!.ValueId);
    }

    [Fact]
    public void WithoutRejectedValues_KeepsMandatoryEnumWhenOnlyProhibitionsWouldSurvive()
    {
        var denylist = new TikTokAttributeValueDenylist(
        [
            ("100401", "Hand wash only"),
            ("100401", "Cold wash"),
            ("100401", "Machine washable"),
            ("100401", "Dry clean")
        ]);

        var filtered = ListingSpecificsMappingEngine.WithoutRejectedValues(
            Category903824Washing(),
            denylist);

        Assert.Equal(6, filtered.Values.Count);
    }

    [Fact]
    public void WithoutRejectedValues_StillFiltersAnOptionalAttribute()
    {
        var care = Attr(
            "101303",
            "Care instructions",
            false,
            ("1", "Hand wash only"),
            ("2", "Do not wash"));
        var denylist = new TikTokAttributeValueDenylist([("101303", "Hand wash only")]);

        var filtered = ListingSpecificsMappingEngine.WithoutRejectedValues(care, denylist);

        Assert.Single(filtered.Values);
        Assert.Equal("Do not wash", filtered.Values[0].ValueName);
    }

    [Fact]
    public void TryMapOntoWashingInstruction_DoesNotEmitCustomWhenEnumsExist()
    {
        var washing = Attr(
            "100401",
            "Washing instructions",
            true,
            ("1001", "Machine wash"),
            ("1002", "Hand wash"));
        washing.IsCustomizable = true;

        var mapped = ListingSpecificsMappingEngine.TryMapOntoWashingInstruction(
            "Care Instructions",
            "Do not bleach under any circumstances xyz",
            washing,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        // No wash-care signal + no enum hit → null (never WashingCustom).
        Assert.Null(mapped);
    }

    [Fact]
    public void EnsureFashionWashingInstruction_SeedsDefaultWhenNoCarePairs()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr(
                "100401",
                "Washing instructions",
                false,
                ("1001", "Machine wash"),
                ("1002", "Hand wash")),
        };

        var matched = new List<MappedListingSpecificDto>();
        ListingSpecificsMappingEngine.EnsureFashionWashingInstruction(
            matched,
            [],
            pairs: [],
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            categoryName: "Women's Apparel / Tops");

        // Gentlest washing method wins: it is safe on the widest range of fabrics, so it is the least
        // likely to contradict what the garment actually needs.
        Assert.Single(matched);
        Assert.Equal("1002", matched[0].ValueId);
        Assert.Equal("Hand wash", matched[0].AttrValue);
        Assert.Equal("WashingDefault", matched[0].MatchMethod);
    }

    [Fact]
    public void MatchValueFirst_PrefersWashingInstructionForCareSource()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100701", "Material", false, ("1000011", "Machine Wash")),
            Attr("9001", "Washing Instruction", false, ("w1", "Machine Wash")),
        };
        var pairs = new List<(string Name, string Value)>
        {
            ("Care Instructions", "Machine Wash"),
        };

        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Empty(collisions);
        Assert.Empty(remaining);
        Assert.Single(matched);
        Assert.Equal("9001", matched[0].AttrId);
        Assert.True(matched[0].IsRequired);
    }

    [Fact]
    public void IsFashionCategory_DetectsBySchemaOrName()
    {
        var withWashing = new List<FourSellerCategoryAttributeDto>
        {
            Attr("9001", "Washing Instruction", false, ("w1", "Machine Wash")),
        };
        Assert.True(ListingSpecificsMappingEngine.IsFashionCategory(null, withWashing));
        Assert.True(ListingSpecificsMappingEngine.IsFashionCategory(
            "Women's Clothing / Tops",
            []));
        Assert.False(ListingSpecificsMappingEngine.IsFashionCategory(
            "Home / Kitchen / Cookware",
            []));
    }

    [Fact]
    public void MergeConditionDefaultsIntoAttributes_SeedsTypeAndDescription()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            new()
            {
                AttributeId = "101341",
                AttributeName = "Condition type",
                IsRequired = true,
                Values =
                [
                    new() { ValueId = "200001", ValueName = "New" },
                    new() { ValueId = "200002", ValueName = "Used" }
                ]
            },
            new()
            {
                AttributeId = "101342",
                AttributeName = "Condition description",
                IsRequired = true,
                IsCustomizable = true
            },
            Attr("100392", "Occasion", false, ("3001", "Beach")),
        };

        var merged = ListingSpecificsMappingEngine.MergeConditionDefaultsIntoAttributes(
            [new ListingAttributeDto { AttrId = "100392", AttrName = "Occasion", AttrValue = "Beach" }],
            schema);

        Assert.Equal(3, merged.Count);
        var type = merged.First(a => a.AttrId == "101341");
        Assert.Equal("New", type.AttrValue);
        Assert.Equal("200001", type.ValueId);
        var desc = merged.First(a => a.AttrId == "101342");
        Assert.Equal("Brand new", desc.AttrValue);
    }

    [Fact]
    public void MergeConditionDefaultsIntoAttributes_SkipsWhenAlreadyFilled()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            new()
            {
                AttributeId = "101342",
                AttributeName = "Condition description",
                IsRequired = true,
                IsCustomizable = true
            }
        };

        var merged = ListingSpecificsMappingEngine.MergeConditionDefaultsIntoAttributes(
            [new ListingAttributeDto { AttrId = "101342", AttrName = "Condition description", AttrValue = "Like new" }],
            schema);

        Assert.Single(merged);
        Assert.Equal("Like new", merged[0].AttrValue);
    }

    [Fact]
    public void FilterSchema_ExcludesSalePropsAndUnmetConditions()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            new()
            {
                AttributeId = "100000",
                AttributeName = "Color",
                AttributeType = "2",
                Values = [new() { ValueId = "1", ValueName = "Black" }]
            },
            new()
            {
                AttributeId = "100701",
                AttributeName = "Material",
                IsMultiple = true,
                Values = [new() { ValueId = "1000039", ValueName = "Cotton" }]
            },
            new()
            {
                AttributeId = "999001",
                AttributeName = "Enable Extra Detail",
                Values =
                [
                    new() { ValueId = "1000058", ValueName = "Yes" },
                    new() { ValueId = "1000059", ValueName = "No" }
                ]
            },
            new()
            {
                AttributeId = "999002",
                AttributeName = "Extra Detail",
                RequirementConditionsJson =
                    """[{"condition_type":"VALUE_ID_MATCH","attribute_id":"999001","attribute_value_id":"1000058"}]""",
                Values = [new() { ValueId = "9", ValueName = "Foo" }]
            },
            new()
            {
                AttributeId = "101400",
                AttributeName = "CA prop 65: carcinogens",
                Values =
                [
                    new() { ValueId = "1000058", ValueName = "Yes" },
                    new() { ValueId = "1000059", ValueName = "No" }
                ]
            },
        };

        var filtered = ListingSpecificsMappingEngine.FilterSchemaForSpecificsMapping(
            schema,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["999001"] = ListingSpecificsMappingEngine.TikTokNoValueId,
                ["101400"] = ListingSpecificsMappingEngine.TikTokNoValueId
            });

        Assert.DoesNotContain(filtered, a => a.AttributeId == "100000");
        Assert.DoesNotContain(filtered, a => a.AttributeId == "999002");
        Assert.DoesNotContain(filtered, a => a.AttributeId == "101400"); // cert name
        Assert.Contains(filtered, a => a.AttributeId == "100701");
        Assert.Contains(filtered, a => a.AttributeId == "999001");
    }

    [Theory]
    [InlineData("Region of origin", false)]
    [InlineData("region-of-origin", false)]
    [InlineData("CPSIA tracking label", false)]
    [InlineData("cpsia tracking label", false)]
    [InlineData("Material", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAutoPublishBlockedSpecificAttribute_IsLegacyNoOp(string? name, bool expected)
    {
        Assert.Equal(expected, ListingSpecificsMappingEngine.IsAutoPublishBlockedSpecificAttribute(name));
    }

    [Fact]
    public void FilterSchema_KeepsRegionOfOriginAndCpsia()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100701", "Material", false, ("1000039", "Cotton")),
            Attr("100336", "Region of origin", true, ("1000850", "China"), ("1000854", "Vietnam")),
            Attr("101200", "CPSIA tracking label", true, ("1000058", "Yes"), ("1000059", "No")),
        };

        var filtered = ListingSpecificsMappingEngine.FilterSchemaForSpecificsMapping(schema);

        Assert.Equal(3, filtered.Count);
        Assert.Contains(filtered, a => a.AttributeId == "100336");
        Assert.Contains(filtered, a => a.AttributeId == "101200");
        Assert.Equal(2, ListingSpecificsMappingEngine.BuildMissingRequired(filtered, []).Count);
    }

    [Fact]
    public void MatchValueFirst_MapsRegionOfOriginAndCpsia()
    {
        var schema = ListingSpecificsMappingEngine.FilterSchemaForSpecificsMapping(
        [
            Attr("100336", "Region of origin", true, ("1000850", "China")),
            Attr("101200", "CPSIA tracking label", true, ("1000059", "No"), ("1000058", "Yes")),
        ]);
        var pairs = new List<(string Name, string Value)>
        {
            ("Region of origin", "China"),
            ("CPSIA tracking label", "No"),
        };

        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Contains(matched, m => m.AttrId == "100336" && m.AttrValue == "China" && m.ValueId == "1000850");
        Assert.Contains(matched, m => m.AttrId == "101200" && m.AttrValue == "No" && m.ValueId == "1000059");
        Assert.Empty(collisions);
        Assert.Empty(remaining);
    }

    [Fact]
    public void EnsureRegionAndCpsiaSpecifics_SeedsChinaAndNoWhenMissing()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100336", "Region of origin", true, ("1000854", "Vietnam"), ("1000850", "China")),
            Attr("101200", "CPSIA tracking label", true, ("1000058", "Yes"), ("1000059", "No")),
        };
        var matched = new List<MappedListingSpecificDto>();

        ListingSpecificsMappingEngine.EnsureRegionAndCpsiaSpecifics(
            matched,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Contains(matched, m =>
            m.AttrId == "100336" && m.AttrValue == "China" && m.ValueId == "1000850"
            && m.MatchMethod == "RegionOfOriginDefault");
        Assert.Contains(matched, m =>
            m.AttrId == "101200" && m.AttrValue == "No" && m.ValueId == "1000059"
            && m.MatchMethod == "CpsiaDefault");
    }

    [Fact]
    public void MergeConditionDefaults_AlsoSeedsRegionAndCpsia()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("100336", "Region of origin", true, ("1000850", "China")),
            Attr("101200", "CPSIA tracking label", true, ("1000059", "No")),
        };

        var merged = ListingSpecificsMappingEngine.MergeConditionDefaultsIntoAttributes([], schema);

        Assert.Contains(merged, a => a.AttrId == "100336" && a.AttrValue == "China" && a.ValueId == "1000850");
        Assert.Contains(merged, a => a.AttrId == "101200" && a.AttrValue == "No" && a.ValueId == "1000059");
    }

    [Fact]
    public void MatchValueFirst_MapsMaterialCottonCanonicalIds()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            MultiAttr("100701", "Material", isMultiple: true, ("1000039", "Cotton"), ("1000011", "Polyester")),
        };
        var pairs = new List<(string Name, string Value)> { ("Material", "Cotton") };

        var (matched, _, _) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Contains(matched, m =>
            m.AttrId == "100701"
            && m.ValueId == "1000039"
            && m.AttrValue == "Cotton"
            && m.Values.Count == 1);
    }

    [Fact]
    public void MatchValueFirst_MultiMaterialCottonPolyester()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            MultiAttr("100701", "Material", isMultiple: true, ("1000039", "Cotton"), ("1000011", "Polyester")),
        };
        var pairs = new List<(string Name, string Value)> { ("Material", "Cotton, Polyester") };

        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Empty(collisions);
        Assert.Empty(remaining);
        Assert.Single(matched);
        Assert.Equal("100701", matched[0].AttrId);
        Assert.Equal(2, matched[0].Values.Count);
        Assert.Contains(matched[0].Values, v => v.ValueId == "1000039");
        Assert.Contains(matched[0].Values, v => v.ValueId == "1000011");
        Assert.Equal("ValueMulti", matched[0].MatchMethod);
    }

    [Fact]
    public void MatchValueFirst_MultiSeasonSpringSummer()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            MultiAttr("100397", "Season", isMultiple: true,
                ("1001161", "Spring"), ("1001162", "Summer"), ("1001163", "Winter")),
        };
        var pairs = new List<(string Name, string Value)> { ("Season", "Spring / Summer") };

        var (matched, _, _) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Single(matched);
        Assert.Equal("100397", matched[0].AttrId);
        Assert.Equal(2, matched[0].Values.Count);
        Assert.Contains(matched[0].Values, v => v.ValueId == "1001161");
        Assert.Contains(matched[0].Values, v => v.ValueId == "1001162");
    }

    [Fact]
    public void MatchValueFirst_SingleSelectKeepsOneValueDespiteList()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            MultiAttr("100392", "Occasion", isMultiple: false, ("1001123", "Wedding"), ("1001124", "Casual")),
        };
        var pairs = new List<(string Name, string Value)> { ("Occasion", "Wedding, Casual") };

        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        // isMultiple=false → multi helper requires IsMultiple, so tokens may remain or partial match
        Assert.True(matched.Count + remaining.Count + collisions.Count >= 1);
        if (matched.Count > 0)
        {
            Assert.True(matched[0].Values.Count <= 1);
        }
    }

    [Fact]
    public void ValidateAiSuggestions_CanonicalizesWrongValueNameAndRejectsInventedMulti()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            MultiAttr("100701", "Material", isMultiple: true, ("1000039", "Cotton"), ("1000011", "Polyester")),
        };
        var remaining = new List<(string Name, string Value)> { ("Material", "Cotton, Polyester") };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aiJson = """
            [
              {
                "sourceName":"Material",
                "sourceValue":"Cotton, Polyester",
                "attributeId":"100701",
                "values":[
                  {"valueId":"1000039","valueName":"Wrong Cotton Label"},
                  {"valueId":"999999","valueName":"Invented"},
                  {"valueId":"1000011","valueName":"Polyester"}
                ],
                "confidence":0.95
              }
            ]
            """;

        var validated = ListingSpecificsMappingEngine.ValidateAiSuggestions(
            aiJson,
            remaining,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            used);

        Assert.Single(validated);
        Assert.Equal(2, validated[0].Values.Count);
        Assert.Contains(validated[0].Values, v => v.ValueId == "1000039" && v.ValueName == "Cotton");
        Assert.Contains(validated[0].Values, v => v.ValueId == "1000011" && v.ValueName == "Polyester");
        Assert.DoesNotContain(validated[0].Values, v => v.ValueId == "999999");
    }

    [Fact]
    public void ValidateAiSuggestions_RejectsNonInt64AttributeId()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("Brand", "Brand", true, ("0", "No brand")),
        };
        var remaining = new List<(string Name, string Value)> { ("Brand", "No brand") };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aiJson = """
            [{"sourceName":"Brand","sourceValue":"No brand","attributeId":"Brand","valueId":"0","valueName":"No brand","confidence":0.99}]
            """;

        var validated = ListingSpecificsMappingEngine.ValidateAiSuggestions(
            aiJson,
            remaining,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            used);

        Assert.Empty(validated);
    }

    /// <summary>
    /// The six values 4Seller really offers for <c>Washing instructions</c> on category 903824 — the
    /// category the publish failures came from, and the reason a default must be chosen on meaning:
    /// two of the six only forbid something and one is wrong for everyday apparel.
    /// </summary>
    private static FourSellerCategoryAttributeDto Category903824Washing() =>
        Attr(
            "100401",
            "Washing instructions",
            true,
            ("1001199", "Machine washable"),
            ("1001198", "Do not dry clean"),
            ("1001200", "Cold wash"),
            ("1001196", "Dry clean"),
            ("1001195", "Hand wash only"),
            ("1001197", "Do not wash"));

    private static FourSellerCategoryAttributeDto Attr(
        string id,
        string name,
        bool required,
        params (string ValueId, string ValueName)[] values) =>
        new()
        {
            AttributeId = id,
            AttributeName = name,
            IsRequired = required,
            IsMultiple = false,
            Values = values.Select(v => new FourSellerCategoryAttributeValueDto
            {
                ValueId = v.ValueId,
                ValueName = v.ValueName
            }).ToList()
        };

    private static FourSellerCategoryAttributeDto MultiAttr(
        string id,
        string name,
        bool isMultiple,
        params (string ValueId, string ValueName)[] values) =>
        new()
        {
            AttributeId = id,
            AttributeName = name,
            IsRequired = false,
            IsMultiple = isMultiple,
            Values = values.Select(v => new FourSellerCategoryAttributeValueDto
            {
                ValueId = v.ValueId,
                ValueName = v.ValueName
            }).ToList()
        };
}
