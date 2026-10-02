using FluentValidation;

namespace OrderProcessing.Orders.Application;

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public const int MaxItemsPerOrder = 100;
    public const int MaxQuantityPerItem = 10_000;

    public CreateOrderRequestValidator()
    {
        RuleFor(r => r.CustomerId).NotEmpty().MaximumLength(64);

        RuleFor(r => r.Items)
            .NotEmpty().WithMessage("An order must contain at least one item.")
            .Must(items => items is null || items.Count <= MaxItemsPerOrder)
                .WithMessage($"An order may contain at most {MaxItemsPerOrder} items.")
            .Must(HaveDistinctProducts)
                .WithMessage("Each product may appear only once per order; combine the quantities instead.");

        RuleForEach(r => r.Items).NotNull().SetValidator(new CreateOrderItemRequestValidator());
    }

    private static bool HaveDistinctProducts(IReadOnlyList<CreateOrderItemRequest>? items) =>
        items is null
        || items.Where(i => i is not null && !string.IsNullOrWhiteSpace(i.ProductId))
               .GroupBy(i => i.ProductId.Trim(), StringComparer.OrdinalIgnoreCase)
               .All(g => g.Count() == 1);
}

public sealed class CreateOrderItemRequestValidator : AbstractValidator<CreateOrderItemRequest>
{
    public CreateOrderItemRequestValidator()
    {
        RuleFor(i => i.ProductId).NotEmpty().MaximumLength(64);
        RuleFor(i => i.ProductName).NotEmpty().MaximumLength(200);
        RuleFor(i => i.Quantity).InclusiveBetween(1, CreateOrderRequestValidator.MaxQuantityPerItem);
        RuleFor(i => i.UnitPrice)
            .GreaterThan(0)
            // Money is stored as numeric(18,2); reject values that would be silently rounded.
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);
    }
}

public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(r => r.Status).NotNull().IsInEnum();
    }
}

public sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQuery>
{
    public const int MaxPageSize = 100;

    public ListOrdersQueryValidator()
    {
        // Query-string binding accepts numbers for enums (e.g. ?status=7), so check the value is defined.
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status.HasValue);
        RuleFor(q => q.CustomerId).MaximumLength(64);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}
