using System;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20250201000000_InitialCatalog")]
partial class InitialCatalog
{
    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasDefaultSchema("catalog")
            .HasAnnotation("ProductVersion", "8.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Channel", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("CustomApiUrl")
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<bool>("IsCustom")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<Guid?>("SystemChannelTemplateId")
                    .HasColumnType("uuid");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("IsCustom");

                b.HasIndex("SystemChannelTemplateId");

                b.HasIndex("TenantId");

                b.ToTable("Channels", "catalog");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ChannelProduct", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<Guid>("ChannelId")
                    .HasColumnType("uuid");

                b.Property<string>("ChannelSpecificData")
                    .HasColumnType("text");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("ExternalProductId")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<string>("ExternalSku")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<DateTimeOffset?>("LastSyncedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("ChannelId");

                b.HasIndex("ExternalProductId");

                b.HasIndex("IsActive");

                b.HasIndex("LastSyncedAt");

                b.HasIndex("TenantId");

                b.HasIndex("ProductId", "ChannelId")
                    .IsUnique();

                b.ToTable("ChannelProducts", "catalog");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Component", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<decimal>("PurchasePrice")
                    .HasPrecision(18, 2)
                    .HasColumnType("numeric(18,2)");

                b.Property<string>("Supplier")
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.Property<string>("Unit")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("TenantId");

                b.ToTable("Components", "catalog");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.LaborRate", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<bool>("IncludesTaxes")
                    .HasColumnType("boolean");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<decimal>("Rate")
                    .HasPrecision(18, 2)
                    .HasColumnType("numeric(18,2)");

                b.Property<string>("RatePeriod")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("TenantId");

                b.ToTable("LaborRates", "catalog");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Product", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("Description")
                    .HasMaxLength(2000)
                    .HasColumnType("character varying(2000)");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<string>("Sku")
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("TenantId");

                b.HasIndex("Sku")
                    .IsUnique();

                b.ToTable("Products", "catalog");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductComponent", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<Guid>("ComponentId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<decimal>("Quantity")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.HasKey("Id");

                b.HasIndex("ComponentId");

                b.HasIndex("TenantId");

                b.HasIndex("ProductId", "ComponentId")
                    .IsUnique();

                b.ToTable("ProductComponents", "catalog");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductLaborCost", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<Guid>("LaborRateId")
                    .HasColumnType("uuid");

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.Property<int>("UnitsProducedPerPeriod")
                    .HasColumnType("integer");

                b.HasKey("Id");

                b.HasIndex("LaborRateId");

                b.HasIndex("TenantId");

                b.HasIndex("ProductId", "LaborRateId")
                    .IsUnique();

                b.ToTable("ProductLaborCosts", "catalog");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ChannelProduct", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Channel", "Channel")
                    .WithMany("ChannelProducts")
                    .HasForeignKey("ChannelId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Product", "Product")
                    .WithMany("ChannelProducts")
                    .HasForeignKey("ProductId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Channel");

                b.Navigation("Product");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductComponent", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Component", "Component")
                    .WithMany("ProductComponents")
                    .HasForeignKey("ComponentId")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired();

                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Product", "Product")
                    .WithMany("ProductComponents")
                    .HasForeignKey("ProductId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Component");

                b.Navigation("Product");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductLaborCost", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.LaborRate", "LaborRate")
                    .WithMany("ProductLaborCosts")
                    .HasForeignKey("LaborRateId")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired();

                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Product", "Product")
                    .WithMany("ProductLaborCosts")
                    .HasForeignKey("ProductId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("LaborRate");

                b.Navigation("Product");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Channel", b =>
            {
                b.Navigation("ChannelProducts");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Component", b =>
            {
                b.Navigation("ProductComponents");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.LaborRate", b =>
            {
                b.Navigation("ProductLaborCosts");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Product", b =>
            {
                b.Navigation("ChannelProducts");

                b.Navigation("ProductComponents");

                b.Navigation("ProductLaborCosts");
            });
#pragma warning restore 612, 618
    }
}
