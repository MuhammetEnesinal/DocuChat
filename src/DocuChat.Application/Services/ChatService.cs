using Mapster;
using DocuChat.Application.Abstractions;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Chat;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;
using DocuChat.Domain.Exceptions;

namespace DocuChat.Application.Services;

public class ChatService : IChatService
{
    private readonly IUnitOfWork _uow;
    private readonly IVectorSearch _vectorSearch;
    private readonly ILlmService _llm;
    private readonly ICurrentUser _currentUser;

    public ChatService(
        IUnitOfWork uow,
        IVectorSearch vectorSearch,
        ILlmService llm,
        ICurrentUser currentUser)
    {
        _uow = uow;
        _vectorSearch = vectorSearch;
        _llm = llm;
        _currentUser = currentUser;
    }

    public async Task<Result<AskResponseDto>> AskAsync(AskRequest req, CancellationToken ct)
    {
        var doc = await _uow.Documents.GetByIdAsync(req.DocumentId, ct);
        if (doc is null)
            return Result<AskResponseDto>.Failure(Error.NotFound("Belge bulunamadı."));

        if (doc.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<AskResponseDto>.Failure(Error.Forbidden("Bu belgeye erişiminiz yok."));

        ChatSession session;

        if (req.SessionId.HasValue)
        {
            session = await _uow.Sessions.GetByIdAsync(req.SessionId.Value, ct)
                ?? throw new SessionNotFoundException(req.SessionId.Value);
        }
        else
        {
            session = new ChatSession
            {
                UserId = _currentUser.UserId,
                DocumentId = req.DocumentId,
                Title = req.Question[..Math.Min(60, req.Question.Length)],
            };
            await _uow.Sessions.AddAsync(session, ct);
        }

        await _uow.Messages.AddAsync(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = req.Question
        }, ct);

        var chunks = await _vectorSearch.SearchAsync(req.DocumentId, req.Question, 5, ct);
        var answer = await _llm.AskAsync(req.Question, chunks, ct);

        await _uow.Messages.AddAsync(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = answer
        }, ct);

        await _uow.SaveChangesAsync(ct);

        return Result<AskResponseDto>.Success(new AskResponseDto(session.Id, answer, chunks));
    }

    public async Task<Result<IReadOnlyList<ChatSessionResponseDto>>> GetMySessionsAsync(
        CancellationToken ct)
    {
        var sessions = await _uow.Sessions.GetByUserIdAsync(_currentUser.UserId, ct);
        var dtos = sessions.Select(s => s.Adapt<ChatSessionResponseDto>()).ToList();
        return Result<IReadOnlyList<ChatSessionResponseDto>>.Success(dtos);
    }

    public async Task<Result<IReadOnlyList<ChatMessageResponseDto>>> GetMessagesAsync(
        Guid sessionId, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetWithMessagesAsync(sessionId, ct);
        if (session is null)
            return Result<IReadOnlyList<ChatMessageResponseDto>>
                .Failure(Error.NotFound("Oturum bulunamadı."));

        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<IReadOnlyList<ChatMessageResponseDto>>
                .Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));

        var dtos = session.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Adapt<ChatMessageResponseDto>())
            .ToList();

        return Result<IReadOnlyList<ChatMessageResponseDto>>.Success(dtos);
    }

    public async Task<Result<bool>> DeleteSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId, ct);
        if (session is null)
            return Result<bool>.Failure(Error.NotFound("Oturum bulunamadı."));

        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<bool>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));

        _uow.Sessions.Delete(session);
        await _uow.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}