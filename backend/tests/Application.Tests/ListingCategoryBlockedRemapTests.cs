using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingCategoryBlockedRemapTests
{
    [Fact]
    public void Parse_ReadsColonPairs()
    {
        var map = ListingCategoryBlockedRemap.Parse("903824:903952, 904000:904001");

        Assert.Equal(2, map.Count);
        Assert.Equal("903952", map["903824"]);
        Assert.Equal("904001", map["904000"]);
    }

    [Fact]
    public void Parse_SkipsMalformedAndSelfMaps()
    {
        var map = ListingCategoryBlockedRemap.Parse(
            "903824:903952; bad; :empty; self:self; 903824:903952");

        Assert.Single(map);
        Assert.Equal("903952", map["903824"]);
    }

    [Fact]
    public void Parse_Empty_ReturnsEmptyMap()
    {
        Assert.Empty(ListingCategoryBlockedRemap.Parse(null));
        Assert.Empty(ListingCategoryBlockedRemap.Parse("   "));
    }

    [Fact]
    public void TryResolve_IsCaseInsensitive()
    {
        var map = ListingCategoryBlockedRemap.Parse(
            ListingAutoPublishSettingKeys.DefaultCategoryBlockedRemap);

        Assert.True(ListingCategoryBlockedRemap.TryResolve(map, "903824", out var a));
        Assert.Equal("903952", a);
        Assert.True(ListingCategoryBlockedRemap.TryResolve(map, "903824", out var b));
        Assert.Equal(a, b);
        Assert.True(ListingCategoryBlockedRemap.IsBlocked(map, "903824"));
        Assert.False(ListingCategoryBlockedRemap.TryResolve(map, "903952", out _));
    }

    [Fact]
    public void DefaultSetting_MapsCostumeTopsToCostumeSets()
    {
        var map = ListingCategoryBlockedRemap.Parse(
            ListingAutoPublishSettingKeys.DefaultCategoryBlockedRemap);

        Assert.True(ListingCategoryBlockedRemap.TryResolve(map, "903824", out var replacement));
        Assert.Equal("903952", replacement);
    }
}
