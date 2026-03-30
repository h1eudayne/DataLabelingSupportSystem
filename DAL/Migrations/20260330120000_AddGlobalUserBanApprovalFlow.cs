using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace DAL.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260330120000_AddGlobalUserBanApprovalFlow")]
    public partial class AddGlobalUserBanApprovalFlow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionKey",
                table: "AppNotifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "AppNotifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceId",
                table: "AppNotifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "AppNotifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GlobalUserBanRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TargetUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RequestedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ManagerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalUserBanRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlobalUserBanRequests_Users_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GlobalUserBanRequests_Users_RequestedByAdminId",
                        column: x => x.RequestedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GlobalUserBanRequests_Users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalUserBanRequests_ManagerId",
                table: "GlobalUserBanRequests",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalUserBanRequests_RequestedByAdminId",
                table: "GlobalUserBanRequests",
                column: "RequestedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalUserBanRequests_TargetUserId",
                table: "GlobalUserBanRequests",
                column: "TargetUserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalUserBanRequests");

            migrationBuilder.DropColumn(
                name: "ActionKey",
                table: "AppNotifications");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "AppNotifications");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "AppNotifications");

            migrationBuilder.DropColumn(
                name: "ReferenceType",
                table: "AppNotifications");
        }
    }
}
