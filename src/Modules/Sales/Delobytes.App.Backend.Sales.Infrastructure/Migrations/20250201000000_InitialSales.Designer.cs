using System;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Sales.Infrastructure.Migrations;

[DbContext(typeof(SalesDbContext))]
[Migration("20250201000000_InitialSales")]
partial class InitialSales
{
    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasDefaultSchema("sales")
            .HasAnnotation("ProductVersion", "8.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("Delobytes.App.Backend.Sales.Domain.Entities.Order", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<Guid>("ChannelId")
                    .HasColumnType("uuid");

                b.Property<Guid>("ChannelProductId")
                    .HasColumnType("uuid");

                b.Property<decimal>("Commission")
                    .HasPrecision(18, 2)
                    .HasColumnType("numeric(18,2)");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("ExternalOrderId")
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<DateTimeOffset>("ImportedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<decimal>("NetRevenue")
                    .HasPrecision(18, 2)
                    .HasColumnType("numeric(18,2)");

                b.Property<DateTimeOffset>("OrderDate")
                    .HasColumnType("timestamp with time zone");

                b.Property<int>("Quantity")
                    .HasColumnType("integer");

                b.Property<Guid?>("RawDataId")
                    .HasColumnType("uuid");

                b.Property<decimal>("Revenue")
                    .HasPrecision(18, 2)
                    .HasColumnType("numeric(18,2)");

                b.Property<string>("Status")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.HasKey("Id");

                b.HasIndex("ChannelId");

                b.HasIndex("ChannelProductId");

                b.HasIndex("OrderDate");

                b.HasIndex("Status");

                b.HasIndex("TenantId");

                b.HasIndex("ExternalOrderId", "ChannelId")
                    .IsUnique();

                b.ToTable("Orders", "sales");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Sales.Domain.Entities.Return", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<Guid>("OrderId")
                    .HasColumnType("uuid");

                b.Property<int>("Quantity")
                    .HasColumnType("integer");

                b.Property<string>("Reason")
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

                b.Property<decimal>("RefundAmount")
                    .HasPrecision(18, 2)
                    .HasColumnType("numeric(18,2)");

                b.Property<DateTimeOffset>("ReturnDate")
                    .HasColumnType("timestamp with time zone");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.HasKey("Id");

                b.HasIndex("OrderId");

                b.HasIndex("ReturnDate");

                b.HasIndex("TenantId");

                b.ToTable("Returns", "sales");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Sales.Domain.Entities.Return", b =>
            {
                b.HasOne("Delobytes.App.Backend.Sales.Domain.Entities.Order", "Order")
                    .WithMany("Returns")
                    .HasForeignKey("OrderId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Order");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Sales.Domain.Entities.Order", b =>
            {
                b.Navigation("Returns");
            });
#pragma warning restore 612, 618
    }
}
