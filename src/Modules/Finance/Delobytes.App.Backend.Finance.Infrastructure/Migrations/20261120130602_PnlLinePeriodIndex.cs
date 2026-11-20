using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PnlLinePeriodIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The read queries filter on the period date, which is the sale date or, for a line that
            // never sold, the first day of its financial period. EF Core emits that as a COALESCE
            // over both columns, and the planner can only use an index that stores the same
            // expression: an index on "SaleDate" alone would stay unused. The translation lives in
            // the query, not in a duplicated stored column, so the model does not know about this
            // index; a future migration will not drop it.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_PnlLines_TenantId_Currency_PeriodDate\" " +
                "ON finance.\"PnlLines\" (\"TenantId\", \"Currency\", " +
                "(COALESCE(\"SaleDate\", \"FinancialPeriodFrom\")));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS finance.\"IX_PnlLines_TenantId_Currency_PeriodDate\";");
        }
    }
}
