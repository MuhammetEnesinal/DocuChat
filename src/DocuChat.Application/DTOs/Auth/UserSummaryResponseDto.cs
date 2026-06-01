namespace DocuChat.Application.DTOs.Auth;

public record UserSummaryResponseDto(
    string Id,
    string Email,
    string FullName,
    DateTime CreatedAt,
    IEnumerable<string> Roles);
