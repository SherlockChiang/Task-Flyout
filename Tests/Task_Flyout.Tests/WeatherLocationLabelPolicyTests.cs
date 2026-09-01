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
    [InlineData("抚松县 · 露水河镇", "露水河镇")]
    [InlineData("余杭区 · 仓前街道", "仓前街道")]
    [InlineData("杭州市", "杭州市")]
    [InlineData("Very Long Administrative Area · Long Specific Locality", "Long Specific Loc…")]
    public void Weather_bar_keeps_only_the_most_specific_location_part(string location, string expected)
        => Assert.Equal(expected, WeatherLocationLabelPolicy.FormatForWeatherBar(location));

    [Theory]
    [InlineData("余杭区 · 仓前街道", "杭州市", "当前位置", "余杭区 · 仓前街道")]
    [InlineData("", "浙江省 · 杭州市", "当前位置", "浙江省 · 杭州市")]
    [InlineData("", "", "西湖区 · 古荡街道", "西湖区 · 古荡街道")]
    [InlineData("当前位置", "", "西湖区 · 古荡街道", "西湖区 · 古荡街道")]
    [InlineData("目前位置", "", "", "目前位置")]
    public void Location_resolution_prefers_specific_labels(
        string reverseGeocoded,
        string civicAddress,
        string previousLabel,
        string expected)
        => Assert.Equal(
            expected,
            WeatherLocationLabelPolicy.ChooseSpecificLabel(
                reverseGeocoded,
                civicAddress,
                previousLabel,
                "Current location"));
}
