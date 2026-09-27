using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeCompass.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_Onboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingPaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingPaths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnboardingPaths_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StarterTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Difficulty = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelatedModuleId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StarterTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StarterTasks_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StarterTasks_RepositoryModules_RelatedModuleId",
                        column: x => x.RelatedModuleId,
                        principalTable: "RepositoryModules",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OnboardingSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OnboardingPathId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    ModuleId = table.Column<int>(type: "int", nullable: true),
                    StepType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnboardingSteps_OnboardingPaths_OnboardingPathId",
                        column: x => x.OnboardingPathId,
                        principalTable: "OnboardingPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OnboardingSteps_RepositoryModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "RepositoryModules",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserOnboardings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    OnboardingPathId = table.Column<int>(type: "int", nullable: false),
                    ProgressPercentage = table.Column<double>(type: "float", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOnboardings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserOnboardings_OnboardingPaths_OnboardingPathId",
                        column: x => x.OnboardingPathId,
                        principalTable: "OnboardingPaths",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserOnboardings_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserOnboardings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserOnboardingSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserOnboardingId = table.Column<int>(type: "int", nullable: false),
                    OnboardingStepId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOnboardingSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserOnboardingSteps_OnboardingSteps_OnboardingStepId",
                        column: x => x.OnboardingStepId,
                        principalTable: "OnboardingSteps",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserOnboardingSteps_UserOnboardings_UserOnboardingId",
                        column: x => x.UserOnboardingId,
                        principalTable: "UserOnboardings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingPaths_RepositoryId_Role",
                table: "OnboardingPaths",
                columns: new[] { "RepositoryId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingSteps_ModuleId",
                table: "OnboardingSteps",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingSteps_OnboardingPathId",
                table: "OnboardingSteps",
                column: "OnboardingPathId");

            migrationBuilder.CreateIndex(
                name: "IX_StarterTasks_RelatedModuleId",
                table: "StarterTasks",
                column: "RelatedModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_StarterTasks_RepositoryId",
                table: "StarterTasks",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOnboardings_OnboardingPathId",
                table: "UserOnboardings",
                column: "OnboardingPathId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOnboardings_RepositoryId",
                table: "UserOnboardings",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOnboardings_UserId",
                table: "UserOnboardings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOnboardingSteps_OnboardingStepId",
                table: "UserOnboardingSteps",
                column: "OnboardingStepId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOnboardingSteps_UserOnboardingId",
                table: "UserOnboardingSteps",
                column: "UserOnboardingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StarterTasks");

            migrationBuilder.DropTable(
                name: "UserOnboardingSteps");

            migrationBuilder.DropTable(
                name: "OnboardingSteps");

            migrationBuilder.DropTable(
                name: "UserOnboardings");

            migrationBuilder.DropTable(
                name: "OnboardingPaths");
        }
    }
}
