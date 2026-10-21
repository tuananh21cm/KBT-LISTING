using Application.DTOs.FourSeller;
using Application.DTOs.Listing;
using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Specifics are mandatory per category, not per platform: the live 4Seller schema of <c>897040</c>
/// (Women's shapewear) marks nothing mandatory, while <c>903824</c> (Costumes) demands Weaving method,
/// Washing instructions, Region of origin and Material. These tests pin both shapes so a listing moved
/// between the two keeps publishing.
/// </summary>
public class ListingCategoryRequiredSpecificsTests
{
    private static FourSellerCategoryAttributeDto Attr(
        string id,
        string name,
        bool required = false,
        bool multiple = false,
        bool customizable = false,
        string? attrType = "3",
        string? conditions = null,
        params (string Id, string Name)[] values) =>
        new()
        {
            AttributeId = id,
            AttributeName = name,
            IsRequired = required,
            IsMultiple = multiple,
            IsCustomizable = customizable,
            AttributeType = attrType,
            RequirementConditionsJson = conditions,
            Values = values
                .Select(v => new FourSellerCategoryAttributeValueDto { ValueId = v.Id, ValueName = v.Name })
                .ToList()
        };

    /// <summary>Shapewear 897040, trimmed to the attributes that decide the mandatory set.</summary>
    private static List<FourSellerCategoryAttributeDto> Schema897040() =>
    [
        Attr("101395", "CA prop 65: repro. chems", values: [("1000058", "Yes"), ("1000059", "No")]),
        Attr("101400", "CA prop 65: carcinogens", values: [("1000058", "Yes"), ("1000059", "No")]),
        Attr(
            "101398",
            "Reprotoxic chemicals",
            multiple: true,
            conditions: """[{"condition_type":"VALUE_ID_MATCH","attribute_id":"101395","attribute_value_id":"1000058"}]""",
            values: [("1022356", "Acrylamide")]),
        Attr("100401", "Washing instructions", multiple: true, values:
        [
            ("1001196", "Dry clean"), ("1001197", "Do not wash"), ("1001198", "Do not dry clean"),
            ("1001199", "Machine washable"), ("1001200", "Cold wash"), ("1001195", "Hand wash only")
        ]),
        Attr("100000", "Color", multiple: true, customizable: true, attrType: "2", values: [("1001063", "Black")]),
        Attr("100007", "Size", multiple: true, customizable: true, attrType: "2", values: [("1003579", "S")]),
        Attr("100701", "Material", multiple: true, customizable: true, values:
        [
            ("1000045", "Wool"), ("1000039", "Cotton"), ("1000011", "Polyester")
        ]),
        Attr("100149", "Country of origin", multiple: true, customizable: true, values:
        [
            ("1000850", "China"), ("1089295", "USA")
        ])
    ];

    /// <summary>Costumes 903824, trimmed: four mandatory attributes plus the optional Material twin.</summary>
    private static List<FourSellerCategoryAttributeDto> Schema903824() =>
    [
        Attr("101095", "Weaving method", required: true, values:
        [
            ("1005753", "Not applicable"), ("1005846", "Woven"), ("1005845", "Knitting")
        ]),
        Attr("100401", "Washing instructions", required: true, multiple: true, customizable: true, values:
        [
            ("1001199", "Machine washable"), ("1001198", "Do not dry clean"), ("1001200", "Cold wash"),
            ("1001196", "Dry clean"), ("1001195", "Hand wash only"), ("1001197", "Do not wash")
        ]),
        Attr("100336", "Region of origin", required: true, multiple: true, customizable: true, values:
        [
            ("1000850", "China"), ("1000853", "Thailand"), ("1000854", "Vietnam"), ("1000860", "US")
        ]),
        Attr("100157", "Material", required: true, multiple: true, customizable: true, values:
        [
            ("1000045", "Wool"), ("1000039", "Cotton"), ("1000044", "Silk"), ("1000011", "Polyester"),
            ("1000042", "Leather")
        ]),
        Attr("100701", "Material", multiple: true, customizable: true, values:
        [
            ("1000011", "Polyester"), ("1000039", "Cotton"), ("1133972", "Cotton blend"),
            ("1000042", "Leather")
        ]),
        Attr("101303", "Care instructions", multiple: true, customizable: true, values:
        [
            ("1001199", "Machine washable"), ("1020413", "Do not bleach"), ("1001198", "Do not dry clean")
        ]),
        Attr("100000", "Color", multiple: true, customizable: true, attrType: "2"),
        Attr("101018", "Product type", customizable: true),
        Attr("101619", "Dangerous goods or hazardous materials", values:
        [
            ("1000059", "No"), ("1024360", "Not sure"), ("1000058", "Yes")
        ])
    ];

    [Fact]
    public void RequiredSpecificAttributes_Shapewear_HasNone()
    {
        var required = ListingSpecificsMappingEngine.RequiredSpecificAttributes(Schema897040());

        Assert.Empty(required);
    }

    [Fact]
    public void RequiredSpecificAttributes_Costumes_ListsTheFourMandatoryAttributes()
    {
        var required = ListingSpecificsMappingEngine.RequiredSpecificAttributes(Schema903824());

        Assert.Equal(
            ["100157", "100336", "100401", "101095"],
            required.Select(a => a.AttributeId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void RequiredSpecificAttributes_SkipsMandatoryAttributeWhoseGateIsClosed()
    {
        // A mandatory chemical list only exists while the Prop 65 answer is Yes, and we always answer No.
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr("101400", "CA prop 65: carcinogens", values: [("1000058", "Yes"), ("1000059", "No")]),
            Attr(
                "101397",
                "Carcinogen",
                required: true,
                multiple: true,
                conditions: """[{"condition_type":"VALUE_ID_MATCH","attribute_id":"101400","attribute_value_id":"1000058"}]""",
                values: [("1021756", "Benzene")])
        };

        Assert.Empty(ListingSpecificsMappingEngine.RequiredSpecificAttributes(schema));
    }

    [Fact]
    public void EnsureRequiredSpecifics_Shapewear_SeedsNothing()
    {
        var matched = new List<MappedListingSpecificDto>();

        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(
            matched,
            Schema897040(),
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Empty(matched);
    }

    [Fact]
    public void EnsureRequiredSpecifics_Costumes_SeedsEveryMandatoryAttribute()
    {
        var matched = new List<MappedListingSpecificDto>();

        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(
            matched,
            Schema903824(),
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Equal("Not applicable", Seeded(matched, "101095").AttrValue);
        Assert.Equal("China", Seeded(matched, "100336").AttrValue);
        Assert.Equal("Polyester", Seeded(matched, "100157").AttrValue);

        // Care advice must describe a way to wash, never only what not to do; the gentlest method is the
        // safe guess for an unknown garment.
        Assert.Equal("Hand wash only", Seeded(matched, "100401").AttrValue);

        // Optional twin / gated cert / free-text attributes are none of the step's business.
        Assert.DoesNotContain(matched, m => m.AttrId is "100701" or "101303" or "101018" or "101619");
        Assert.All(matched, m => Assert.True(m.IsRequired));
        Assert.All(matched, m => Assert.Equal("RequiredDefault", m.MatchMethod));
    }

    [Fact]
    public void EnsureRequiredSpecifics_Costumes_KeepsSingleSelectToOneValue()
    {
        var matched = new List<MappedListingSpecificDto>();

        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(
            matched,
            Schema903824(),
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Single(Seeded(matched, "101095").Values);
        Assert.All(matched, m => Assert.All(m.Values, v => Assert.False(string.IsNullOrWhiteSpace(v.ValueId))));
    }

    [Fact]
    public void EnsureRequiredSpecifics_ReusesTheMaterialAlreadyChosenOnTheTwinAttribute()
    {
        // 903824 carries two attributes named "Material": 100157 (mandatory) and 100701 (optional).
        // Seeding Polyester beside a supplier-stated Cotton would contradict the listing.
        var schema = Schema903824();
        var twin = schema.First(a => a.AttributeId == "100701");
        var matched = new List<MappedListingSpecificDto>
        {
            new()
            {
                AttrId = twin.AttributeId,
                AttrName = twin.AttributeName,
                AttrValue = "Cotton",
                ValueId = "1000039",
                Values = [new MappedListingSpecificValueDto { ValueId = "1000039", ValueName = "Cotton" }]
            }
        };

        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(
            matched,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Equal("Cotton", Seeded(matched, "100157").AttrValue);
    }

    [Fact]
    public void EnsureRequiredSpecifics_LeavesAlreadyFilledMandatoryRowsAlone()
    {
        var matched = new List<MappedListingSpecificDto>
        {
            new()
            {
                AttrId = "100336",
                AttrName = "Region of origin",
                AttrValue = "Vietnam",
                ValueId = "1000854",
                Values = [new MappedListingSpecificValueDto { ValueId = "1000854", ValueName = "Vietnam" }]
            }
        };

        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(
            matched,
            Schema903824(),
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        Assert.Equal("Vietnam", matched.Single(m => m.AttrId == "100336").AttrValue);
    }

    [Fact]
    public void EnsureRequiredSpecifics_HonoursAValueAlreadyOnTheListing()
    {
        var existing = new Dictionary<string, UpdateListingMirrorAttributeDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["100157"] = new()
            {
                AttrId = "100157",
                AttrName = "Material",
                AttrValue = "Silk",
                ValueId = "1000044"
            }
        };
        var matched = new List<MappedListingSpecificDto>();

        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(matched, Schema903824(), existing);

        Assert.DoesNotContain(matched, m => m.AttrId == "100157");
    }

    [Fact]
    public void ResolveRequiredDefaultValue_FreeTextMandatoryAttribute_ReturnsNull()
    {
        // No enum to pick from: the value has to come from the seller, so the step must report it
        // instead of inventing one.
        var attr = Attr("101018", "Product type", required: true, customizable: true);

        Assert.Null(ListingSpecificsMappingEngine.ResolveRequiredDefaultValue(attr));
    }

    [Fact]
    public void ResolveRequiredDefaultValue_Material_PrefersFibreNamedInTheTitle()
    {
        var attr = Attr(
            "100157",
            "Material",
            required: true,
            multiple: true,
            values:
            [
                ("1000011", "Polyester"),
                ("1000039", "Cotton"),
                ("1000999", "Linen"),
                ("1000045", "Wool")
            ]);

        var value = ListingSpecificsMappingEngine.ResolveRequiredDefaultValue(
            attr,
            sourceTitleText: "SHEIN Women's Oversized Linen Sweatshirt Casual Fall");

        Assert.Equal("Linen", value?.ValueName);
    }

    [Fact]
    public void ResolveRequiredDefaultValue_Material_FallsBackToPolyesterWhenTitleNamesNoFibre()
    {
        var attr = Attr(
            "100157",
            "Material",
            required: true,
            multiple: true,
            values:
            [
                ("1000045", "Wool"),
                ("1000039", "Cotton"),
                ("1000011", "Polyester")
            ]);

        var value = ListingSpecificsMappingEngine.ResolveRequiredDefaultValue(
            attr,
            sourceTitleText: "Women's Oversized Crewneck Sweatshirt");

        Assert.Equal("Polyester", value?.ValueName);
    }

    [Fact]
    public void ResolveRequiredDefaultValue_Material_FallsBackWhenTitleFibreIsNotInEnum()
    {
        // Title says Linen but the category only offers Polyester/Cotton/Wool — keep the safe default.
        var attr = Attr(
            "100157",
            "Material",
            required: true,
            multiple: true,
            values:
            [
                ("1000045", "Wool"),
                ("1000039", "Cotton"),
                ("1000011", "Polyester")
            ]);

        var value = ListingSpecificsMappingEngine.ResolveRequiredDefaultValue(
            attr,
            sourceTitleText: "Women's Oversized Linen Sweatshirt");

        Assert.Equal("Polyester", value?.ValueName);
    }

    [Fact]
    public void EnsureRequiredSpecifics_Material_UsesTitleFibreWhenSeeding()
    {
        var schema = new List<FourSellerCategoryAttributeDto>
        {
            Attr(
                "100157",
                "Material",
                required: true,
                multiple: true,
                values:
                [
                    ("1000011", "Polyester"),
                    ("1000999", "Linen"),
                    ("1000039", "Cotton")
                ])
        };
        var matched = new List<MappedListingSpecificDto>();

        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(
            matched,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>(),
            sourceTitleText: "Women's Linen Blend Sweatshirt");

        Assert.Equal("Linen", Seeded(matched, "100157").AttrValue);
    }

    [Fact]
    public void BuildMissingRequired_Costumes_IsEmptyOnceDefaultsAreSeeded()
    {
        var schema = Schema903824();
        var matched = new List<MappedListingSpecificDto>();
        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(
            matched,
            schema,
            new Dictionary<string, UpdateListingMirrorAttributeDto>());

        var missing = ListingSpecificsMappingEngine.BuildMissingRequired(
            schema,
            matched.Select(m => m.AttrId));

        Assert.Empty(missing);
    }

    [Fact]
    public void BuildMissingRequired_Costumes_NamesWhatIsStillEmpty()
    {
        var missing = ListingSpecificsMappingEngine.BuildMissingRequired(Schema903824(), ["101095", "100401"]);

        Assert.Equal(
            ["100157", "100336"],
            missing.Select(m => m.AttrId).OrderBy(id => id, StringComparer.Ordinal));
    }

    private static MappedListingSpecificDto Seeded(IEnumerable<MappedListingSpecificDto> matched, string attrId) =>
        Assert.Single(matched, m => m.AttrId == attrId);
}
