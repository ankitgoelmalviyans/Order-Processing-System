using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Infrastructure.Persistence;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(o => o.CustomerId).HasColumnName("customer_id").HasMaxLength(64).IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 2);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        builder.Property(o => o.Version).HasColumnName("version").IsConcurrencyToken();

        // Serves "list by status, newest first" and the background job's "WHERE status = 'Pending'".
        builder.HasIndex(o => new { o.Status, o.CreatedAt }).HasDatabaseName("ix_orders_status_created_at");
        builder.HasIndex(o => o.CustomerId).HasDatabaseName("ix_orders_customer_id");

        // Items are part of the aggregate: no repository of their own, loaded and saved with the order.
        builder.OwnsMany(o => o.Items, items =>
        {
            items.ToTable("order_items");
            items.WithOwner().HasForeignKey("order_id");
            items.HasKey(i => i.Id);
            items.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            items.Property(i => i.LineNumber).HasColumnName("line_number");
            items.Property(i => i.ProductId).HasColumnName("product_id").HasMaxLength(64).IsRequired();
            items.Property(i => i.ProductName).HasColumnName("product_name").HasMaxLength(200).IsRequired();
            items.Property(i => i.Quantity).HasColumnName("quantity");
            items.Property(i => i.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 2);
            items.Ignore(i => i.LineTotal);
        });

        builder.Navigation(o => o.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
