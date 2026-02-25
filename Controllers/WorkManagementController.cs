using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lib.ServerTiming;
using Swashbuckle.AspNetCore.Annotations;
using static BeatLeader_Server.Utils.WorkTaskResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class WorkManagementController : Controller {
        private readonly AppContext _context;
        private readonly IDbContextFactory<AppContext> _dbFactory;
        private readonly IAmazonS3 _s3Client;
        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public WorkManagementController(
            AppContext context,
            IDbContextFactory<AppContext> dbFactory,
            IConfiguration configuration,
            IServerTiming serverTiming) {
            _context = context;
            _dbFactory = dbFactory;
            _s3Client = configuration.GetS3Client();
            _serverTiming = serverTiming;
            _configuration = configuration;
        }

        #region Helper Methods

        private int CurrentTimestamp => (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

        private async Task<Player?> GetCurrentPlayer() {
            string? currentId = HttpContext.CurrentUserID(_context);
            if (currentId == null) return null;
            return await _context.Players.FindAsync(currentId);
        }

        private bool IsAdmin(Player? player) => player?.Role?.Contains("admin") == true;

        private async Task RecordHistory(int taskId, WorkTaskHistoryAction action, string? playerId, 
            string? fieldName = null, string? oldValue = null, string? newValue = null, 
            string? details = null, int? relatedEntityId = null, string? relatedEntityType = null) {
            
            var history = new WorkTaskHistory {
                WorkTaskId = taskId,
                Action = action,
                PlayerId = playerId,
                Timestamp = CurrentTimestamp,
                FieldName = fieldName,
                OldValue = oldValue,
                NewValue = newValue,
                Details = details,
                RelatedEntityId = relatedEntityId,
                RelatedEntityType = relatedEntityType
            };
            _context.WorkTaskHistory.Add(history);
        }

        #endregion

        #region Status Management

        [HttpGet("~/work/statuses")]
        [SwaggerOperation(Summary = "Get all task statuses", Description = "Retrieves all available task statuses")]
        public async Task<ActionResult<ICollection<WorkTaskStatusResponse>>> GetStatuses() {
            var statuses = await _context.WorkTaskStatuses
                .AsNoTracking()
                .OrderBy(s => s.Order)
                .Select(s => new WorkTaskStatusResponse {
                    Id = s.Id,
                    Title = s.Title,
                    Description = s.Description,
                    Icon = s.Icon,
                    Color = s.Color,
                    Order = s.Order,
                    IsClosedStatus = s.IsClosedStatus,
                    IsDefault = s.IsDefault
                })
                .ToListAsync();
            
            return Ok(statuses);
        }

        [HttpPost("~/work/status")]
        [Authorize]
        [SwaggerOperation(Summary = "Create a new status", Description = "Creates a new task status (admin only)")]
        public async Task<ActionResult<WorkTaskStatusResponse>> CreateStatus(
            [FromQuery] string title,
            [FromQuery] string? description = null,
            [FromQuery] string? icon = null,
            [FromQuery] string? color = null,
            [FromQuery] int order = 0,
            [FromQuery] bool isClosedStatus = false,
            [FromQuery] bool isDefault = false) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can create statuses");
            }

            // If setting as default, unset other defaults
            if (isDefault) {
                var existingDefault = await _context.WorkTaskStatuses.FirstOrDefaultAsync(s => s.IsDefault);
                if (existingDefault != null) {
                    existingDefault.IsDefault = false;
                }
            }

            var status = new WorkTaskStatus {
                Title = title,
                Description = description,
                Icon = icon,
                Color = color,
                Order = order,
                IsClosedStatus = isClosedStatus,
                IsDefault = isDefault,
                CreatedAt = CurrentTimestamp,
                CreatedById = currentPlayer!.Id
            };

            _context.WorkTaskStatuses.Add(status);
            await _context.SaveChangesAsync();

            return Ok(MapStatus(status));
        }

        [HttpPut("~/work/status/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Update a status", Description = "Updates an existing task status (admin only)")]
        public async Task<ActionResult<WorkTaskStatusResponse>> UpdateStatus(
            int id,
            [FromQuery] string? title = null,
            [FromQuery] string? description = null,
            [FromQuery] string? icon = null,
            [FromQuery] string? color = null,
            [FromQuery] int? order = null,
            [FromQuery] bool? isClosedStatus = null,
            [FromQuery] bool? isDefault = null) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can update statuses");
            }

            var status = await _context.WorkTaskStatuses.FindAsync(id);
            if (status == null) return NotFound("Status not found");

            if (title != null) status.Title = title;
            if (description != null) status.Description = description;
            if (icon != null) status.Icon = icon;
            if (color != null) status.Color = color;
            if (order != null) status.Order = order.Value;
            if (isClosedStatus != null) status.IsClosedStatus = isClosedStatus.Value;
            
            if (isDefault == true) {
                var existingDefault = await _context.WorkTaskStatuses.FirstOrDefaultAsync(s => s.IsDefault && s.Id != id);
                if (existingDefault != null) {
                    existingDefault.IsDefault = false;
                }
                status.IsDefault = true;
            } else if (isDefault == false) {
                status.IsDefault = false;
            }

            await _context.SaveChangesAsync();
            return Ok(MapStatus(status));
        }

        [HttpDelete("~/work/status/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Delete a status", Description = "Deletes a task status (admin only)")]
        public async Task<ActionResult> DeleteStatus(int id) {
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can delete statuses");
            }

            var status = await _context.WorkTaskStatuses.FindAsync(id);
            if (status == null) return NotFound("Status not found");

            // Check if any tasks use this status
            var tasksWithStatus = await _context.WorkTasks.AnyAsync(t => t.StatusId == id);
            if (tasksWithStatus) {
                return BadRequest("Cannot delete status that is in use by tasks");
            }

            _context.WorkTaskStatuses.Remove(status);
            await _context.SaveChangesAsync();
            return Ok();
        }

        #endregion

        #region Tag Management

        [HttpGet("~/work/tags")]
        [SwaggerOperation(Summary = "Get all tags", Description = "Retrieves all available task tags")]
        public async Task<ActionResult<ICollection<WorkTaskTagResponse>>> GetTags() {
            var tags = await _context.WorkTaskTags
                .AsNoTracking()
                .OrderBy(t => t.Title)
                .Select(t => new WorkTaskTagResponse {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    Icon = t.Icon,
                    Link = t.Link,
                    Color = t.Color,
                    AdminOnly = t.AdminOnly
                })
                .ToListAsync();
            
            return Ok(tags);
        }

        [HttpPost("~/work/tag")]
        [Authorize]
        [SwaggerOperation(Summary = "Create a new tag", Description = "Creates a new task tag (admin only)")]
        public async Task<ActionResult<WorkTaskTagResponse>> CreateTag(
            [FromQuery] string title,
            [FromQuery] string? description = null,
            [FromQuery] string? icon = null,
            [FromQuery] string? link = null,
            [FromQuery] string? color = null,
            [FromQuery] bool adminOnly = false) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can create tags");
            }

            var tag = new WorkTaskTag {
                Title = title,
                Description = description,
                Icon = icon,
                Link = link,
                Color = color,
                AdminOnly = adminOnly,
                CreatedAt = CurrentTimestamp,
                CreatedById = currentPlayer!.Id
            };

            _context.WorkTaskTags.Add(tag);
            await _context.SaveChangesAsync();

            return Ok(MapTag(tag));
        }

        [HttpPut("~/work/tag/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Update a tag", Description = "Updates an existing task tag (admin only)")]
        public async Task<ActionResult<WorkTaskTagResponse>> UpdateTag(
            int id,
            [FromQuery] string? title = null,
            [FromQuery] string? description = null,
            [FromQuery] string? icon = null,
            [FromQuery] string? link = null,
            [FromQuery] string? color = null,
            [FromQuery] bool? adminOnly = null) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can update tags");
            }

            var tag = await _context.WorkTaskTags.FindAsync(id);
            if (tag == null) return NotFound("Tag not found");

            if (title != null) tag.Title = title;
            if (description != null) tag.Description = description;
            if (icon != null) tag.Icon = icon;
            if (link != null) tag.Link = link;
            if (color != null) tag.Color = color;
            if (adminOnly != null) tag.AdminOnly = adminOnly.Value;

            await _context.SaveChangesAsync();
            return Ok(MapTag(tag));
        }

        [HttpDelete("~/work/tag/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Delete a tag", Description = "Deletes a task tag (admin only)")]
        public async Task<ActionResult> DeleteTag(int id) {
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can delete tags");
            }

            var tag = await _context.WorkTaskTags.FindAsync(id);
            if (tag == null) return NotFound("Tag not found");

            // Remove all tag assignments first
            var assignments = await _context.WorkTaskTagAssignments.Where(a => a.TagId == id).ToListAsync();
            _context.WorkTaskTagAssignments.RemoveRange(assignments);

            _context.WorkTaskTags.Remove(tag);
            await _context.SaveChangesAsync();
            return Ok();
        }

        #endregion

        #region Task List and Details

        [HttpGet("~/work/tasks")]
        [SwaggerOperation(Summary = "Get list of tasks", Description = "Retrieves a paginated list of work tasks with filtering options")]
        public async Task<ActionResult<ResponseWithMetadata<WorkTaskListResponse>>> GetTasks(
            [FromQuery] int page = 1,
            [FromQuery] int count = 20,
            [FromQuery] WorkTaskType? type = null,
            [FromQuery] int? statusId = null,
            [FromQuery] int? tagId = null,
            [FromQuery] string? assigneeId = null,
            [FromQuery] string? creatorId = null,
            [FromQuery] string? search = null,
            [FromQuery] bool includeArchived = false,
            [FromQuery] string sortBy = "createdAt",
            [FromQuery] string order = "desc") {
            
            var currentPlayer = await GetCurrentPlayer();
            var isAdmin = IsAdmin(currentPlayer);

            var query = _context.WorkTasks
                .AsNoTracking()
                .Include(t => t.Creator)
                .Include(t => t.Status)
                .Include(t => t.TagAssignments)!.ThenInclude(ta => ta.Tag)
                .Include(t => t.Assignments)!.ThenInclude(a => a.Player)
                .Include(t => t.Attachments)
                .AsQueryable();

            // Filter out non-public tasks for non-admins
            if (!isAdmin) {
                query = query.Where(t => t.IsPublic);
            }

            // Apply filters
            if (type != null) {
                query = query.Where(t => t.Type == type);
            }

            if (statusId != null) {
                query = query.Where(t => t.StatusId == statusId);
            }

            if (tagId != null) {
                query = query.Where(t => t.TagAssignments!.Any(ta => ta.TagId == tagId));
            }

            if (assigneeId != null) {
                query = query.Where(t => t.Assignments!.Any(a => a.PlayerId == assigneeId));
            }

            if (creatorId != null) {
                query = query.Where(t => t.CreatorId == creatorId);
            }

            if (!includeArchived) {
                query = query.Where(t => !t.IsArchived);
            }

            if (!string.IsNullOrEmpty(search)) {
                var lowSearch = search.ToLower();
                query = query.Where(t => t.Title.ToLower().Contains(lowSearch) || 
                                         (t.Description != null && t.Description.ToLower().Contains(lowSearch)));
            }

            // Apply sorting
            query = (sortBy.ToLower(), order.ToLower()) switch {
                ("createdat", "asc") => query.OrderBy(t => t.CreatedAt),
                ("createdat", _) => query.OrderByDescending(t => t.CreatedAt),
                ("votescore", "asc") => query.OrderBy(t => t.VoteScore),
                ("votescore", _) => query.OrderByDescending(t => t.VoteScore),
                ("commentcount", "asc") => query.OrderBy(t => t.CommentCount),
                ("commentcount", _) => query.OrderByDescending(t => t.CommentCount),
                ("priority", "asc") => query.OrderBy(t => t.Priority),
                ("priority", _) => query.OrderByDescending(t => t.Priority),
                ("title", "asc") => query.OrderBy(t => t.Title),
                ("title", _) => query.OrderByDescending(t => t.Title),
                _ => query.OrderByDescending(t => t.CreatedAt)
            };

            var total = await query.CountAsync();

            // Get current user's votes for these tasks
            var taskIds = await query.Skip((page - 1) * count).Take(count).Select(t => t.Id).ToListAsync();
            var userVotes = currentPlayer != null 
                ? await _context.WorkTaskVotes
                    .Where(v => taskIds.Contains(v.WorkTaskId) && v.PlayerId == currentPlayer.Id && !v.IsRemoved)
                    .ToDictionaryAsync(v => v.WorkTaskId, v => v.Value)
                : new Dictionary<int, int>();

            var tasks = await query
                .Skip((page - 1) * count)
                .Take(count)
                .Select(t => new WorkTaskListResponse {
                    Id = t.Id,
                    Type = t.Type,
                    Title = t.Title,
                    Description = t.Description != null && t.Description.Length > 200 
                        ? t.Description.Substring(0, 200) + "..." 
                        : t.Description,
                    Creator = MapPlayer(t.Creator),
                    CreatedAt = t.CreatedAt,
                    LastEditedAt = t.LastEditedAt,
                    Status = MapStatus(t.Status),
                    Priority = t.Priority,
                    VoteScore = t.VoteScore,
                    CommentCount = t.CommentCount,
                    IsPublic = t.IsPublic,
                    IsArchived = t.IsArchived,
                    IsLocked = t.IsLocked,
                    DueDate = t.DueDate,
                    ResolvedAt = t.ResolvedAt,
                    Tags = t.TagAssignments!.Select(ta => MapTag(ta.Tag)!).ToList(),
                    Assignees = t.Assignments!.Select(a => MapPlayer(a.Player)!).ToList(),
                    AttachmentCount = t.Attachments != null ? t.Attachments.Count : 0
                })
                .ToListAsync();

            // Add current user votes
            foreach (var task in tasks) {
                task.CurrentUserVote = userVotes.TryGetValue(task.Id, out var vote) ? vote : null;
            }

            return new ResponseWithMetadata<WorkTaskListResponse> {
                Metadata = new Metadata {
                    Page = page,
                    ItemsPerPage = count,
                    Total = total
                },
                Data = tasks
            };
        }

        [HttpGet("~/work/task/{id}")]
        [SwaggerOperation(Summary = "Get task details", Description = "Retrieves full details of a specific task")]
        public async Task<ActionResult<WorkTaskDetailResponse>> GetTask(int id) {
            var currentPlayer = await GetCurrentPlayer();
            var isAdmin = IsAdmin(currentPlayer);

            var task = await _context.WorkTasks
                .AsNoTracking()
                .Include(t => t.Creator)
                .Include(t => t.LastEditedBy)
                .Include(t => t.ResolvedBy)
                .Include(t => t.Status)
                .Include(t => t.TagAssignments)!.ThenInclude(ta => ta.Tag)
                .Include(t => t.TagAssignments)!.ThenInclude(ta => ta.AddedBy)
                .Include(t => t.Attachments)!.ThenInclude(a => a.UploadedBy)
                .Include(t => t.Assignments)!.ThenInclude(a => a.Player)
                .Include(t => t.Assignments)!.ThenInclude(a => a.AssignedBy)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound("Task not found");

            if (!task.IsPublic && !isAdmin) {
                return Unauthorized("This task is not public");
            }

            int? currentUserVote = null;
            if (currentPlayer != null) {
                var vote = await _context.WorkTaskVotes
                    .FirstOrDefaultAsync(v => v.WorkTaskId == id && v.PlayerId == currentPlayer.Id && !v.IsRemoved);
                currentUserVote = vote?.Value;
            }

            // Return type-specific response
            if (task is BugReport bugReport) {
                return Ok(new BugReportResponse {
                    Id = bugReport.Id,
                    Type = bugReport.Type,
                    Title = bugReport.Title,
                    Description = bugReport.Description,
                    Creator = MapPlayer(bugReport.Creator),
                    CreatedAt = bugReport.CreatedAt,
                    LastEditedAt = bugReport.LastEditedAt,
                    LastEditedBy = MapPlayer(bugReport.LastEditedBy),
                    Status = MapStatus(bugReport.Status),
                    Priority = bugReport.Priority,
                    VoteScore = bugReport.VoteScore,
                    CommentCount = bugReport.CommentCount,
                    IsPublic = bugReport.IsPublic,
                    IsArchived = bugReport.IsArchived,
                    IsLocked = bugReport.IsLocked,
                    DueDate = bugReport.DueDate,
                    ResolvedAt = bugReport.ResolvedAt,
                    ResolvedBy = MapPlayer(bugReport.ResolvedBy),
                    CurrentUserVote = currentUserVote,
                    TagAssignments = bugReport.TagAssignments?.Select(ta => new WorkTaskTagAssignmentResponse {
                        Id = ta.Id,
                        Tag = MapTag(ta.Tag),
                        AddedBy = MapPlayer(ta.AddedBy),
                        AddedAt = ta.AddedAt
                    }).ToList(),
                    Attachments = bugReport.Attachments?.Select(a => new WorkTaskAttachmentResponse {
                        Id = a.Id,
                        Type = a.Type,
                        FileName = a.FileName,
                        Url = a.Url,
                        MimeType = a.MimeType,
                        FileSize = a.FileSize,
                        UploadedBy = MapPlayer(a.UploadedBy),
                        UploadedAt = a.UploadedAt
                    }).ToList(),
                    Assignments = bugReport.Assignments?.Select(a => new WorkTaskAssignmentResponse {
                        Id = a.Id,
                        Player = MapPlayer(a.Player),
                        AssignedBy = MapPlayer(a.AssignedBy),
                        AssignedAt = a.AssignedAt,
                        Role = a.Role,
                        IsPrimary = a.IsPrimary
                    }).ToList(),
                    Severity = bugReport.Severity,
                    StepsToReproduce = bugReport.StepsToReproduce,
                    ExpectedBehavior = bugReport.ExpectedBehavior,
                    ActualBehavior = bugReport.ActualBehavior,
                    Version = bugReport.Version,
                    Platform = bugReport.Platform,
                    IsReproducible = bugReport.IsReproducible,
                    LogContent = bugReport.LogContent
                });
            }

            if (task is Suggestion suggestion) {
                return Ok(new SuggestionResponse {
                    Id = suggestion.Id,
                    Type = suggestion.Type,
                    Title = suggestion.Title,
                    Description = suggestion.Description,
                    Creator = MapPlayer(suggestion.Creator),
                    CreatedAt = suggestion.CreatedAt,
                    LastEditedAt = suggestion.LastEditedAt,
                    LastEditedBy = MapPlayer(suggestion.LastEditedBy),
                    Status = MapStatus(suggestion.Status),
                    Priority = suggestion.Priority,
                    VoteScore = suggestion.VoteScore,
                    CommentCount = suggestion.CommentCount,
                    IsPublic = suggestion.IsPublic,
                    IsArchived = suggestion.IsArchived,
                    IsLocked = suggestion.IsLocked,
                    DueDate = suggestion.DueDate,
                    ResolvedAt = suggestion.ResolvedAt,
                    ResolvedBy = MapPlayer(suggestion.ResolvedBy),
                    CurrentUserVote = currentUserVote,
                    TagAssignments = suggestion.TagAssignments?.Select(ta => new WorkTaskTagAssignmentResponse {
                        Id = ta.Id,
                        Tag = MapTag(ta.Tag),
                        AddedBy = MapPlayer(ta.AddedBy),
                        AddedAt = ta.AddedAt
                    }).ToList(),
                    Attachments = suggestion.Attachments?.Select(a => new WorkTaskAttachmentResponse {
                        Id = a.Id,
                        Type = a.Type,
                        FileName = a.FileName,
                        Url = a.Url,
                        MimeType = a.MimeType,
                        FileSize = a.FileSize,
                        UploadedBy = MapPlayer(a.UploadedBy),
                        UploadedAt = a.UploadedAt
                    }).ToList(),
                    Assignments = suggestion.Assignments?.Select(a => new WorkTaskAssignmentResponse {
                        Id = a.Id,
                        Player = MapPlayer(a.Player),
                        AssignedBy = MapPlayer(a.AssignedBy),
                        AssignedAt = a.AssignedAt,
                        Role = a.Role,
                        IsPrimary = a.IsPrimary
                    }).ToList(),
                    Category = suggestion.Category,
                    UseCase = suggestion.UseCase,
                    ProposedSolution = suggestion.ProposedSolution,
                    Alternatives = suggestion.Alternatives,
                    Impact = suggestion.Impact,
                    TargetArea = suggestion.TargetArea
                });
            }

            if (task is HelpRequest helpRequest) {
                return Ok(new HelpRequestResponse {
                    Id = helpRequest.Id,
                    Type = helpRequest.Type,
                    Title = helpRequest.Title,
                    Description = helpRequest.Description,
                    Creator = MapPlayer(helpRequest.Creator),
                    CreatedAt = helpRequest.CreatedAt,
                    LastEditedAt = helpRequest.LastEditedAt,
                    LastEditedBy = MapPlayer(helpRequest.LastEditedBy),
                    Status = MapStatus(helpRequest.Status),
                    Priority = helpRequest.Priority,
                    VoteScore = helpRequest.VoteScore,
                    CommentCount = helpRequest.CommentCount,
                    IsPublic = helpRequest.IsPublic,
                    IsArchived = helpRequest.IsArchived,
                    IsLocked = helpRequest.IsLocked,
                    DueDate = helpRequest.DueDate,
                    ResolvedAt = helpRequest.ResolvedAt,
                    ResolvedBy = MapPlayer(helpRequest.ResolvedBy),
                    CurrentUserVote = currentUserVote,
                    TagAssignments = helpRequest.TagAssignments?.Select(ta => new WorkTaskTagAssignmentResponse {
                        Id = ta.Id,
                        Tag = MapTag(ta.Tag),
                        AddedBy = MapPlayer(ta.AddedBy),
                        AddedAt = ta.AddedAt
                    }).ToList(),
                    Attachments = helpRequest.Attachments?.Select(a => new WorkTaskAttachmentResponse {
                        Id = a.Id,
                        Type = a.Type,
                        FileName = a.FileName,
                        Url = a.Url,
                        MimeType = a.MimeType,
                        FileSize = a.FileSize,
                        UploadedBy = MapPlayer(a.UploadedBy),
                        UploadedAt = a.UploadedAt
                    }).ToList(),
                    Assignments = helpRequest.Assignments?.Select(a => new WorkTaskAssignmentResponse {
                        Id = a.Id,
                        Player = MapPlayer(a.Player),
                        AssignedBy = MapPlayer(a.AssignedBy),
                        AssignedAt = a.AssignedAt,
                        Role = a.Role,
                        IsPrimary = a.IsPrimary
                    }).ToList(),
                    Category = helpRequest.Category,
                    Urgency = helpRequest.Urgency,
                    AlreadyTried = helpRequest.AlreadyTried,
                    ErrorMessage = helpRequest.ErrorMessage,
                    Version = helpRequest.Version,
                    Platform = helpRequest.Platform,
                    Resolution = helpRequest.Resolution,
                    IsResolved = helpRequest.IsResolved,
                    UserResolvedAt = helpRequest.UserResolvedAt
                });
            }

            // Generic task response
            return Ok(new WorkTaskDetailResponse {
                Id = task.Id,
                Type = task.Type,
                Title = task.Title,
                Description = task.Description,
                Creator = MapPlayer(task.Creator),
                CreatedAt = task.CreatedAt,
                LastEditedAt = task.LastEditedAt,
                LastEditedBy = MapPlayer(task.LastEditedBy),
                Status = MapStatus(task.Status),
                Priority = task.Priority,
                VoteScore = task.VoteScore,
                CommentCount = task.CommentCount,
                IsPublic = task.IsPublic,
                IsArchived = task.IsArchived,
                IsLocked = task.IsLocked,
                DueDate = task.DueDate,
                ResolvedAt = task.ResolvedAt,
                ResolvedBy = MapPlayer(task.ResolvedBy),
                CurrentUserVote = currentUserVote,
                TagAssignments = task.TagAssignments?.Select(ta => new WorkTaskTagAssignmentResponse {
                    Id = ta.Id,
                    Tag = MapTag(ta.Tag),
                    AddedBy = MapPlayer(ta.AddedBy),
                    AddedAt = ta.AddedAt
                }).ToList(),
                Attachments = task.Attachments?.Select(a => new WorkTaskAttachmentResponse {
                    Id = a.Id,
                    Type = a.Type,
                    FileName = a.FileName,
                    Url = a.Url,
                    MimeType = a.MimeType,
                    FileSize = a.FileSize,
                    UploadedBy = MapPlayer(a.UploadedBy),
                    UploadedAt = a.UploadedAt
                }).ToList(),
                Assignments = task.Assignments?.Select(a => new WorkTaskAssignmentResponse {
                    Id = a.Id,
                    Player = MapPlayer(a.Player),
                    AssignedBy = MapPlayer(a.AssignedBy),
                    AssignedAt = a.AssignedAt,
                    Role = a.Role,
                    IsPrimary = a.IsPrimary
                }).ToList()
            });
        }

        #endregion

        #region Task CRUD

        [HttpPost("~/work/task")]
        [Authorize]
        [SwaggerOperation(Summary = "Create a generic task", Description = "Creates a new generic work task")]
        public async Task<ActionResult<WorkTaskDetailResponse>> CreateTask(
            [FromQuery] string title,
            [FromQuery] string? description = null,
            [FromQuery] int? statusId = null,
            [FromQuery] int priority = 3,
            [FromQuery] bool isPublic = true) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var status = statusId != null 
                ? await _context.WorkTaskStatuses.FindAsync(statusId) 
                : await _context.WorkTaskStatuses.FirstOrDefaultAsync(s => s.IsDefault);

            var task = new WorkTask {
                Type = WorkTaskType.Task,
                Title = title,
                Description = description,
                CreatorId = currentPlayer.Id,
                CreatedAt = CurrentTimestamp,
                StatusId = status?.Id,
                Priority = priority,
                IsPublic = isPublic
            };

            _context.WorkTasks.Add(task);
            await _context.SaveChangesAsync();

            await RecordHistory(task.Id, WorkTaskHistoryAction.Created, currentPlayer.Id);
            await _context.SaveChangesAsync();

            return await GetTask(task.Id);
        }

        [HttpPost("~/work/bugreport")]
        [Authorize]
        [SwaggerOperation(Summary = "Create a bug report", Description = "Creates a new bug report task")]
        public async Task<ActionResult<BugReportResponse>> CreateBugReport(
            [FromQuery] string title,
            [FromQuery] string? description = null,
            [FromQuery] BugSeverity severity = BugSeverity.Medium,
            [FromQuery] string? stepsToReproduce = null,
            [FromQuery] string? expectedBehavior = null,
            [FromQuery] string? actualBehavior = null,
            [FromQuery] string? version = null,
            [FromQuery] string? platform = null,
            [FromQuery] bool? isReproducible = null,
            [FromQuery] int? statusId = null,
            [FromQuery] int priority = 3,
            [FromQuery] bool isPublic = true) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var status = statusId != null 
                ? await _context.WorkTaskStatuses.FindAsync(statusId) 
                : await _context.WorkTaskStatuses.FirstOrDefaultAsync(s => s.IsDefault);

            var bugReport = new BugReport {
                Title = title,
                Description = description,
                CreatorId = currentPlayer.Id,
                CreatedAt = CurrentTimestamp,
                StatusId = status?.Id,
                Priority = priority,
                IsPublic = isPublic,
                Severity = severity,
                StepsToReproduce = stepsToReproduce,
                ExpectedBehavior = expectedBehavior,
                ActualBehavior = actualBehavior,
                Version = version,
                Platform = platform,
                IsReproducible = isReproducible
            };

            _context.BugReports.Add(bugReport);
            await _context.SaveChangesAsync();

            await RecordHistory(bugReport.Id, WorkTaskHistoryAction.Created, currentPlayer.Id);
            await _context.SaveChangesAsync();

            return new BugReportResponse {
                Id = bugReport.Id,
                Type = bugReport.Type,
                Title = bugReport.Title,
                Description = bugReport.Description,
                Creator = MapPlayer(bugReport.Creator),
                CreatedAt = bugReport.CreatedAt,
                LastEditedAt = bugReport.LastEditedAt,
                LastEditedBy = MapPlayer(bugReport.LastEditedBy),
                Status = MapStatus(bugReport.Status),
                Priority = bugReport.Priority,
                VoteScore = bugReport.VoteScore,
                CommentCount = bugReport.CommentCount,
                IsPublic = bugReport.IsPublic,
                IsArchived = bugReport.IsArchived,
                IsLocked = bugReport.IsLocked,
                DueDate = bugReport.DueDate,
                ResolvedAt = bugReport.ResolvedAt,
                ResolvedBy = MapPlayer(bugReport.ResolvedBy),
                CurrentUserVote = 0,
                TagAssignments = bugReport.TagAssignments?.Select(ta => new WorkTaskTagAssignmentResponse {
                    Id = ta.Id,
                    Tag = MapTag(ta.Tag),
                    AddedBy = MapPlayer(ta.AddedBy),
                    AddedAt = ta.AddedAt
                }).ToList(),
                Attachments = bugReport.Attachments?.Select(a => new WorkTaskAttachmentResponse {
                    Id = a.Id,
                    Type = a.Type,
                    FileName = a.FileName,
                    Url = a.Url,
                    MimeType = a.MimeType,
                    FileSize = a.FileSize,
                    UploadedBy = MapPlayer(a.UploadedBy),
                    UploadedAt = a.UploadedAt
                }).ToList(),
                Assignments = bugReport.Assignments?.Select(a => new WorkTaskAssignmentResponse {
                    Id = a.Id,
                    Player = MapPlayer(a.Player),
                    AssignedBy = MapPlayer(a.AssignedBy),
                    AssignedAt = a.AssignedAt,
                    Role = a.Role,
                    IsPrimary = a.IsPrimary
                }).ToList(),
                Severity = bugReport.Severity,
                StepsToReproduce = bugReport.StepsToReproduce,
                ExpectedBehavior = bugReport.ExpectedBehavior,
                ActualBehavior = bugReport.ActualBehavior,
                Version = bugReport.Version,
                Platform = bugReport.Platform,
                IsReproducible = bugReport.IsReproducible,
                LogContent = bugReport.LogContent
            };
        }

        [HttpPost("~/work/suggestion")]
        [Authorize]
        [SwaggerOperation(Summary = "Create a suggestion", Description = "Creates a new suggestion/feature request")]
        public async Task<ActionResult<SuggestionResponse>> CreateSuggestion(
            [FromQuery] string title,
            [FromQuery] string? description = null,
            [FromQuery] SuggestionCategory category = SuggestionCategory.Feature,
            [FromQuery] string? useCase = null,
            [FromQuery] string? proposedSolution = null,
            [FromQuery] string? alternatives = null,
            [FromQuery] string? impact = null,
            [FromQuery] string? targetArea = null,
            [FromQuery] int? statusId = null,
            [FromQuery] int priority = 3,
            [FromQuery] bool isPublic = true) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var status = statusId != null 
                ? await _context.WorkTaskStatuses.FindAsync(statusId) 
                : await _context.WorkTaskStatuses.FirstOrDefaultAsync(s => s.IsDefault);

            var suggestion = new Suggestion {
                Title = title,
                Description = description,
                CreatorId = currentPlayer.Id,
                CreatedAt = CurrentTimestamp,
                StatusId = status?.Id,
                Priority = priority,
                IsPublic = isPublic,
                Category = category,
                UseCase = useCase,
                ProposedSolution = proposedSolution,
                Alternatives = alternatives,
                Impact = impact,
                TargetArea = targetArea
            };

            _context.Suggestions.Add(suggestion);
            await _context.SaveChangesAsync();

            await RecordHistory(suggestion.Id, WorkTaskHistoryAction.Created, currentPlayer.Id);
            await _context.SaveChangesAsync();

            return new SuggestionResponse {
                Id = suggestion.Id,
                Type = suggestion.Type,
                Title = suggestion.Title,
                Description = suggestion.Description,
                Creator = MapPlayer(suggestion.Creator),
                CreatedAt = suggestion.CreatedAt,
                LastEditedAt = suggestion.LastEditedAt,
                LastEditedBy = MapPlayer(suggestion.LastEditedBy),
                Status = MapStatus(suggestion.Status),
                Priority = suggestion.Priority,
                VoteScore = suggestion.VoteScore,
                CommentCount = suggestion.CommentCount,
                IsPublic = suggestion.IsPublic,
                IsArchived = suggestion.IsArchived,
                IsLocked = suggestion.IsLocked,
                DueDate = suggestion.DueDate,
                ResolvedAt = suggestion.ResolvedAt,
                ResolvedBy = MapPlayer(suggestion.ResolvedBy),
                CurrentUserVote = 0,
                TagAssignments = suggestion.TagAssignments?.Select(ta => new WorkTaskTagAssignmentResponse {
                    Id = ta.Id,
                    Tag = MapTag(ta.Tag),
                    AddedBy = MapPlayer(ta.AddedBy),
                    AddedAt = ta.AddedAt
                }).ToList(),
                Attachments = suggestion.Attachments?.Select(a => new WorkTaskAttachmentResponse {
                    Id = a.Id,
                    Type = a.Type,
                    FileName = a.FileName,
                    Url = a.Url,
                    MimeType = a.MimeType,
                    FileSize = a.FileSize,
                    UploadedBy = MapPlayer(a.UploadedBy),
                    UploadedAt = a.UploadedAt
                }).ToList(),
                Assignments = suggestion.Assignments?.Select(a => new WorkTaskAssignmentResponse {
                    Id = a.Id,
                    Player = MapPlayer(a.Player),
                    AssignedBy = MapPlayer(a.AssignedBy),
                    AssignedAt = a.AssignedAt,
                    Role = a.Role,
                    IsPrimary = a.IsPrimary
                }).ToList(),
                Category = suggestion.Category,
                UseCase = suggestion.UseCase,
                ProposedSolution = suggestion.ProposedSolution,
                Alternatives = suggestion.Alternatives,
                Impact = suggestion.Impact,
                TargetArea = suggestion.TargetArea
            };
        }

        [HttpPost("~/work/helprequest")]
        [Authorize]
        [SwaggerOperation(Summary = "Create a help request", Description = "Creates a new help request")]
        public async Task<ActionResult<HelpRequestResponse>> CreateHelpRequest(
            [FromQuery] string title,
            [FromQuery] string? description = null,
            [FromQuery] HelpCategory category = HelpCategory.Other,
            [FromQuery] HelpUrgency urgency = HelpUrgency.Normal,
            [FromQuery] string? alreadyTried = null,
            [FromQuery] string? errorMessage = null,
            [FromQuery] string? version = null,
            [FromQuery] string? platform = null,
            [FromQuery] int? statusId = null,
            [FromQuery] int priority = 3,
            [FromQuery] bool isPublic = true) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var status = statusId != null 
                ? await _context.WorkTaskStatuses.FindAsync(statusId) 
                : await _context.WorkTaskStatuses.FirstOrDefaultAsync(s => s.IsDefault);

            var helpRequest = new HelpRequest {
                Title = title,
                Description = description,
                CreatorId = currentPlayer.Id,
                CreatedAt = CurrentTimestamp,
                StatusId = status?.Id,
                Priority = priority,
                IsPublic = isPublic,
                Category = category,
                Urgency = urgency,
                AlreadyTried = alreadyTried,
                ErrorMessage = errorMessage,
                Version = version,
                Platform = platform
            };

            _context.HelpRequests.Add(helpRequest);
            await _context.SaveChangesAsync();

            await RecordHistory(helpRequest.Id, WorkTaskHistoryAction.Created, currentPlayer.Id);
            await _context.SaveChangesAsync();

            return new HelpRequestResponse {
                Id = helpRequest.Id,
                Type = helpRequest.Type,
                Title = helpRequest.Title,
                Description = helpRequest.Description,
                Creator = MapPlayer(helpRequest.Creator),
                CreatedAt = helpRequest.CreatedAt,
                LastEditedAt = helpRequest.LastEditedAt,
                LastEditedBy = MapPlayer(helpRequest.LastEditedBy),
                Status = MapStatus(helpRequest.Status),
                Priority = helpRequest.Priority,
                VoteScore = helpRequest.VoteScore,
                CommentCount = helpRequest.CommentCount,
                IsPublic = helpRequest.IsPublic,
                IsArchived = helpRequest.IsArchived,
                IsLocked = helpRequest.IsLocked,
                DueDate = helpRequest.DueDate,
                ResolvedAt = helpRequest.ResolvedAt,
                ResolvedBy = MapPlayer(helpRequest.ResolvedBy),
                CurrentUserVote = 0,
                TagAssignments = helpRequest.TagAssignments?.Select(ta => new WorkTaskTagAssignmentResponse {
                    Id = ta.Id,
                    Tag = MapTag(ta.Tag),
                    AddedBy = MapPlayer(ta.AddedBy),
                    AddedAt = ta.AddedAt
                }).ToList(),
                Attachments = helpRequest.Attachments?.Select(a => new WorkTaskAttachmentResponse {
                    Id = a.Id,
                    Type = a.Type,
                    FileName = a.FileName,
                    Url = a.Url,
                    MimeType = a.MimeType,
                    FileSize = a.FileSize,
                    UploadedBy = MapPlayer(a.UploadedBy),
                    UploadedAt = a.UploadedAt
                }).ToList(),
                Assignments = helpRequest.Assignments?.Select(a => new WorkTaskAssignmentResponse {
                    Id = a.Id,
                    Player = MapPlayer(a.Player),
                    AssignedBy = MapPlayer(a.AssignedBy),
                    AssignedAt = a.AssignedAt,
                    Role = a.Role,
                    IsPrimary = a.IsPrimary
                }).ToList(),
                Category = helpRequest.Category,
                Urgency = helpRequest.Urgency,
                AlreadyTried = helpRequest.AlreadyTried,
                ErrorMessage = helpRequest.ErrorMessage,
                Version = helpRequest.Version,
                Platform = helpRequest.Platform,
                Resolution = helpRequest.Resolution,
                IsResolved = helpRequest.IsResolved,
                UserResolvedAt = helpRequest.UserResolvedAt
            };
        }

        [HttpPut("~/work/task/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Update a task", Description = "Updates an existing task (creator or admin only)")]
        public async Task<ActionResult<WorkTaskDetailResponse>> UpdateTask(
            int id,
            [FromQuery] string? title = null,
            [FromQuery] string? description = null,
            [FromQuery] int? statusId = null,
            [FromQuery] int? priority = null,
            [FromQuery] bool? isPublic = null,
            [FromQuery] bool? isArchived = null,
            [FromQuery] bool? isLocked = null,
            [FromQuery] int? dueDate = null) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound("Task not found");

            var isAdmin = IsAdmin(currentPlayer);
            var isCreator = task.CreatorId == currentPlayer.Id;

            if (!isAdmin && !isCreator) {
                return Unauthorized("Only the creator or admins can edit this task");
            }

            // Track changes for history
            if (title != null && title != task.Title) {
                await RecordHistory(id, WorkTaskHistoryAction.TitleChanged, currentPlayer.Id, 
                    "Title", task.Title, title);
                task.Title = title;
            }

            if (description != null && description != task.Description) {
                await RecordHistory(id, WorkTaskHistoryAction.DescriptionChanged, currentPlayer.Id, 
                    "Description", task.Description?.Length > 100 ? task.Description.Substring(0, 100) + "..." : task.Description, 
                    description.Length > 100 ? description.Substring(0, 100) + "..." : description);
                task.Description = description;
            }

            if (statusId != null && statusId != task.StatusId) {
                var oldStatus = task.StatusId != null ? await _context.WorkTaskStatuses.FindAsync(task.StatusId) : null;
                var newStatus = await _context.WorkTaskStatuses.FindAsync(statusId);
                if (newStatus != null) {
                    await RecordHistory(id, WorkTaskHistoryAction.StatusChanged, currentPlayer.Id, 
                        "Status", oldStatus?.Title, newStatus.Title);
                    task.StatusId = statusId;

                    // Handle resolved status
                    if (newStatus.IsClosedStatus && task.ResolvedAt == null) {
                        task.ResolvedAt = CurrentTimestamp;
                        task.ResolvedById = currentPlayer.Id;
                        await RecordHistory(id, WorkTaskHistoryAction.Resolved, currentPlayer.Id);
                    } else if (!newStatus.IsClosedStatus && task.ResolvedAt != null) {
                        task.ResolvedAt = null;
                        task.ResolvedById = null;
                        await RecordHistory(id, WorkTaskHistoryAction.Reopened, currentPlayer.Id);
                    }
                }
            }

            if (priority != null && priority != task.Priority) {
                await RecordHistory(id, WorkTaskHistoryAction.PriorityChanged, currentPlayer.Id, 
                    "Priority", task.Priority.ToString(), priority.ToString());
                task.Priority = priority.Value;
            }

            // Admin-only changes
            if (isAdmin) {
                if (isPublic != null && isPublic != task.IsPublic) {
                    await RecordHistory(id, WorkTaskHistoryAction.VisibilityChanged, currentPlayer.Id, 
                        "IsPublic", task.IsPublic.ToString(), isPublic.ToString());
                    task.IsPublic = isPublic.Value;
                }

                if (isArchived != null && isArchived != task.IsArchived) {
                    await RecordHistory(id, isArchived.Value ? WorkTaskHistoryAction.TaskArchived : WorkTaskHistoryAction.TaskUnarchived, 
                        currentPlayer.Id);
                    task.IsArchived = isArchived.Value;
                }

                if (isLocked != null && isLocked != task.IsLocked) {
                    await RecordHistory(id, isLocked.Value ? WorkTaskHistoryAction.TaskLocked : WorkTaskHistoryAction.TaskUnlocked, 
                        currentPlayer.Id);
                    task.IsLocked = isLocked.Value;
                }

                if (dueDate != null && dueDate != task.DueDate) {
                    await RecordHistory(id, WorkTaskHistoryAction.DueDateChanged, currentPlayer.Id, 
                        "DueDate", task.DueDate?.ToString(), dueDate.ToString());
                    task.DueDate = dueDate;
                }
            }

            task.LastEditedAt = CurrentTimestamp;
            task.LastEditedById = currentPlayer.Id;

            await _context.SaveChangesAsync();
            return await GetTask(id);
        }

        [HttpDelete("~/work/task/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Delete a task", Description = "Deletes a task (admin only)")]
        public async Task<ActionResult> DeleteTask(int id) {
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can delete tasks");
            }

            var task = await _context.WorkTasks
                .Include(t => t.Comments)
                .Include(t => t.Votes)
                .Include(t => t.TagAssignments)
                .Include(t => t.Attachments)
                .Include(t => t.Assignments)
                .Include(t => t.History)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound("Task not found");

            // Remove all related entities
            if (task.Comments != null) _context.WorkTaskComments.RemoveRange(task.Comments);
            if (task.Votes != null) _context.WorkTaskVotes.RemoveRange(task.Votes);
            if (task.TagAssignments != null) _context.WorkTaskTagAssignments.RemoveRange(task.TagAssignments);
            if (task.Attachments != null) _context.WorkTaskAttachments.RemoveRange(task.Attachments);
            if (task.Assignments != null) _context.WorkTaskAssignments.RemoveRange(task.Assignments);
            if (task.History != null) _context.WorkTaskHistory.RemoveRange(task.History);

            _context.WorkTasks.Remove(task);
            await _context.SaveChangesAsync();

            return Ok();
        }

        #endregion

        #region Voting

        [HttpPost("~/work/task/{id}/vote")]
        [Authorize]
        [SwaggerOperation(Summary = "Vote on a task", Description = "Upvote (+1) or downvote (-1) a task")]
        public async Task<ActionResult> VoteOnTask(int id, [FromQuery] int value) {
            if (value != 1 && value != -1) {
                return BadRequest("Vote value must be +1 or -1");
            }

            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound("Task not found");

            if (task.IsLocked && !IsAdmin(currentPlayer)) {
                return BadRequest("This task is locked");
            }

            var existingVote = await _context.WorkTaskVotes
                .FirstOrDefaultAsync(v => v.WorkTaskId == id && v.PlayerId == currentPlayer.Id);

            if (existingVote != null) {
                if (existingVote.IsRemoved) {
                    return BadRequest("Your vote was removed by an admin");
                }

                // Update existing vote
                var oldValue = existingVote.Value;
                if (oldValue == value) {
                    // Remove vote
                    task.VoteScore -= oldValue;
                    _context.WorkTaskVotes.Remove(existingVote);
                } else {
                    // Change vote
                    task.VoteScore = task.VoteScore - oldValue + value;
                    existingVote.Value = value;
                    existingVote.VotedAt = CurrentTimestamp;
                }
            } else {
                // Create new vote
                var vote = new WorkTaskVote {
                    WorkTaskId = id,
                    PlayerId = currentPlayer.Id,
                    Value = value,
                    VotedAt = CurrentTimestamp
                };
                _context.WorkTaskVotes.Add(vote);
                task.VoteScore += value;

                await RecordHistory(id, WorkTaskHistoryAction.VoteAdded, currentPlayer.Id, 
                    details: value > 0 ? "Upvote" : "Downvote");
            }

            await _context.SaveChangesAsync();
            return Ok(new { VoteScore = task.VoteScore, CurrentUserVote = value });
        }

        [HttpDelete("~/work/task/{taskId}/vote/{playerId}")]
        [Authorize]
        [SwaggerOperation(Summary = "Remove a vote (admin)", Description = "Removes a player's vote from a task (admin only)")]
        public async Task<ActionResult> RemoveVote(int taskId, string playerId, [FromQuery] string? reason = null) {
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can remove votes");
            }

            var vote = await _context.WorkTaskVotes
                .FirstOrDefaultAsync(v => v.WorkTaskId == taskId && v.PlayerId == playerId && !v.IsRemoved);

            if (vote == null) return NotFound("Vote not found");

            var task = await _context.WorkTasks.FindAsync(taskId);
            if (task != null) {
                task.VoteScore -= vote.Value;
            }

            vote.IsRemoved = true;
            vote.RemovedById = currentPlayer!.Id;
            vote.RemovedAt = CurrentTimestamp;
            vote.RemovalReason = reason;

            await RecordHistory(taskId, WorkTaskHistoryAction.VoteRemoved, currentPlayer.Id, 
                details: $"Vote by {playerId} removed" + (reason != null ? $": {reason}" : ""));

            await _context.SaveChangesAsync();
            return Ok();
        }

        #endregion

        #region Comments

        [HttpGet("~/work/task/{id}/comments")]
        [SwaggerOperation(Summary = "Get task comments", Description = "Retrieves paginated comments for a task")]
        public async Task<ActionResult<ResponseWithMetadata<WorkTaskCommentResponse>>> GetComments(
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 20,
            [FromQuery] int? parentId = null,
            [FromQuery] string sortBy = "createdAt",
            [FromQuery] string order = "asc") {
            
            var currentPlayer = await GetCurrentPlayer();

            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound("Task not found");

            if (!task.IsPublic && !IsAdmin(currentPlayer)) {
                return Unauthorized("This task is not public");
            }

            var query = _context.WorkTaskComments
                .AsNoTracking()
                .Include(c => c.Author)
                .Include(c => c.Attachments)
                .Include(c => c.Replies)
                .Where(c => c.WorkTaskId == id && c.ParentCommentId == parentId)
                .AsQueryable();

            query = (sortBy.ToLower(), order.ToLower()) switch {
                ("votescore", "desc") => query.OrderByDescending(c => c.VoteScore),
                ("votescore", _) => query.OrderBy(c => c.VoteScore),
                ("createdat", "desc") => query.OrderByDescending(c => c.CreatedAt),
                _ => query.OrderBy(c => c.CreatedAt)
            };

            var total = await query.CountAsync();

            var commentIds = await query.Skip((page - 1) * count).Take(count).Select(c => c.Id).ToListAsync();
            var userVotes = currentPlayer != null 
                ? await _context.WorkTaskCommentVotes
                    .Where(v => commentIds.Contains(v.CommentId) && v.PlayerId == currentPlayer.Id && !v.IsRemoved)
                    .ToDictionaryAsync(v => v.CommentId, v => v.Value)
                : new Dictionary<int, int>();

            var comments = await query
                .Skip((page - 1) * count)
                .Take(count)
                .Select(c => new WorkTaskCommentResponse {
                    Id = c.Id,
                    Content = c.IsDeleted ? "[deleted]" : c.Content,
                    Author = c.IsDeleted ? null : MapPlayer(c.Author),
                    CreatedAt = c.CreatedAt,
                    EditedAt = c.EditedAt,
                    IsEdited = c.IsEdited,
                    VoteScore = c.VoteScore,
                    IsDeleted = c.IsDeleted,
                    ParentCommentId = c.ParentCommentId,
                    ReplyCount = c.Replies != null ? c.Replies.Count : 0,
                    Attachments = c.IsDeleted ? null : c.Attachments!.Select(a => new WorkTaskCommentAttachmentResponse {
                        Id = a.Id,
                        Type = a.Type,
                        FileName = a.FileName,
                        Url = a.Url
                    }).ToList()
                })
                .ToListAsync();

            foreach (var comment in comments) {
                comment.CurrentUserVote = userVotes.TryGetValue(comment.Id, out var vote) ? vote : null;
            }

            return new ResponseWithMetadata<WorkTaskCommentResponse> {
                Metadata = new Metadata {
                    Page = page,
                    ItemsPerPage = count,
                    Total = total
                },
                Data = comments
            };
        }

        [HttpPost("~/work/task/{id}/comment")]
        [Authorize]
        [SwaggerOperation(Summary = "Add a comment", Description = "Adds a comment to a task")]
        public async Task<ActionResult<WorkTaskCommentResponse>> AddComment(
            int id,
            [FromQuery] string content,
            [FromQuery] int? parentCommentId = null) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound("Task not found");

            if (task.IsLocked && !IsAdmin(currentPlayer)) {
                return BadRequest("This task is locked");
            }

            if (parentCommentId != null) {
                var parentComment = await _context.WorkTaskComments.FindAsync(parentCommentId);
                if (parentComment == null || parentComment.WorkTaskId != id) {
                    return BadRequest("Invalid parent comment");
                }
            }

            var comment = new WorkTaskComment {
                WorkTaskId = id,
                Content = content,
                AuthorId = currentPlayer.Id,
                CreatedAt = CurrentTimestamp,
                ParentCommentId = parentCommentId
            };

            _context.WorkTaskComments.Add(comment);
            task.CommentCount++;

            await RecordHistory(id, WorkTaskHistoryAction.CommentAdded, currentPlayer.Id, 
                relatedEntityId: comment.Id, relatedEntityType: "Comment");

            await _context.SaveChangesAsync();

            return Ok(new WorkTaskCommentResponse {
                Id = comment.Id,
                Content = comment.Content,
                Author = MapPlayer(currentPlayer),
                CreatedAt = comment.CreatedAt,
                VoteScore = 0,
                ParentCommentId = parentCommentId,
                ReplyCount = 0
            });
        }

        [HttpPut("~/work/comment/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Edit a comment", Description = "Edits a comment (author or admin only)")]
        public async Task<ActionResult<WorkTaskCommentResponse>> EditComment(int id, [FromQuery] string content) {
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var comment = await _context.WorkTaskComments
                .Include(c => c.Author)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (comment == null) return NotFound("Comment not found");
            if (comment.IsDeleted) return BadRequest("Cannot edit deleted comment");

            var isAdmin = IsAdmin(currentPlayer);
            if (comment.AuthorId != currentPlayer.Id && !isAdmin) {
                return Unauthorized("Only the author or admins can edit this comment");
            }

            await RecordHistory(comment.WorkTaskId, WorkTaskHistoryAction.CommentEdited, currentPlayer.Id, 
                relatedEntityId: id, relatedEntityType: "Comment");

            comment.Content = content;
            comment.EditedAt = CurrentTimestamp;
            comment.IsEdited = true;

            await _context.SaveChangesAsync();

            return Ok(new WorkTaskCommentResponse {
                Id = comment.Id,
                Content = comment.Content,
                Author = MapPlayer(comment.Author),
                CreatedAt = comment.CreatedAt,
                EditedAt = comment.EditedAt,
                IsEdited = true,
                VoteScore = comment.VoteScore,
                ParentCommentId = comment.ParentCommentId
            });
        }

        [HttpDelete("~/work/comment/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Delete a comment", Description = "Deletes a comment (author or admin only)")]
        public async Task<ActionResult> DeleteComment(int id) {
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var comment = await _context.WorkTaskComments.FindAsync(id);
            if (comment == null) return NotFound("Comment not found");

            var isAdmin = IsAdmin(currentPlayer);
            if (comment.AuthorId != currentPlayer.Id && !isAdmin) {
                return Unauthorized("Only the author or admins can delete this comment");
            }

            // Soft delete
            comment.IsDeleted = true;
            comment.DeletedAt = CurrentTimestamp;
            if (isAdmin && comment.AuthorId != currentPlayer.Id) {
                comment.DeletedById = currentPlayer.Id;
            }

            var task = await _context.WorkTasks.FindAsync(comment.WorkTaskId);
            if (task != null) {
                task.CommentCount = Math.Max(0, task.CommentCount - 1);
            }

            await RecordHistory(comment.WorkTaskId, WorkTaskHistoryAction.CommentDeleted, currentPlayer.Id, 
                relatedEntityId: id, relatedEntityType: "Comment");

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("~/work/comment/{id}/vote")]
        [Authorize]
        [SwaggerOperation(Summary = "Vote on a comment", Description = "Upvote (+1) or downvote (-1) a comment")]
        public async Task<ActionResult> VoteOnComment(int id, [FromQuery] int value) {
            if (value != 1 && value != -1) {
                return BadRequest("Vote value must be +1 or -1");
            }

            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var comment = await _context.WorkTaskComments.FindAsync(id);
            if (comment == null) return NotFound("Comment not found");
            if (comment.IsDeleted) return BadRequest("Cannot vote on deleted comment");

            var existingVote = await _context.WorkTaskCommentVotes
                .FirstOrDefaultAsync(v => v.CommentId == id && v.PlayerId == currentPlayer.Id);

            if (existingVote != null) {
                if (existingVote.IsRemoved) {
                    return BadRequest("Your vote was removed by an admin");
                }

                var oldValue = existingVote.Value;
                if (oldValue == value) {
                    // Remove vote
                    comment.VoteScore -= oldValue;
                    _context.WorkTaskCommentVotes.Remove(existingVote);
                    value = 0;
                } else {
                    // Change vote
                    comment.VoteScore = comment.VoteScore - oldValue + value;
                    existingVote.Value = value;
                    existingVote.VotedAt = CurrentTimestamp;
                }
            } else {
                var vote = new WorkTaskCommentVote {
                    CommentId = id,
                    PlayerId = currentPlayer.Id,
                    Value = value,
                    VotedAt = CurrentTimestamp
                };
                _context.WorkTaskCommentVotes.Add(vote);
                comment.VoteScore += value;
            }

            await _context.SaveChangesAsync();
            return Ok(new { VoteScore = comment.VoteScore, CurrentUserVote = value == 0 ? (int?)null : value });
        }

        #endregion

        #region Tags on Tasks

        [HttpPost("~/work/task/{taskId}/tag/{tagId}")]
        [Authorize]
        [SwaggerOperation(Summary = "Add tag to task", Description = "Adds a tag to a task")]
        public async Task<ActionResult> AddTagToTask(int taskId, int tagId) {
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var task = await _context.WorkTasks.FindAsync(taskId);
            if (task == null) return NotFound("Task not found");

            var tag = await _context.WorkTaskTags.FindAsync(tagId);
            if (tag == null) return NotFound("Tag not found");

            if (tag.AdminOnly && !IsAdmin(currentPlayer)) {
                return Unauthorized("This tag can only be added by admins");
            }

            var existingAssignment = await _context.WorkTaskTagAssignments
                .AnyAsync(a => a.WorkTaskId == taskId && a.TagId == tagId);
            if (existingAssignment) {
                return BadRequest("Tag already assigned to this task");
            }

            var assignment = new WorkTaskTagAssignment {
                WorkTaskId = taskId,
                TagId = tagId,
                AddedById = currentPlayer.Id,
                AddedAt = CurrentTimestamp
            };

            _context.WorkTaskTagAssignments.Add(assignment);

            await RecordHistory(taskId, WorkTaskHistoryAction.TagAdded, currentPlayer.Id, 
                details: tag.Title, relatedEntityId: tagId, relatedEntityType: "Tag");

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("~/work/task/{taskId}/tag/{tagId}")]
        [Authorize]
        [SwaggerOperation(Summary = "Remove tag from task", Description = "Removes a tag from a task")]
        public async Task<ActionResult> RemoveTagFromTask(int taskId, int tagId) {
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var assignment = await _context.WorkTaskTagAssignments
                .Include(a => a.Tag)
                .FirstOrDefaultAsync(a => a.WorkTaskId == taskId && a.TagId == tagId);

            if (assignment == null) return NotFound("Tag assignment not found");

            // Allow removal by the person who added it, the task creator, or an admin
            var task = await _context.WorkTasks.FindAsync(taskId);
            var isAdmin = IsAdmin(currentPlayer);
            if (assignment.AddedById != currentPlayer.Id && task?.CreatorId != currentPlayer.Id && !isAdmin) {
                return Unauthorized("You cannot remove this tag");
            }

            await RecordHistory(taskId, WorkTaskHistoryAction.TagRemoved, currentPlayer.Id, 
                details: assignment.Tag?.Title, relatedEntityId: tagId, relatedEntityType: "Tag");

            _context.WorkTaskTagAssignments.Remove(assignment);
            await _context.SaveChangesAsync();
            return Ok();
        }

        #endregion

        #region Assignments

        [HttpPost("~/work/task/{taskId}/assign/{playerId}")]
        [Authorize]
        [SwaggerOperation(Summary = "Assign player to task", Description = "Assigns a player to work on a task (admin only)")]
        public async Task<ActionResult> AssignPlayer(
            int taskId, 
            string playerId,
            [FromQuery] string? role = null,
            [FromQuery] bool isPrimary = false) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can assign players");
            }

            var task = await _context.WorkTasks.FindAsync(taskId);
            if (task == null) return NotFound("Task not found");

            var player = await _context.Players.FindAsync(playerId);
            if (player == null) return NotFound("Player not found");

            var existingAssignment = await _context.WorkTaskAssignments
                .AnyAsync(a => a.WorkTaskId == taskId && a.PlayerId == playerId);
            if (existingAssignment) {
                return BadRequest("Player already assigned to this task");
            }

            // If setting as primary, unset others
            if (isPrimary) {
                var otherPrimary = await _context.WorkTaskAssignments
                    .Where(a => a.WorkTaskId == taskId && a.IsPrimary)
                    .ToListAsync();
                foreach (var a in otherPrimary) {
                    a.IsPrimary = false;
                }
            }

            var assignment = new WorkTaskAssignment {
                WorkTaskId = taskId,
                PlayerId = playerId,
                AssignedById = currentPlayer!.Id,
                AssignedAt = CurrentTimestamp,
                Role = role,
                IsPrimary = isPrimary
            };

            _context.WorkTaskAssignments.Add(assignment);

            await RecordHistory(taskId, WorkTaskHistoryAction.AssigneeAdded, currentPlayer.Id, 
                details: player.Name, relatedEntityId: assignment.Id, relatedEntityType: "Assignment");

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("~/work/task/{taskId}/assign/{playerId}")]
        [Authorize]
        [SwaggerOperation(Summary = "Remove player assignment", Description = "Removes a player from a task (admin only)")]
        public async Task<ActionResult> RemoveAssignment(int taskId, string playerId) {
            var currentPlayer = await GetCurrentPlayer();
            if (!IsAdmin(currentPlayer)) {
                return Unauthorized("Only admins can remove assignments");
            }

            var assignment = await _context.WorkTaskAssignments
                .Include(a => a.Player)
                .FirstOrDefaultAsync(a => a.WorkTaskId == taskId && a.PlayerId == playerId);

            if (assignment == null) return NotFound("Assignment not found");

            await RecordHistory(taskId, WorkTaskHistoryAction.AssigneeRemoved, currentPlayer!.Id, 
                details: assignment.Player?.Name);

            _context.WorkTaskAssignments.Remove(assignment);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("~/work/my-assignments")]
        [Authorize]
        [SwaggerOperation(Summary = "Get my assignments", Description = "Gets all tasks assigned to the current user")]
        public async Task<ActionResult<ResponseWithMetadata<WorkTaskListResponse>>> GetMyAssignments(
            [FromQuery] int page = 1,
            [FromQuery] int count = 20,
            [FromQuery] bool includeCompleted = false) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var query = _context.WorkTaskAssignments
                .AsNoTracking()
                .Include(a => a.WorkTask)!.ThenInclude(t => t!.Creator)
                .Include(a => a.WorkTask)!.ThenInclude(t => t!.Status)
                .Include(a => a.WorkTask)!.ThenInclude(t => t!.TagAssignments)!.ThenInclude(ta => ta.Tag)
                .Where(a => a.PlayerId == currentPlayer.Id)
                .AsQueryable();

            if (!includeCompleted) {
                query = query.Where(a => a.WorkTask!.Status == null || !a.WorkTask.Status.IsClosedStatus);
            }

            var total = await query.CountAsync();

            var assignments = await query
                .OrderByDescending(a => a.WorkTask!.Priority)
                .ThenByDescending(a => a.WorkTask!.CreatedAt)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(a => a.WorkTask)
                .ToListAsync();

            var tasks = assignments.Select(t => new WorkTaskListResponse {
                Id = t!.Id,
                Type = t.Type,
                Title = t.Title,
                Description = t.Description != null && t.Description.Length > 200 
                    ? t.Description.Substring(0, 200) + "..." 
                    : t.Description,
                Creator = MapPlayer(t.Creator),
                CreatedAt = t.CreatedAt,
                Status = MapStatus(t.Status),
                Priority = t.Priority,
                VoteScore = t.VoteScore,
                CommentCount = t.CommentCount,
                IsPublic = t.IsPublic,
                IsArchived = t.IsArchived,
                IsLocked = t.IsLocked,
                DueDate = t.DueDate,
                Tags = t.TagAssignments?.Select(ta => MapTag(ta.Tag)!).ToList()
            }).ToList();

            return new ResponseWithMetadata<WorkTaskListResponse> {
                Metadata = new Metadata {
                    Page = page,
                    ItemsPerPage = count,
                    Total = total
                },
                Data = tasks
            };
        }

        #endregion

        #region History

        [HttpGet("~/work/task/{id}/history")]
        [SwaggerOperation(Summary = "Get task history", Description = "Retrieves the change history for a task")]
        public async Task<ActionResult<ResponseWithMetadata<WorkTaskHistoryResponse>>> GetHistory(
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 50) {
            
            var currentPlayer = await GetCurrentPlayer();

            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound("Task not found");

            if (!task.IsPublic && !IsAdmin(currentPlayer)) {
                return Unauthorized("This task is not public");
            }

            var query = _context.WorkTaskHistory
                .AsNoTracking()
                .Include(h => h.Player)
                .Where(h => h.WorkTaskId == id)
                .OrderByDescending(h => h.Timestamp);

            var total = await query.CountAsync();

            var history = await query
                .Skip((page - 1) * count)
                .Take(count)
                .Select(h => new WorkTaskHistoryResponse {
                    Id = h.Id,
                    Action = h.Action,
                    Player = MapPlayer(h.Player),
                    Timestamp = h.Timestamp,
                    FieldName = h.FieldName,
                    OldValue = h.OldValue,
                    NewValue = h.NewValue,
                    Details = h.Details
                })
                .ToListAsync();

            return new ResponseWithMetadata<WorkTaskHistoryResponse> {
                Metadata = new Metadata {
                    Page = page,
                    ItemsPerPage = count,
                    Total = total
                },
                Data = history
            };
        }

        #endregion

        #region Attachments

        [HttpPost("~/work/task/{id}/attachment")]
        [Authorize]
        [SwaggerOperation(Summary = "Upload attachment", Description = "Uploads a file attachment to a task")]
        public async Task<ActionResult<WorkTaskAttachmentResponse>> UploadAttachment(
            int id,
            [FromQuery] string fileName) {
            
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound("Task not found");

            var isAdmin = IsAdmin(currentPlayer);
            if (task.CreatorId != currentPlayer.Id && !isAdmin) {
                return Unauthorized("Only the creator or admins can add attachments");
            }

            try {
                var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                var (extension, stream) = ImageUtils.GetFormat(ms);
                var uniqueFileName = $"worktask-{id}-{Guid.NewGuid()}{extension}";

                var url = await _s3Client.UploadAsset(uniqueFileName, stream);

                var attachmentType = extension.ToLower() switch {
                    ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => WorkTaskAttachmentType.Image,
                    ".mp4" or ".webm" or ".mov" => WorkTaskAttachmentType.Video,
                    _ => WorkTaskAttachmentType.File
                };

                var attachment = new WorkTaskAttachment {
                    WorkTaskId = id,
                    Type = attachmentType,
                    FileName = fileName,
                    Url = url,
                    MimeType = extension.TrimStart('.'),
                    FileSize = ms.Length,
                    UploadedById = currentPlayer.Id,
                    UploadedAt = CurrentTimestamp
                };

                _context.WorkTaskAttachments.Add(attachment);

                await RecordHistory(id, WorkTaskHistoryAction.AttachmentAdded, currentPlayer.Id, 
                    details: fileName, relatedEntityId: attachment.Id, relatedEntityType: "Attachment");

                await _context.SaveChangesAsync();

                return Ok(new WorkTaskAttachmentResponse {
                    Id = attachment.Id,
                    Type = attachment.Type,
                    FileName = attachment.FileName,
                    Url = attachment.Url,
                    MimeType = attachment.MimeType,
                    FileSize = attachment.FileSize,
                    UploadedBy = MapPlayer(currentPlayer),
                    UploadedAt = attachment.UploadedAt
                });
            } catch (Exception ex) {
                return BadRequest($"Failed to upload attachment: {ex.Message}");
            }
        }

        [HttpDelete("~/work/attachment/{id}")]
        [Authorize]
        [SwaggerOperation(Summary = "Delete attachment", Description = "Deletes an attachment (creator or admin only)")]
        public async Task<ActionResult> DeleteAttachment(int id) {
            var currentPlayer = await GetCurrentPlayer();
            if (currentPlayer == null) return Unauthorized();

            var attachment = await _context.WorkTaskAttachments
                .Include(a => a.WorkTask)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (attachment == null) return NotFound("Attachment not found");

            var isAdmin = IsAdmin(currentPlayer);
            if (attachment.UploadedById != currentPlayer.Id && attachment.WorkTask?.CreatorId != currentPlayer.Id && !isAdmin) {
                return Unauthorized("You cannot delete this attachment");
            }

            await RecordHistory(attachment.WorkTaskId, WorkTaskHistoryAction.AttachmentRemoved, currentPlayer.Id, 
                details: attachment.FileName);

            _context.WorkTaskAttachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return Ok();
        }

        #endregion
    }
}
