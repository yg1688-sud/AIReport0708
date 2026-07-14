using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIExport.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateCustomRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomRequirements",
                table: "AnalysisTemplates",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomRequirements",
                table: "AnalysisTemplates");
        }
    }
}
