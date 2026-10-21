using System.Text.Json.Nodes;
using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Guards the schema pass that keeps <c>productAttributes</c> publishable: TikTok answers
/// 【Unpublishable】Product attributes are unavailable — naming no row — when the list holds a sale
/// prop, an attribute from another category, or an enum value with no <c>valueId</c>.
/// </summary>
public class ListingProductAttributesSchemaTests
{
    /// <summary>Category 601302 (Women's T-shirts) shape: Color is option1, not a product attribute.</summary>
    private static JsonObject BuildPayload(string productAttributesJson) =>
        new()
        {
            ["categoryId"] = "601302",
            ["option1"] = """{"100000":"Color"}""",
            ["option1Value"] = """[{"1003577":"Multicolor"}]""",
            ["option2"] = """{"100007":"Size"}""",
            ["productAttributes"] = productAttributesJson,
            ["categoryProductAttributeList"] = new JsonArray
            {
                new JsonObject
                {
                    ["attrId"] = "100397",
                    ["attrName"] = "Season",
                    ["attrType"] = 3,
                    ["categoryId"] = "601302",
                    ["isCustomized"] = 0,
                    ["isMultipleSelected"] = 1,
                    ["valueList"] = """[{"id":"1003131","name":"Fall"},{"id":"1003132","name":"Spring"}]"""
                },
                new JsonObject
                {
                    ["attrId"] = "100198",
                    ["attrName"] = "Pattern",
                    ["attrType"] = 3,
                    ["categoryId"] = "601302",
                    ["isCustomized"] = 1,
                    ["isMultipleSelected"] = 0,
                    ["valueList"] = """[{"id":"1001182","name":"Plain"}]"""
                }
            }
        };

    /// <summary>
    /// Adds <c>Washing instructions</c> with the six values 4Seller really offers on category 903824,
    /// the category every custom-value rejection came from.
    /// </summary>
    private static JsonObject BuildWashingPayload(string valueJson)
    {
        var payload = BuildPayload(
            $$"""
            [{"attributeId":"100401","attributeName":"Washing instructions","values":[{{valueJson}}]}]
            """);
        payload["categoryProductAttributeList"]!.AsArray().Add(new JsonObject
        {
            ["attrId"] = "100401",
            ["attrName"] = "Washing instructions",
            ["attrType"] = 3,
            ["categoryId"] = "601302",
            ["isCustomized"] = 1,
            ["isMultipleSelected"] = 0,
            ["valueList"] = """
                [{"id":"1001199","name":"Machine washable"},{"id":"1001198","name":"Do not dry clean"},
                 {"id":"1001200","name":"Cold wash"},{"id":"1001196","name":"Dry clean"},
                 {"id":"1001195","name":"Hand wash only"},{"id":"1001197","name":"Do not wash"}]
                """
        });
        return payload;
    }

    private static List<JsonObject> ReadRows(JsonObject payload) =>
        (JsonNode.Parse(payload["productAttributes"]!.GetValue<string>()) as JsonArray)!
        .OfType<JsonObject>()
        .ToList();

    [Fact]
    public void Canonicalize_FillsMissingValueIdFromSchema()
    {
        var payload = BuildPayload(
            """[{"attributeId":"100397","attributeName":"Season","values":[{"valueId":"","valueName":"Fall"}]}]""");

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.Contains("Season (100397)", audit.Repaired);
        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("1003131", row["values"]![0]!["valueId"]!.GetValue<string>());
        Assert.Equal("Fall", row["values"]![0]!["valueName"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalize_DropsSalePropEchoedIntoProductAttributes()
    {
        var payload = BuildPayload(
            """
            [{"attributeId":"100000","attributeName":"Color","values":[{"valueId":"","valueName":"Multicolor"}]},
             {"attributeId":"100198","attributeName":"Pattern","values":[{"valueId":"1001182","valueName":"Plain"}]}]
            """);

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.Contains(audit.Dropped, d => d.StartsWith("Color (100000)", StringComparison.Ordinal));
        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("100198", row["attributeId"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalize_DropsAttributeThatIsNotInThisCategory()
    {
        var payload = BuildPayload(
            """[{"attributeId":"999999","attributeName":"Sole material","values":[{"valueId":"5","valueName":"Rubber"}]}]""");

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.Contains(audit.Dropped, d => d.StartsWith("Sole material (999999)", StringComparison.Ordinal));
        Assert.Empty(ReadRows(payload));
    }

    [Fact]
    public void Canonicalize_DropsFreeTextValueOnEnumOnlyAttribute()
    {
        var payload = BuildPayload(
            """[{"attributeId":"100397","attributeName":"Season","values":[{"valueId":"","valueName":"Monsoon"}]}]""");

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.Contains(audit.Dropped, d => d.StartsWith("Season (100397)", StringComparison.Ordinal));
        Assert.Empty(ReadRows(payload));
    }

    /// <summary>
    /// 4Seller marks Pattern <c>isCustomized=1</c>, but that flag proved unreliable: on category
    /// 903824 it says the same about Washing instructions while TikTok answers "attribute ID 100401
    /// does not support custom value names". So an enum-bearing attribute never carries a custom name.
    /// </summary>
    [Fact]
    public void Canonicalize_DropsFreeTextValueEvenWhenAttributeIsMarkedCustomizable()
    {
        var payload = BuildPayload(
            """[{"attributeId":"100198","attributeName":"Pattern","values":[{"valueId":"","valueName":"Tie dye swirl"}]}]""");

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.Single(audit.Dropped);
        Assert.Empty(ReadRows(payload));
    }

    [Fact]
    public void Canonicalize_TrimsExtraValuesOnSingleSelectAttribute()
    {
        var payload = BuildPayload(
            """
            [{"attributeId":"100198","attributeName":"Pattern",
              "values":[{"valueId":"1001182","valueName":"Plain"},{"valueId":"","valueName":"Tie dye"}]}]
            """);

        ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        var row = Assert.Single(ReadRows(payload));
        Assert.Single(row["values"]!.AsArray());
        Assert.Equal("1001182", row["values"]![0]!["valueId"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalize_MatchesEnumNameIgnoringCaseAndPunctuation()
    {
        var payload = BuildPayload(
            """[{"attributeId":"100397","attributeName":"Season","values":[{"valueId":"","valueName":"  fall  "}]}]""");

        ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("1003131", row["values"]![0]!["valueId"]!.GetValue<string>());
        Assert.Equal("Fall", row["values"]![0]!["valueName"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalize_TypedValueWinsOverStaleResolvedValueId()
    {
        var payload = BuildPayload(
            """
            [{"attributeId":"100397","attributeName":"Season","valueId":"1003131",
              "values":[{"valueId":"1003131","valueName":"Spring"}]}]
            """);

        ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("Spring", row["values"]![0]!["valueName"]!.GetValue<string>());
        Assert.Equal("1003132", row["values"]![0]!["valueId"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalize_KeepsRowsWhenEmbeddedSchemaBelongsToAnotherCategory()
    {
        var payload = BuildPayload(
            """[{"attributeId":"100397","attributeName":"Season","values":[{"valueId":"","valueName":"Fall"}]}]""");
        payload["categoryId"] = "601265";

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.Empty(audit.Dropped);
        Assert.Single(ReadRows(payload));
    }

    [Fact]
    public void Canonicalize_DropsSalePropEvenWithoutUsableSchema()
    {
        var payload = BuildPayload(
            """
            [{"attributeId":"100000","attributeName":"Color","values":[{"valueId":"","valueName":"Multicolor"}]},
             {"attributeId":"100397","attributeName":"Season","values":[{"valueId":"","valueName":"Fall"}]}]
            """);
        payload.Remove("categoryProductAttributeList");

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.Contains(audit.Dropped, d => d.StartsWith("Color (100000)", StringComparison.Ordinal));
        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("100397", row["attributeId"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalize_KeepsRegionOfOriginAndCpsiaWhenInSchema()
    {
        var payload = BuildPayload(
            """
            [{"attributeId":"100336","attributeName":"Region of origin","values":[{"valueId":"1000850","valueName":"China"}]},
             {"attributeId":"101200","attributeName":"CPSIA tracking label","values":[{"valueId":"1000059","valueName":"No"}]},
             {"attributeId":"100198","attributeName":"Pattern","values":[{"valueId":"1001182","valueName":"Plain"}]}]
            """);
        payload["categoryProductAttributeList"]!.AsArray().Add(new JsonObject
        {
            ["attrId"] = "100336",
            ["attrName"] = "Region of origin",
            ["attrType"] = 3,
            ["categoryId"] = "601302",
            ["isCustomized"] = 0,
            ["isMultipleSelected"] = 0,
            ["valueList"] = """[{"id":"1000850","name":"China"}]"""
        });
        payload["categoryProductAttributeList"]!.AsArray().Add(new JsonObject
        {
            ["attrId"] = "101200",
            ["attrName"] = "CPSIA tracking label",
            ["attrType"] = 3,
            ["categoryId"] = "601302",
            ["isCustomized"] = 0,
            ["isMultipleSelected"] = 0,
            ["valueList"] = """[{"id":"1000059","name":"No"}]"""
        });

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.DoesNotContain(audit.Dropped, d => d.Contains("Region of origin", StringComparison.Ordinal));
        Assert.DoesNotContain(audit.Dropped, d => d.Contains("CPSIA tracking label", StringComparison.Ordinal));
        var ids = ReadRows(payload).Select(r => r["attributeId"]!.GetValue<string>()).OrderBy(x => x).ToList();
        Assert.Equal(["100198", "100336", "101200"], ids);
    }

    [Fact]
    public void Canonicalize_MapsHandWashOnlyOntoHandWashEnum()
    {
        var payload = BuildPayload(
            """
            [{"attributeId":"100401","attributeName":"Washing instructions",
              "values":[{"valueId":"","valueName":"Hand wash only"}]}]
            """);
        payload["categoryProductAttributeList"]!.AsArray().Add(new JsonObject
        {
            ["attrId"] = "100401",
            ["attrName"] = "Washing instructions",
            ["attrType"] = 3,
            ["categoryId"] = "601302",
            ["isCustomized"] = 1,
            ["isMultipleSelected"] = 0,
            ["valueList"] = """[{"id":"1001","name":"Machine wash"},{"id":"1002","name":"Hand wash"},{"id":"1003","name":"Dry clean"}]"""
        });

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.DoesNotContain(audit.Dropped, d => d.Contains("100401", StringComparison.Ordinal));
        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("100401", row["attributeId"]!.GetValue<string>());
        Assert.Equal("1002", row["values"]![0]!["valueId"]!.GetValue<string>());
        Assert.Equal("Hand wash", row["values"]![0]!["valueName"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalize_KeepsWashingValueTikTokStillAccepts()
    {
        var payload = BuildWashingPayload("""{"valueId":"1001199","valueName":"Machine washable"}""");

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        // No evidence TikTok refuses this value, so it must survive untouched — the listings using it
        // are live. Blocking it on suspicion is what pushed every listing onto one shared value.
        Assert.DoesNotContain(audit.Dropped, d => d.Contains("100401", StringComparison.Ordinal));
        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("1001199", row["values"]![0]!["valueId"]!.GetValue<string>());
        Assert.Equal("Machine washable", row["values"]![0]!["valueName"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalize_ReplacesDenylistedWashingValueWithAWashingMethod()
    {
        var payload = BuildWashingPayload("""{"valueId":"1001198","valueName":"Do not dry clean"}""");
        var denylist = new TikTokAttributeValueDenylist([("100401", "Do not dry clean")]);

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload, denylist);

        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("100401", row["attributeId"]!.GetValue<string>());
        Assert.Equal("1001195", row["values"]![0]!["valueId"]!.GetValue<string>());
        Assert.Equal("Hand wash only", row["values"]![0]!["valueName"]!.GetValue<string>());
        Assert.Contains(audit.Repaired, r => r.Contains("100401", StringComparison.Ordinal));
    }

    [Fact]
    public void Canonicalize_ReplacesUnmappedWashingCustomEvenWhenIsCustomized()
    {
        var payload = BuildWashingPayload("""{"valueId":"","valueName":"Totally invented wash ritual"}""");

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        // Apparel requires this attribute, so dropping the row would only trade a custom-value
        // rejection for a missing-Specifics one.
        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("1001195", row["values"]![0]!["valueId"]!.GetValue<string>());
        Assert.Equal("Hand wash only", row["values"]![0]!["valueName"]!.GetValue<string>());
        Assert.Contains(audit.Repaired, r => r.Contains("100401", StringComparison.Ordinal));
    }

    [Fact]
    public void Canonicalize_KeepsSimplifiedMirrorsInSync()
    {
        var payload = BuildPayload(
            """
            [{"id":"100397","attrId":"100397","attributeId":"100397","name":"season","attrName":"season",
              "attributeName":"season","value":"fall","attrValue":"fall","valueId":"",
              "values":[{"valueId":"","valueName":"fall"}]}]
            """);

        ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("Season", row["attrName"]!.GetValue<string>());
        Assert.Equal("Season", row["name"]!.GetValue<string>());
        Assert.Equal("Fall", row["attrValue"]!.GetValue<string>());
        Assert.Equal("Fall", row["value"]!.GetValue<string>());
        Assert.Equal("1003131", row["valueId"]!.GetValue<string>());
    }

    [Fact]
    public void ParseAttributes_HidesSalePropRowFromTheSpecificsCard()
    {
        var payload = BuildPayload(
            """
            [{"attributeId":"100000","attributeName":"Color","values":[{"valueId":"","valueName":"Multicolor"}]},
             {"attributeId":"100198","attributeName":"Pattern","values":[{"valueId":"1001182","valueName":"Plain"}]}]
            """);

        var rows = ListingSyncService.ParseAttributes(payload);

        var row = Assert.Single(rows);
        Assert.Equal("100198", row.AttrId);
    }

    [Fact]
    public void ParseAttributes_BackfillsValueIdForSupplierTextRow()
    {
        var payload = BuildPayload(
            """[{"attributeId":"100397","attributeName":"season","attrValue":"Fall","valueId":""}]""");

        var row = Assert.Single(ListingSyncService.ParseAttributes(payload));

        Assert.Equal("Season", row.AttrName);
        Assert.Equal("1003131", row.ValueId);
        Assert.Equal("1003131", Assert.Single(row.Values!).ValueId);
    }

    [Fact]
    public void ParseAttributes_KeepsRowWithUnknownIdVisibleForTheSeller()
    {
        var payload = BuildPayload(
            """[{"attributeId":"999999","attributeName":"Sole material","attrValue":"Rubber","valueId":""}]""");

        var row = Assert.Single(ListingSyncService.ParseAttributes(payload));

        Assert.Equal("999999", row.AttrId);
        Assert.Equal("Rubber", row.AttrValue);
    }

    [Fact]
    public void Canonicalize_LeavesPayloadUntouchedWhenEverythingAlreadyCanonical()
    {
        var payload = BuildPayload(
            """
            [{"attributeId":"100397","attributeName":"Season","values":[{"valueId":"1003131","valueName":"Fall","isShow":"true"}],
              "attrId":"100397","id":"100397","attrName":"Season","name":"Season",
              "attrValue":"Fall","value":"Fall","valueId":"1003131"}]
            """);
        var before = payload["productAttributes"]!.GetValue<string>();

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.False(audit.HasChanges);
        Assert.Equal(before, payload["productAttributes"]!.GetValue<string>());
    }

    /// <summary>
    /// 4Seller resolves the value id off the flat <c>attrValue</c>/<c>valueId</c> mirror, so a row that
    /// carries only <c>values[]</c> publishes as a custom value name — TikTok answered "attribute ID
    /// 100401 does not support custom value names" for a payload whose nested valueId was correct.
    /// </summary>
    [Fact]
    public void Canonicalize_FillsTheFlatMirrorOnARowThatOnlyCarriesValues()
    {
        var payload = BuildPayload(
            """[{"attributeId":"100397","attributeName":"Season","values":[{"valueId":"1003131","valueName":"Fall"}]}]""");

        var audit = ListingSyncService.CanonicalizeProductAttributesAgainstSchema(payload);

        Assert.True(audit.HasChanges);
        var row = Assert.Single(ReadRows(payload));
        Assert.Equal("100397", row["attrId"]!.GetValue<string>());
        Assert.Equal("Fall", row["attrValue"]!.GetValue<string>());
        Assert.Equal("1003131", row["valueId"]!.GetValue<string>());
    }
}
