using System;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations;

[DbContext(typeof(IntegrationsDbContext))]
[Migration("20250201000000_InitialIntegrations")]
partial class InitialIntegrations
{
    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasDefaultSchema("integrations")
            .HasAnnotation("ProductVersion", "8.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.Connection", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<string>("ApiKey")
                    .IsRequired()
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

                b.Property<string>("ApiSecret")
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

                b.Property<Guid>("ChannelId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.Property<DateTimeOffset?>("LastSyncAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<string>("Settings")
                    .HasColumnType("text");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("Id");

                b.HasIndex("ChannelId");

                b.HasIndex("IsActive");

                b.HasIndex("LastSyncAt");

                b.HasIndex("TenantId");

                b.ToTable("Connections", "integrations");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.RawApiResponse", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<string>("Endpoint")
                    .IsRequired()
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

                b.Property<int>("HttpStatusCode")
                    .HasColumnType("integer");

                b.Property<DateTimeOffset?>("ProcessedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<DateTimeOffset>("ReceivedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("RequestPayload")
                    .IsRequired()
                    .HasColumnType("text");

                b.Property<string>("ResponsePayload")
                    .IsRequired()
                    .HasColumnType("text");

                b.Property<Guid>("SyncJobId")
                    .HasColumnType("uuid");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.HasKey("Id");

                b.HasIndex("HttpStatusCode");

                b.HasIndex("ProcessedAt");

                b.HasIndex("ReceivedAt");

                b.HasIndex("SyncJobId");

                b.HasIndex("TenantId");

                b.ToTable("RawApiResponses", "integrations");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.SyncJob", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset?>("CompletedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<Guid>("ConnectionId")
                    .HasColumnType("uuid");

                b.Property<DateTimeOffset>("DateRangeFrom")
                    .HasColumnType("timestamp with time zone");

                b.Property<DateTimeOffset>("DateRangeTo")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("ErrorMessage")
                    .HasMaxLength(2000)
                    .HasColumnType("character varying(2000)");

                b.Property<string>("JobType")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)");

                b.Property<int>("RecordsImported")
                    .HasColumnType("integer");

                b.Property<int>("RecordsProcessed")
                    .HasColumnType("integer");

                b.Property<DateTimeOffset?>("StartedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("Status")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uuid");

                b.HasKey("Id");

                b.HasIndex("ConnectionId");

                b.HasIndex("DateRangeFrom");

                b.HasIndex("JobType");

                b.HasIndex("StartedAt");

                b.HasIndex("Status");

                b.HasIndex("TenantId");

                b.ToTable("SyncJobs", "integrations");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.SystemChannelTemplate", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid");

                b.Property<string>("ApiBaseUrl")
                    .IsRequired()
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

                b.Property<string>("ApiVersion")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)");

                b.Property<string>("Code")
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<DateTimeOffset>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("Description")
                    .HasMaxLength(1000)
                    .HasColumnType("character varying(1000)");

                b.Property<string>("DisplayName")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<bool>("IsActive")
                    .HasColumnType("boolean");

                b.HasKey("Id");

                b.HasIndex("Code")
                    .IsUnique();

                b.HasIndex("IsActive");

                b.ToTable("SystemChannelTemplates", "integrations");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.Connection", b =>
            {
                b.HasOne("Delobytes.App.Backend.Integrations.Domain.Entities.SystemChannelTemplate", "Channel")
                    .WithMany("Connections")
                    .HasForeignKey("ChannelId")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired();

                b.Navigation("Channel");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.RawApiResponse", b =>
            {
                b.HasOne("Delobytes.App.Backend.Integrations.Domain.Entities.SyncJob", "SyncJob")
                    .WithMany("RawApiResponses")
                    .HasForeignKey("SyncJobId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("SyncJob");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.SyncJob", b =>
            {
                b.HasOne("Delobytes.App.Backend.Integrations.Domain.Entities.Connection", "Connection")
                    .WithMany("SyncJobs")
                    .HasForeignKey("ConnectionId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Connection");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.Connection", b =>
            {
                b.Navigation("SyncJobs");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.SyncJob", b =>
            {
                b.Navigation("RawApiResponses");
            });

        modelBuilder.Entity("Delobytes.App.Backend.Integrations.Domain.Entities.SystemChannelTemplate", b =>
            {
                b.Navigation("Connections");
            });
#pragma warning restore 612, 618
    }
}
