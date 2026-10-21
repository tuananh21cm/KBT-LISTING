using System.Text.Json;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class SizeChartPromptBuilderTests
{
    private const string Template =
        "Product: {{PRODUCT_TYPE}}\nData:\n{{SIZE_CHART_DATA}}\nGuides:\n{{MEASURE_GUIDES}}\nNote: {{MEASURE_NOTE}}";

    [Fact]
    public void CmToInch_ConvertsWithOneDecimal()
    {
        Assert.Equal("39.4", SizeChartPromptBuilder.CmToInch("100"));
        Assert.Equal("31.5", SizeChartPromptBuilder.CmToInch("80"));
    }

    [Fact]
    public void CmToInch_ConvertsRange()
    {
        Assert.Equal("30.3-36.2", SizeChartPromptBuilder.CmToInch("77.0-92.0 cm"));
    }

    [Fact]
    public void CmToInch_ConvertsDualLengthCm()
    {
        Assert.Equal("13.6-24.8", SizeChartPromptBuilder.CmToInch("63.0/34.6 cm"));
    }

    [Fact]
    public void CmToInch_Missing_ReturnsDash()
    {
        Assert.Equal("—", SizeChartPromptBuilder.CmToInch(null));
        Assert.Equal("—", SizeChartPromptBuilder.CmToInch(""));
    }

    [Fact]
    public void ExtractProductType_FindsDress()
    {
        var type = SizeChartPromptBuilder.ExtractProductType(
            "SHEIN Summer Women's Floral Mini Dress Casual");
        Assert.Equal("Mini Dress", type);
    }

    [Fact]
    public void BuildSizeDataBlock_UsesChartRowsInch()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "unit": "inch",
              "chartRows": [
                {
                  "size": "XS",
                  "sizeId": "755",
                  "usSize": "2",
                  "bust": "30.3-36.2",
                  "waist": "24.4-36.2",
                  "hip": "34.3-35.8",
                  "length": "13.6-24.8"
                },
                {
                  "size": "S",
                  "sizeId": "568",
                  "usSize": "4",
                  "bust": "31.9-37.8",
                  "waist": "26-37.8",
                  "hip": "35.8-37.4",
                  "length": "13.8-25.2"
                }
              ]
            }
            """);

        var block = SizeChartPromptBuilder.BuildSizeDataBlock(json);
        Assert.Contains("SIZE | US | BUST | WAIST | HIP | LENGTH", block);
        Assert.Contains("XS | 2 | 30.3-36.2 | 24.4-36.2 | 34.3-35.8 | 13.6-24.8", block);
        Assert.Contains("S | 4 | 31.9-37.8 | 26-37.8 | 35.8-37.4 | 13.8-25.2", block);
    }

    [Fact]
    public void BuildSizeDataBlock_LegacyCmRows_ConvertsRanges()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "unit": "cm",
              "dimensions": [
                { "name": "Bust", "nameEn": "Bust" },
                { "name": "Waist Size", "nameEn": "Waist Size" },
                { "name": "Length", "nameEn": "Length" }
              ],
              "rows": [
                {
                  "size": "XS",
                  "usSize": "2",
                  "measurements": {
                    "Bust": " 77.0-92.0 cm",
                    "Waist Size": " 62.0-92.0 cm",
                    "Length": " 63.0/34.6 cm"
                  }
                }
              ]
            }
            """);

        var block = SizeChartPromptBuilder.BuildSizeDataBlock(json);
        Assert.Contains("XS | 2 | 30.3-36.2 | 24.4-36.2 | — | 13.6-24.8", block);
    }

    [Fact]
    public void BuildSizeDataBlock_ExcludesSleeveLengthFromLengthColumn()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "unit": "cm",
              "dimensions": [
                { "name": "Bust", "nameEn": "Bust" },
                { "name": "Sleeve Length", "nameEn": "Sleeve Length" },
                { "name": "Dress Length", "nameEn": "Dress Length" }
              ],
              "rows": [
                {
                  "size": "S",
                  "usSize": "4",
                  "measurements": { "Bust": "84", "Sleeve Length": "60", "Dress Length": "90" }
                }
              ]
            }
            """);

        var block = SizeChartPromptBuilder.BuildSizeDataBlock(json);
        Assert.Contains("S | 4 | 33.1 | — | — | 35.4", block);
    }

    [Fact]
    public void BuildMeasureGuidesBlock_MergesProductAndBodyHints()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "productMeasureGuide": [
                { "name": "Bust", "description": "Measure from the stitches below the armpits on one side to another.", "sort": 1 },
                { "name": "Length", "description": "Measure from where the shoulder seam meets the collar to the hem.", "sort": 2 }
              ],
              "bodyMeasureGuide": [
                { "name": "Your bust", "description": "Measure the circumference over the fullest part of your bust.", "sort": 1 },
                { "name": "Your waist", "description": "Measure your waist at the thinnest place.", "sort": 2 },
                { "name": "Your hips", "description": "Measure the fullest part of your hips.", "sort": 3 }
              ]
            }
            """);

        var block = SizeChartPromptBuilder.BuildMeasureGuidesBlock(json);
        Assert.Contains("BUST: Measure from the stitches below the armpits", block);
        Assert.Contains("WAIST: Measure your waist at the thinnest place.", block);
        Assert.Contains("HIP: Measure the fullest part of your hips.", block);
        Assert.Contains("LENGTH: Measure from where the shoulder seam meets the collar", block);
    }

    [Fact]
    public void GetMeasureNote_UsesStoredValueOrDefault()
    {
        var withNote = JsonSerializer.Deserialize<JsonElement>(
            """{"measureNote":"This data was obtained from manually measuring the product, it may be off by 0.39-0.79 IN."}""");
        Assert.Contains("0.39-0.79", SizeChartPromptBuilder.GetMeasureNote(withNote));

        var without = JsonSerializer.Deserialize<JsonElement>("{}");
        Assert.Contains("0.4-0.8", SizeChartPromptBuilder.GetMeasureNote(without));
    }

    [Fact]
    public void Build_ReplacesAllPlaceholders()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "unit": "inch",
              "measureNote": "Tolerance 0.39-0.79 IN.",
              "chartRows": [
                {
                  "size": "S",
                  "usSize": "4",
                  "bust": "31.9-37.8",
                  "waist": "26-37.8",
                  "hip": "35.8-37.4",
                  "length": "13.8-25.2"
                }
              ],
              "productMeasureGuide": [
                { "name": "Bust", "description": "Product bust guide.", "sort": 1 }
              ],
              "bodyMeasureGuide": [
                { "name": "Your waist", "description": "Body waist guide.", "sort": 1 }
              ]
            }
            """);

        var prompt = SizeChartPromptBuilder.Build(
            Template,
            json,
            "Women's Mini Dress");

        Assert.Contains("Product: Mini Dress", prompt);
        Assert.Contains("S | 4 | 31.9-37.8 | 26-37.8 | 35.8-37.4 | 13.8-25.2", prompt);
        Assert.Contains("BUST: Product bust guide.", prompt);
        Assert.Contains("WAIST: Body waist guide.", prompt);
        Assert.Contains("Note: Tolerance 0.39-0.79 IN.", prompt);
        Assert.DoesNotContain("{{PRODUCT_TYPE}}", prompt);
        Assert.DoesNotContain("{{SIZE_CHART_DATA}}", prompt);
        Assert.DoesNotContain("{{MEASURE_GUIDES}}", prompt);
        Assert.DoesNotContain("{{MEASURE_NOTE}}", prompt);
    }
}
