using Shared.Helpers;
using Xunit;

namespace Application.Tests;

public class ProfileNameComparerTests
{
    [Theory]
    [InlineData("KBT_SHOP_9", "KBT_SHOP_10", -1)]
    [InlineData("KBT_SHOP_041", "KBT_SHOP_41", 0)]
    [InlineData("KBT-SHOP 41", "KBT_SHOP_41", 0)]
    [InlineData("KBT_SHOP_143", "KBT_SHOP_2", 1)]
    [InlineData("", "KBT_SHOP_1", 1)]
    [InlineData("AAA_1", "BBB_1", -1)]
    public void Compare_MatchesShopsProfileSort(string left, string right, int expectedSign)
    {
        var result = ProfileNameComparer.Instance.Compare(left, right);
        if (expectedSign == 0)
        {
            Assert.Equal(0, result);
        }
        else
        {
            Assert.Equal(expectedSign, Math.Sign(result));
        }
    }
}
