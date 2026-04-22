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
        ChatSession session;

        if (req.SessionId.HasValue)
        {
            session = await _uow.Sessions.GetByIdAsync(req.SessionId.Value, ct)
                ?? throw new SessionNotFoundException(req.SessionId.Value);

            if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
                return Result<AskResponseDto>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));
        }
        else
        {
            session = new ChatSession
            {
                UserId = _currentUser.UserId,
                Title = req.Question[..Math.Min(60, req.Question.Length)],
            };
            await _uow.Sessions.AddAsync(session, ct);
            await _uow.SaveChangesAsync(ct);
        }

        await _uow.Messages.AddAsync(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = req.Question
        }, ct);
        await _uow.SaveChangesAsync(ct);

        // Oturum geçmişini çek (son 4 mesaj) — user mesajı kaydedildikten sonra
        var history = new List<(string Role, string Content)>();
        var sessionWithMessages = await _uow.Sessions.GetWithMessagesAsync(session.Id, ct);
        if (sessionWithMessages?.Messages?.Any() == true)
        {
            var allMessages = sessionWithMessages.Messages
                .OrderBy(m => m.CreatedAt)
                .ToList();

            // Son user mesajını çıkar — mevcut soru olarak LLM'e ayrıca gönderiliyor
            history = allMessages
                .Take(allMessages.Count - 1)
                .TakeLast(4)
                .Where(m => !m.Content.StartsWith("AŞAĞIDAKİ BELGE PARÇALARINI"))
                .Select(m => (m.Role == MessageRole.User ? "user" : "assistant", m.Content))
                .ToList();
        }

        // 1. Session'da daha önce hangi belge kullanıldı?
        //    Son assistant mesajındaki dosya adından belge ID'sini bul
        Guid? preferredDocumentId = null;
        if (history.Any())
        {
            var lastAssistant = history.LastOrDefault(h => h.Role == "assistant");
            if (lastAssistant.Content != null)
            {
                var fileMatch = System.Text.RegularExpressions.Regex.Match(
                    lastAssistant.Content,
                    @"[\w\-\+\.]+\.(csv|pdf|xlsx|docx|txt)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fileMatch.Success)
                {
                    var fileName = fileMatch.Value;
                    var fileBaseName = fileName.Split('.')[0];
                    var doc = await _uow.Documents.FindAsync(
                        d => d.FileName == fileName || d.FileName.Contains(fileBaseName),
                        ct);
                    if (doc.Any())
                        preferredDocumentId = doc.First().Id;
                }
            }
        }

        // 2. İlgili chunk'ları bul — önceki belgeden bağlamı koru
        var chunks = await _vectorSearch.SearchAsync(req.Question, ct: ct, preferredDocumentId: preferredDocumentId);

        if (chunks.Count == 0)
        {
            const string noData = "Sisteme yüklenmiş belgeler arasında bu soruyla ilgili bilgi bulunamadı.";
            await _uow.Messages.AddAsync(new ChatMessage
            {
                SessionId = session.Id,
                Role = MessageRole.Assistant,
                Content = noData
            }, ct);
            await _uow.SaveChangesAsync(ct);
            return Result<AskResponseDto>.Success(new AskResponseDto(session.Id, noData, []));
        }

        var answer = await _llm.AskAsync(req.Question, chunks, history, ct);

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


    public async Task<Result<bool>> RenameSessionAsync(
        Guid sessionId, string title, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId, ct);
        if (session is null)
            return Result<bool>.Failure(Error.NotFound("Oturum bulunamadı."));

        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<bool>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));

        session.Title = title[..Math.Min(60, title.Length)];
        await _uow.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }


    public async Task<Result<IReadOnlyList<string>>> GetPopularQuestionsAsync(
        int limit, CancellationToken ct)
    {
        var allMessages = await _uow.Messages.FindAsync(
            m => m.Role == MessageRole.User, ct);

        var popular = allMessages
            .Select(m => m.Content.Trim())
            .Where(q => q.Length > 10 && q.Length < 200)
            .Where(q => !q.StartsWith("AŞAĞIDAKİ BELGE PARÇALARINI"))
            .GroupBy(q => NormalizeQuestion(q))
            .OrderByDescending(g => g.Count())
            .Take(limit)
            .Select(g => g.OrderBy(q => q.Length).First())
            .ToList();

        return Result<IReadOnlyList<string>>.Success(popular);
    }

    // Soruyu normalize et — büyük/küçük harf, noktalama farkı gözetme
    private static string NormalizeQuestion(string question)
    {
        return new string(
            question.ToLowerInvariant()
                    .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                    .ToArray()
        ).Trim();
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