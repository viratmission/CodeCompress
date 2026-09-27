using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeCompass.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_RepositoryAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnalyzedAt",
                table: "Repositories",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RepositoryFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    IsDirectory = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryFiles_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryModules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ModuleType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryModules_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryDependencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    SourceFileId = table.Column<int>(type: "int", nullable: false),
                    TargetFileId = table.Column<int>(type: "int", nullable: false),
                    DependencyType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ImportStatement = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryDependencies_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepositoryDependencies_RepositoryFiles_SourceFileId",
                        column: x => x.SourceFileId,
                        principalTable: "RepositoryFiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RepositoryDependencies_RepositoryFiles_TargetFileId",
                        column: x => x.TargetFileId,
                        principalTable: "RepositoryFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryDependencies_RepositoryId",
                table: "RepositoryDependencies",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryDependencies_SourceFileId_TargetFileId",
                table: "RepositoryDependencies",
                columns: new[] { "SourceFileId", "TargetFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryDependencies_TargetFileId",
                table: "RepositoryDependencies",
                column: "TargetFileId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryFiles_RepositoryId_FilePath",
                table: "RepositoryFiles",
                columns: new[] { "RepositoryId", "FilePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryModules_RepositoryId_Path",
                table: "RepositoryModules",
                columns: new[] { "RepositoryId", "Path" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepositoryDependencies");

            migrationBuilder.DropTable(
                name: "RepositoryModules");

            migrationBuilder.DropTable(
                name: "RepositoryFiles");

            migrationBuilder.DropColumn(
                name: "AnalyzedAt",
                table: "Repositories");
        }
    }
}
