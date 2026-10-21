using Application.DTOs.ProductOpportunity;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class ProductOpportunityCategoryInfosTests
{
    [Fact]
    public void BuildGroups_SingleLeaf_OneGroupWithApiLevel()
    {
        var groups = ProductOpportunityCategoryInfos.BuildGroups(
            ["843400"],
            Nodes(("843400", 2)));

        var path = Assert.Single(groups);
        Assert.Equal([(843400L, 3)], path.Select(P).ToArray());
    }

    [Fact]
    public void BuildGroups_TwoSiblingLeaves_TwoSeparateGroups()
    {
        var groups = ProductOpportunityCategoryInfos.BuildGroups(
            ["601284", "601295"],
            Nodes(("601284", 2), ("601295", 2)));

        Assert.Equal(2, groups.Count);
        Assert.Equal([(601284L, 3)], groups[0].Select(P).ToArray());
        Assert.Equal([(601295L, 3)], groups[1].Select(P).ToArray());
    }

    [Fact]
    public void BuildGroups_ParentAndChild_DoesNotConcatenateIntoOnePath()
    {
        var groups = ProductOpportunityCategoryInfos.BuildGroups(
            ["846864", "1038224"],
            Nodes(("846864", 2), ("1038224", 3)));

        Assert.Equal(2, groups.Count);
        Assert.Equal([(846864L, 3)], groups[0].Select(P).ToArray());
        Assert.Equal([(1038224L, 4)], groups[1].Select(P).ToArray());
    }

    [Fact]
    public void BuildGroups_DuplicateAndUnknownIds_Skipped()
    {
        var groups = ProductOpportunityCategoryInfos.BuildGroups(
            ["", "843400", "843400", "999"],
            Nodes(("843400", 2)));

        var path = Assert.Single(groups);
        Assert.Equal([(843400L, 3)], path.Select(P).ToArray());
    }

    private static Dictionary<string, ProductOpportunityCategoryInfos.Node> Nodes(
        params (string Id, int Level)[] rows) =>
        rows.ToDictionary(
            r => r.Id,
            r => new ProductOpportunityCategoryInfos.Node(r.Id, r.Level),
            StringComparer.Ordinal);

    private static (long CateId, int Level) P(ProductOpportunityCategoryInfoDto dto) =>
        (dto.CateId, dto.Level);
}
