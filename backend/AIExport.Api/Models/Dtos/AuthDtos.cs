using FluentValidation;

namespace AIExport.Api.Models.Dtos;

// === Auth ===
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAt, UserInfo User);
public record UserInfo(Guid Id, string Username, string Role);

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().Length(3, 50);
        RuleFor(x => x.Password).NotEmpty().Length(6, 100);
    }
}

// === Files ===
public record FileInfoDto(Guid Id, string OriginalName, long FileSize, string FileFormat,
    string ParseStatus, int? RowCount, int? ColumnCount);
public record UploadResponse(Guid BatchId, List<FileInfoDto> Files);

// === Chat ===
public class StartChatRequest { public Guid BatchId { get; set; } public string Strategy { get; set; } = ""; public Guid? TemplateId { get; set; } }
public record StartChatResponse(Guid SessionId, string Mode, string FirstMessage);
public class SendMessageRequest { public Guid SessionId { get; set; } public string Content { get; set; } = ""; public string? Model { get; set; } }
public record ChatMessageDto(Guid Id, string Sender, string Content, DateTime Timestamp, string? Reasoning = null);
public class ConfirmRequest { public Guid SessionId { get; set; } }
public record ConfirmResponse(Guid TaskId, string Message);

// === Reports ===
public record ReportListItem(Guid Id, string OriginalFileName, string ReportStatus,
    string Mode, string ReportType, long? FileSize, DateTime CreatedAt, DateTime ExpiresAt);
public record ReportListResponse(List<ReportListItem> Items, int Total, int Page, int PageSize);

// === Templates ===
public record TemplateDto(Guid Id, string Name, string Strategy, List<string> ColumnNames, DateTime CreatedAt);
public record SaveTemplateRequest(Guid SessionId, string Name);
public record ValidateTemplateResponse(bool Valid, List<string> MissingColumns,
    bool StrategyConflict, string? TemplateStrategy, string? CurrentStrategy);

// === Admin ===
public record CreateUserRequest(string Username, string Password, string Role);
public record UserListItem(Guid Id, string Username, string Role, bool IsActive, DateTime CreatedAt);
