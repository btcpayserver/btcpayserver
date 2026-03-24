using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260322130350_AddPlanChangeTiming")]
    /// <inheritdoc />
    public partial class AddPlanChangeTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "timing",
                table: "subs_plan_changes",
                type: "text",
                nullable: false,
                defaultValue: "Immediate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "timing",
                table: "subs_plan_changes");
        }
    }
}
