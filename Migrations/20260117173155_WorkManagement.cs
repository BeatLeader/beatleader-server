using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class WorkManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkTaskStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsClosedStatus = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskStatuses_Players_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AdminOnly = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskTags_Players_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatorId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<int>(type: "int", nullable: false),
                    LastEditedAt = table.Column<int>(type: "int", nullable: true),
                    LastEditedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    StatusId = table.Column<int>(type: "int", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    VoteScore = table.Column<int>(type: "int", nullable: false),
                    CommentCount = table.Column<int>(type: "int", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    DueDate = table.Column<int>(type: "int", nullable: true),
                    ResolvedAt = table.Column<int>(type: "int", nullable: true),
                    ResolvedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Discriminator = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: true),
                    StepsToReproduce = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpectedBehavior = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActualBehavior = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BugReport_Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BugReport_Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsReproducible = table.Column<bool>(type: "bit", nullable: true),
                    LogContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HelpRequest_Category = table.Column<int>(type: "int", nullable: true),
                    Urgency = table.Column<int>(type: "int", nullable: true),
                    AlreadyTried = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: true),
                    UserResolvedAt = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: true),
                    UseCase = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedSolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Alternatives = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Impact = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TargetArea = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTasks_Players_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTasks_Players_LastEditedById",
                        column: x => x.LastEditedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTasks_Players_ResolvedById",
                        column: x => x.ResolvedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTasks_WorkTaskStatuses_StatusId",
                        column: x => x.StatusId,
                        principalTable: "WorkTaskStatuses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkTaskId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AssignedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AssignedAt = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskAssignments_Players_AssignedById",
                        column: x => x.AssignedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskAssignments_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkTaskAssignments_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkTaskId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    UploadedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    UploadedAt = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskAttachments_Players_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskAttachments_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkTaskId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<int>(type: "int", nullable: false),
                    EditedAt = table.Column<int>(type: "int", nullable: true),
                    IsEdited = table.Column<bool>(type: "bit", nullable: false),
                    VoteScore = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DeletedAt = table.Column<int>(type: "int", nullable: true),
                    ParentCommentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskComments_Players_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskComments_Players_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskComments_WorkTaskComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "WorkTaskComments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskComments_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkTaskId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedEntityId = table.Column<int>(type: "int", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskHistory_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskHistory_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskTagAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkTaskId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false),
                    AddedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AddedAt = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskTagAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskTagAssignments_Players_AddedById",
                        column: x => x.AddedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskTagAssignments_WorkTaskTags_TagId",
                        column: x => x.TagId,
                        principalTable: "WorkTaskTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkTaskTagAssignments_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkTaskId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    VotedAt = table.Column<int>(type: "int", nullable: false),
                    IsRemoved = table.Column<bool>(type: "bit", nullable: false),
                    RemovedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RemovedAt = table.Column<int>(type: "int", nullable: true),
                    RemovalReason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskVotes_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkTaskVotes_Players_RemovedById",
                        column: x => x.RemovedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskVotes_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskCommentAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedAt = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskCommentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskCommentAttachments_WorkTaskComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "WorkTaskComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskCommentVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    VotedAt = table.Column<int>(type: "int", nullable: false),
                    IsRemoved = table.Column<bool>(type: "bit", nullable: false),
                    RemovedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RemovedAt = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskCommentVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskCommentVotes_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkTaskCommentVotes_Players_RemovedById",
                        column: x => x.RemovedById,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkTaskCommentVotes_WorkTaskComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "WorkTaskComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAssignments_AssignedById",
                table: "WorkTaskAssignments",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAssignments_PlayerId",
                table: "WorkTaskAssignments",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAssignments_WorkTaskId_PlayerId",
                table: "WorkTaskAssignments",
                columns: new[] { "WorkTaskId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAttachments_UploadedById",
                table: "WorkTaskAttachments",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAttachments_WorkTaskId",
                table: "WorkTaskAttachments",
                column: "WorkTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskCommentAttachments_CommentId",
                table: "WorkTaskCommentAttachments",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskComments_AuthorId",
                table: "WorkTaskComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskComments_DeletedById",
                table: "WorkTaskComments",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskComments_ParentCommentId",
                table: "WorkTaskComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskComments_WorkTaskId",
                table: "WorkTaskComments",
                column: "WorkTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskCommentVotes_CommentId_PlayerId",
                table: "WorkTaskCommentVotes",
                columns: new[] { "CommentId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskCommentVotes_PlayerId",
                table: "WorkTaskCommentVotes",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskCommentVotes_RemovedById",
                table: "WorkTaskCommentVotes",
                column: "RemovedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskHistory_PlayerId",
                table: "WorkTaskHistory",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskHistory_Timestamp",
                table: "WorkTaskHistory",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskHistory_WorkTaskId",
                table: "WorkTaskHistory",
                column: "WorkTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_CreatedAt",
                table: "WorkTasks",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_CreatorId",
                table: "WorkTasks",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_LastEditedById",
                table: "WorkTasks",
                column: "LastEditedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_ResolvedById",
                table: "WorkTasks",
                column: "ResolvedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_StatusId",
                table: "WorkTasks",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_Type",
                table: "WorkTasks",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskStatuses_CreatedById",
                table: "WorkTaskStatuses",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskTagAssignments_AddedById",
                table: "WorkTaskTagAssignments",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskTagAssignments_TagId",
                table: "WorkTaskTagAssignments",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskTagAssignments_WorkTaskId",
                table: "WorkTaskTagAssignments",
                column: "WorkTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskTags_CreatedById",
                table: "WorkTaskTags",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskVotes_PlayerId",
                table: "WorkTaskVotes",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskVotes_RemovedById",
                table: "WorkTaskVotes",
                column: "RemovedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskVotes_WorkTaskId_PlayerId",
                table: "WorkTaskVotes",
                columns: new[] { "WorkTaskId", "PlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkTaskAssignments");

            migrationBuilder.DropTable(
                name: "WorkTaskAttachments");

            migrationBuilder.DropTable(
                name: "WorkTaskCommentAttachments");

            migrationBuilder.DropTable(
                name: "WorkTaskCommentVotes");

            migrationBuilder.DropTable(
                name: "WorkTaskHistory");

            migrationBuilder.DropTable(
                name: "WorkTaskTagAssignments");

            migrationBuilder.DropTable(
                name: "WorkTaskVotes");

            migrationBuilder.DropTable(
                name: "WorkTaskComments");

            migrationBuilder.DropTable(
                name: "WorkTaskTags");

            migrationBuilder.DropTable(
                name: "WorkTasks");

            migrationBuilder.DropTable(
                name: "WorkTaskStatuses");
        }
    }
}
