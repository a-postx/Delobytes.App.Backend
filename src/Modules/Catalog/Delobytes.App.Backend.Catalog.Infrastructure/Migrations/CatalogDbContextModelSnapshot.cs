using System;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    partial class CatalogDbContextModelSnapshot : ModelSnapshot
    {
        /// <inheritdoc />
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("catalog")
                .HasAnnotation("ProductVersion", "8.0.2")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

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

                b.Property<Guid?>("TenantId")
                    .IsRequired()
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

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ChannelParameterSet", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<decimal>("AcquiringPercent")
                    .HasPrecision(8, 6)
                    .HasColumnType("numeric(8,6)");

                b.Property<Guid>("ChannelId")
                    .HasColumnType("uuid");

                b.Property<decimal>("CommissionPercent")
                    .HasPrecision(8, 6)
                    .HasColumnType("numeric(8,6)");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<bool>("SppEnabled")
                    .HasColumnType("boolean");

                b.Property<decimal>("SppPercent")
                    .HasPrecision(8, 6)
                    .HasColumnType("numeric(8,6)");

                b.Property<decimal>("TaxRatePercent")
                    .HasPrecision(8, 6)
                    .HasColumnType("numeric(8,6)");

                b.Property<int>("TaxType")
                    .HasColumnType("integer");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<DateOnly>("ValidFrom")
                    .HasColumnType("date");

                b.HasKey("Id");

                b.HasIndex("TenantId");

                b.HasIndex("ChannelId", "ValidFrom");

                b.ToTable("ChannelParameterSets", "catalog");
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

                b.Property<Guid?>("TenantId")
                    .IsRequired()
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
                    .HasColumnType("text");

                b.Property<decimal>("PurchasePrice")
                    .HasColumnType("numeric");

                b.Property<string>("Supplier")
                    .HasColumnType("text");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<int>("Unit")
                    .HasColumnType("integer");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("TenantId");

                b.ToTable("Component", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.MarginCalculationSnapshot", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<decimal>("AcquiringAmount")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<decimal>("BuyerPrice")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<DateTimeOffset>("CalculatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<decimal>("CommissionAmount")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<decimal>("LogisticsToMarketplaceCost")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<decimal>("Margin")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<decimal>("MarginPercent")
                    .HasPrecision(8, 6)
                    .HasColumnType("numeric(8,6)");

                b.Property<decimal>("NetRevenue")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<Guid>("ProductChannelInputId")
                    .HasColumnType("uuid");

                b.Property<decimal>("RawMaterialCost")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<decimal>("SppDiscount")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<decimal>("TotalCost")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<decimal>("WorkCost")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.HasKey("Id");

                b.HasIndex("ProductChannelInputId");

                b.HasIndex("TenantId");

                b.ToTable("MarginCalculationSnapshots", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.PackagingComponent", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("Description")
                    .HasMaxLength(1000)
                    .HasColumnType("character varying(1000)");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<decimal>("PricePerUnit")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<string>("Supplier")
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<int>("Unit")
                    .HasColumnType("integer");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("TenantId");

                b.ToTable("PackagingComponents", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Product", b =>
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

                b.Property<string>("Sku")
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
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

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductChannelInput", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<decimal>("AdditionalExpenses")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<decimal>("BuyerPrice")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<Guid>("ChannelId")
                    .HasColumnType("uuid");

                b.Property<Guid?>("ChannelParameterSetId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<decimal>("PackagingCost")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<decimal>("StorageCostPerMonth")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<Guid?>("TariffGridId")
                    .HasColumnType("uuid");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("ChannelId");

                b.HasIndex("ChannelParameterSetId");

                b.HasIndex("TariffGridId");

                b.HasIndex("TenantId");

                b.HasIndex("ProductId", "ChannelId")
                    .IsUnique();

                b.ToTable("ProductChannelInputs", "catalog");
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

                b.Property<decimal>("Quantity")
                    .HasPrecision(10, 3)
                    .HasColumnType("numeric(10,3)");

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.HasKey("Id");

                b.HasIndex("ComponentId");

                b.HasIndex("TenantId");

                b.HasIndex("ProductId", "ComponentId")
                    .IsUnique();

                b.ToTable("ProductComponents", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductPackagingComponent", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<Guid>("PackagingComponentId")
                    .HasColumnType("uuid");

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<decimal>("Quantity")
                    .HasPrecision(10, 3)
                    .HasColumnType("numeric(10,3)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.HasKey("Id");

                b.HasIndex("PackagingComponentId");

                b.HasIndex("TenantId");

                b.HasIndex("ProductId", "PackagingComponentId")
                    .IsUnique();

                b.ToTable("ProductPackagingComponents", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.RawMaterialRate", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<decimal>("CostPerUnit")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<DateOnly>("ValidFrom")
                    .HasColumnType("date");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("TenantId");

                b.HasIndex("ProductId", "ValidFrom");

                b.ToTable("RawMaterialRates", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.TariffGrid", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<Guid?>("ChannelId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<int>("TariffType")
                    .HasColumnType("integer");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<DateOnly>("ValidFrom")
                    .HasColumnType("date");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("TariffType");

                b.HasIndex("TenantId");

                b.HasIndex("ValidFrom");

                b.ToTable("TariffGrids", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.TariffGridEntry", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<decimal>("Rate")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<string>("RegionOrCity")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<Guid>("TariffGridId")
                    .HasColumnType("uuid");

                b.Property<decimal?>("VolumeThresholdLiters")
                    .HasPrecision(10, 3)
                    .HasColumnType("numeric(10,3)");

                b.HasKey("Id");

                b.HasIndex("TenantId");

                b.HasIndex("TariffGridId", "RegionOrCity", "VolumeThresholdLiters")
                    .IsUnique();

                b.ToTable("TariffGridEntries", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.WorkRate", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<int>("AssemblyRatePerDay")
                    .HasColumnType("integer");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<decimal>("DailyWage")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<DateOnly>("ValidFrom")
                    .HasColumnType("date");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("TenantId");

                b.HasIndex("ValidFrom");

                b.ToTable("WorkRates", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ChannelParameterSet", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Channel", "Channel")
                    .WithMany("ChannelParameterSets")
                    .HasForeignKey("ChannelId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Channel");
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

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.MarginCalculationSnapshot", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.ProductChannelInput", "ProductChannelInput")
                    .WithMany("MarginCalculationSnapshots")
                    .HasForeignKey("ProductChannelInputId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("ProductChannelInput");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.PackagingComponent", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Product", null)
                    .WithMany()
                    .HasForeignKey("ProductId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductChannelInput", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Channel", "Channel")
                    .WithMany()
                    .HasForeignKey("ChannelId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.ChannelParameterSet", "ChannelParameterSet")
                    .WithMany()
                    .HasForeignKey("ChannelParameterSetId");

                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Product", "Product")
                    .WithMany("ProductChannelInputs")
                    .HasForeignKey("ProductId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.TariffGrid", "TariffGrid")
                    .WithMany()
                    .HasForeignKey("TariffGridId");

                b.Navigation("Channel");

                b.Navigation("ChannelParameterSet");

                b.Navigation("Product");

                b.Navigation("TariffGrid");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductComponent", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Component", "Component")
                    .WithMany("ProductComponents")
                    .HasForeignKey("ComponentId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Product", "Product")
                    .WithMany("ProductComponents")
                    .HasForeignKey("ProductId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Component");

                b.Navigation("Product");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductPackagingComponent", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.PackagingComponent", "PackagingComponent")
                    .WithMany("ProductPackagingComponents")
                    .HasForeignKey("PackagingComponentId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Product", "Product")
                    .WithMany("ProductPackagingComponents")
                    .HasForeignKey("ProductId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("PackagingComponent");

                b.Navigation("Product");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.RawMaterialRate", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.Product", "Product")
                    .WithMany("RawMaterialRates")
                    .HasForeignKey("ProductId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Product");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.TariffGridEntry", b =>
            {
                b.HasOne("Delobytes.App.Backend.Catalog.Domain.Entities.TariffGrid", "TariffGrid")
                    .WithMany("Entries")
                    .HasForeignKey("TariffGridId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("TariffGrid");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Channel", b =>
            {
                b.Navigation("ChannelParameterSets");

                b.Navigation("ChannelProducts");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Component", b =>
            {
                b.Navigation("ProductComponents");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.PackagingComponent", b =>
            {
                b.Navigation("ProductPackagingComponents");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Product", b =>
            {
                b.Navigation("ChannelProducts");

                b.Navigation("ProductChannelInputs");

                b.Navigation("ProductComponents");

                b.Navigation("ProductPackagingComponents");

                b.Navigation("RawMaterialRates");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductChannelInput", b =>
            {
                b.Navigation("MarginCalculationSnapshots");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.TariffGrid", b =>
            {
                b.Navigation("Entries");
            });
#pragma warning restore 612, 618
        }
    }
}
