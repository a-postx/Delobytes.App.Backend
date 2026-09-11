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
    [Migration("20260914000000_Stage9_ConvertUnitToString")]
    partial class Stage9_ConvertUnitToString
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
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

                b.Property<decimal>("TaxAmount")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.HasKey("Id");

                b.HasIndex("TenantId");

                b.HasIndex("ProductChannelInputId");

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

                b.Property<Guid?>("SupplierId")
                    .HasColumnType("uuid");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<string>("Unit")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("SupplierId");

                b.HasIndex("TenantId");

                b.ToTable("PackagingComponents", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Product", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<decimal?>("CostPerUnit")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("Description")
                    .HasMaxLength(2000)
                    .HasColumnType("character varying(2000)");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(300)
                    .HasColumnType("character varying(300)");

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

                b.HasIndex("Sku", "TenantId")
                    .IsUnique();

                b.ToTable("Products", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.ProductChannelInput", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<Guid>("ChannelId")
                    .HasColumnType("uuid");

                b.Property<Guid>("ChannelParameterSetId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<decimal>("LogisticsToMarketplaceCost")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<decimal>("RawMaterialCost")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<decimal>("RetailPrice")
                    .HasPrecision(18, 4)
                    .HasColumnType("numeric(18,4)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("ChannelId");

                b.HasIndex("ChannelParameterSetId");

                b.HasIndex("ProductId");

                b.HasIndex("TenantId");

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

                b.Property<Guid>("ProductId")
                    .HasColumnType("uuid");

                b.Property<decimal>("Quantity")
                    .HasPrecision(18, 6)
                    .HasColumnType("numeric(18,6)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("ComponentId");

                b.HasIndex("ProductId");

                b.HasIndex("TenantId");

                b.HasIndex("ProductId", "ComponentId")
                    .IsUnique();

                b.ToTable("ProductComponents", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.Supplier", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("Description")
                    .HasMaxLength(1000)
                    .HasColumnType("character varying(1000)");

                b.Property<string>("Email")
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<string>("Inn")
                    .IsRequired()
                    .HasMaxLength(12)
                    .HasColumnType("character varying(12)");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<string>("Phone")
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)");

                b.Property<Guid?>("TenantId")
                    .IsRequired()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.HasIndex("TenantId");

                b.HasIndex("Inn", "TenantId")
                    .IsUnique();

                b.ToTable("Suppliers", "catalog");
            });

            modelBuilder.Entity("Delobytes.App.Backend.Catalog.Domain.Entities.SystemChannelTemplate", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<string>("ApiBaseUrl")
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

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

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("IsActive");

                b.ToTable("SystemChannelTemplates", "catalog");
            });
#pragma warning restore 612, 618
        }
    }
}
