using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIExport.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomRequirements",
                table: "AnalysisRequirements",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomRequirements",
                table: "AnalysisRequirements");
        }
    }
}
