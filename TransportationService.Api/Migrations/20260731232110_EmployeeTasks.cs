using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "task_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employee_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategorySnapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssignedEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiresReview = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresCompletionNote = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresEvidence = table.Column<bool>(type: "boolean", nullable: false),
                    RelatedEntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RelatedEntityId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BlockedReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CompletionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReopenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecurrenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecurrenceDedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OverdueNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueSoonNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_tasks_employees_AssignedEmployeeId",
                        column: x => x.AssignedEmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_tasks_task_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "task_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_recurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Interval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomIntervalDays = table.Column<int>(type: "integer", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastGeneratedPeriod = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_recurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_recurrences_employees_AssignedEmployeeId",
                        column: x => x.AssignedEmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_recurrences_task_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "task_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_template_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DueInDays = table.Column<int>(type: "integer", nullable: true),
                    RequiresReview = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresCompletionNote = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresEvidence = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_template_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_template_items_task_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "task_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_task_template_items_task_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "task_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_attachments_employee_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "employee_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_tasks_AssignedEmployeeId",
                table: "employee_tasks",
                column: "AssignedEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_tasks_CategoryId",
                table: "employee_tasks",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_tasks_TenantId_AssignedEmployeeId_Status",
                table: "employee_tasks",
                columns: new[] { "TenantId", "AssignedEmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_tasks_TenantId_CreatedByUserId",
                table: "employee_tasks",
                columns: new[] { "TenantId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_tasks_TenantId_DueAt",
                table: "employee_tasks",
                columns: new[] { "TenantId", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_tasks_TenantId_RecurrenceDedupeKey",
                table: "employee_tasks",
                columns: new[] { "TenantId", "RecurrenceDedupeKey" },
                unique: true,
                filter: "\"RecurrenceDedupeKey\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_employee_tasks_TenantId_RelatedEntityType_RelatedEntityId",
                table: "employee_tasks",
                columns: new[] { "TenantId", "RelatedEntityType", "RelatedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_TaskId",
                table: "task_attachments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_TenantId_TaskId",
                table: "task_attachments",
                columns: new[] { "TenantId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_categories_TenantId",
                table: "task_categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_categories_TenantId_Code",
                table: "task_categories",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_task_categories_TenantId_IsActive",
                table: "task_categories",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_task_recurrences_AssignedEmployeeId",
                table: "task_recurrences",
                column: "AssignedEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_task_recurrences_TemplateId",
                table: "task_recurrences",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_task_recurrences_TenantId_IsActive",
                table: "task_recurrences",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_task_template_items_CategoryId",
                table: "task_template_items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_task_template_items_TemplateId",
                table: "task_template_items",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_task_template_items_TenantId_TemplateId",
                table: "task_template_items",
                columns: new[] { "TenantId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_templates_TenantId_IsActive",
                table: "task_templates",
                columns: new[] { "TenantId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_attachments");

            migrationBuilder.DropTable(
                name: "task_recurrences");

            migrationBuilder.DropTable(
                name: "task_template_items");

            migrationBuilder.DropTable(
                name: "employee_tasks");

            migrationBuilder.DropTable(
                name: "task_templates");

            migrationBuilder.DropTable(
                name: "task_categories");
        }
    }
}
