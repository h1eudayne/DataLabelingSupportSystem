using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAdvancedBRFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Thêm 3 cột mới vào bảng Projects
            migrationBuilder.AddColumn<string>(name: "GuidelineVersion", table: "Projects", type: "nvarchar(max)", nullable: false, defaultValue: "1.0");
            migrationBuilder.AddColumn<bool>(name: "RequireConsensus", table: "Projects", type: "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "Status", table: "Projects", type: "nvarchar(max)", nullable: false, defaultValue: "Draft");

            // 2. Thêm 2 cột mới vào bảng Assignments
            migrationBuilder.AddColumn<int>(name: "RejectCount", table: "Assignments", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(name: "IsEscalated", table: "Assignments", type: "bit", nullable: false, defaultValue: false);

            // 3. Thêm cột khóa tài khoản vào bảng UserProjectStats
            migrationBuilder.AddColumn<bool>(name: "IsLocked", table: "UserProjectStats", type: "bit", nullable: false, defaultValue: false);

            // 4. Thêm cột duyệt bài vào bảng ReviewLogs
            migrationBuilder.AddColumn<bool>(name: "IsApproved", table: "ReviewLogs", type: "bit", nullable: false, defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lệnh để xóa các cột này nếu ông muốn Rollback
            migrationBuilder.DropColumn(name: "GuidelineVersion", table: "Projects");
            migrationBuilder.DropColumn(name: "RequireConsensus", table: "Projects");
            migrationBuilder.DropColumn(name: "Status", table: "Projects");

            migrationBuilder.DropColumn(name: "RejectCount", table: "Assignments");
            migrationBuilder.DropColumn(name: "IsEscalated", table: "Assignments");

            migrationBuilder.DropColumn(name: "IsLocked", table: "UserProjectStats");

            migrationBuilder.DropColumn(name: "IsApproved", table: "ReviewLogs");
        }
    }
}
