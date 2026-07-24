using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherLocationLabelPolicyTests
{
    [Theory]
    [InlineData("浙江省", "杭州市", "浙江省 · 杭州市")]
    [InlineData("California", "San Francisco", "California · San Francisco")]
    [InlineData("北京市", "北京市", "北京市")]
    [InlineData("上海市", "上海", "上海")]
    [InlineData("广东省", "", "广东省")]
    [InlineData("", "深圳市", "深圳市")]
    public void Province_and_city_are_compact_and_duplicates_are_removed(string province, string city, string expected)
        => Assert.Equal(expected, WeatherLocationLabelPolicy.FormatProvinceCity(province, city));

    [Theory]
    [InlineData("浙江省", "杭州市", "余杭区", "仓前街道", "文一西路", "仓前街道 · 文一西路")]
    [InlineData("浙江省", "杭州市", "余杭区", "仓前街道", "", "余杭区 · 仓前街道")]
    [InlineData("广东省", "深圳市", "南山区", "", "", "深圳市 · 南山区")]
    [InlineData("四川省", "成都市", "郫都区", "红光镇", "红光镇", "郫都区 · 红光镇")]
    public void Detailed_location_prefers_the_two_most_specific_distinct_levels(
        string province,
        string city,
        string district,
        string township,
        string street,
        string expected)
        => Assert.Equal(expected, WeatherLocationLabelPolicy.FormatDetailed(province, city, district, township, street));

    [Theory]
    [InlineData("Fusong County · Lushuihe", "Lushuihe")]
    [InlineData("余杭区 · 仓前街道", "余杭区 · 仓前街道")]
    [InlineData("Very Long Administrative Area · Long Specific Locality", "Long Specific Loc…")]
    public void Weather_bar_keeps_the_specific_part_when_the_full_location_is_long(string location, string expected)
        => Assert.Equal(expected, WeatherLocationLabelPolicy.FormatForWeatherBar(location));
}
