using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.UnitTests.Domain;

public class OrderStatusTransitionsTests
{
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> Legal =
    [
        (OrderStatus.Pending, OrderStatus.Processing),
        (OrderStatus.Pending, OrderStatus.Cancelled),
        (OrderStatus.Processing, OrderStatus.Shipped),
        (OrderStatus.Shipped, OrderStatus.Delivered),
    ];

    /// <summary>Every (from, to) pair of statuses: 25 cases, so no transition is left untested.</summary>
    public static TheoryData<OrderStatus, OrderStatus> AllPairs()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        {
            foreach (var to in Enum.GetValues<OrderStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void CanTransition_matches_the_documented_state_machine(OrderStatus from, OrderStatus to)
    {
        OrderStatusTransitions.CanTransition(from, to).Should().Be(Legal.Contains((from, to)));
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Order_ChangeStatus_enforces_the_state_machine(OrderStatus from, OrderStatus to)
    {
        var order = OrderTests.OrderIn(from);

        var act = () => order.ChangeStatus(to, DateTimeOffset.UtcNow);

        if (Legal.Contains((from, to)))
        {
            act.Should().NotThrow();
            order.Status.Should().Be(to);
        }
        else
        {
            act.Should().Throw<InvalidOrderStateTransitionException>();
            order.Status.Should().Be(from);
        }
    }

    [Fact]
    public void Terminal_states_allow_no_further_transitions()
    {
        OrderStatusTransitions.AllowedFrom(OrderStatus.Delivered).Should().BeEmpty();
        OrderStatusTransitions.AllowedFrom(OrderStatus.Cancelled).Should().BeEmpty();
    }
}
