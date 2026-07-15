using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class BoundedHostGatePoolTests
{
    [Fact]
    public void Retains_fixed_number_of_gates_for_many_hosts()
    {
        var pool = new BoundedHostGatePool(64, 2);

        for (int index = 0; index < 10_000; index++)
            _ = pool.GetGate($"host-{index}.example");

        Assert.Equal(64, pool.GateCount);
    }

    [Fact]
    public void Host_lookup_is_case_insensitive_and_stable()
    {
        var pool = new BoundedHostGatePool(64, 2);

        Assert.Same(pool.GetGate("Images.Example.com"), pool.GetGate("images.example.com"));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(64, 0)]
    public void Rejects_invalid_configuration(int gateCount, int concurrencyPerGate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedHostGatePool(gateCount, concurrencyPerGate));
    }
}
