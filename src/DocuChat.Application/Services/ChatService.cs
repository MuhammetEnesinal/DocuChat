using System.Text.Json;
using Mapster;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Chat;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

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
            var foundSession = await _uow.Sessions.GetByIdAsync(req.SessionId.Value, ct);
            if (foundSession is null)
                return Result<AskResponseDto>.Failure(Error.NotFound($"Oturum bulunamadı. Id: {req.SessionId.Value}"));
            session = foundSession;

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

        var history = new List<(string Role, string Content)>();
        var sessionWithMessages = await _uow.Sessions.GetWithMessagesAsync(session.Id, ct);
        if (sessionWithMessages?.Messages?.Any() == true)
        {
            var allMessages = sessionWithMessages.Messages.OrderBy(m => m.CreatedAt).ToList();
            history = allMessages
                .Take(allMessages.Count - 1)
                .TakeLast(10)
                .Where(m => !m.Content.StartsWith("AŞAĞIDAKİ BELGE PARÇALARINI"))
                .Select(m => (m.Role == MessageRole.User ? "user" : "assistant", m.Content))
                .ToList();
        }

        // Belirsiz soru kontrolü — history yoksa zamir içeren kısa sorular reddedilsin
        if (!history.Any() && IsAmbiguousQuestion(req.Question))
        {
            const string ambiguous = "Sorunuz bağlam gerektiriyor. Lütfen neyi kastettiğinizi daha açık ifade edin. Örneğin: 'Bu belgedeki X nedir?' yerine 'Y belgesi hangi konuları içeriyor?' gibi.";
            await _uow.Messages.AddAsync(new ChatMessage
            {
                SessionId = session.Id,
                Role = MessageRole.Assistant,
                Content = ambiguous
            }, ct);
            await _uow.SaveChangesAsync(ct);
            return Result<AskResponseDto>.Success(new AskResponseDto(session.Id, ambiguous, []));
        }

        // Aşama 1: LLM'e history + soru ver, hangi belge/konu olduğunu tespit et
        var allDocs = await _uow.Documents.FindAsync(d => true, ct);
        var docNames = allDocs.Select(d => d.FileName).ToList();

        var relevantDocNames = await _llm.DetectRelevantDocumentsAsync(
            req.Question, history, docNames, ct);

        // Tespit edilen belgelerle vector search yönlendir
        List<Guid>? relevantDocIds = null;
        if (relevantDocNames.Any())
        {
            var matchedDocs = allDocs.Where(d =>
                relevantDocNames.Any(r =>
                    d.FileName.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                    d.FileName.Contains(r.Split('.')[0], StringComparison.OrdinalIgnoreCase)
                )).ToList();
            if (matchedDocs.Any())
                relevantDocIds = matchedDocs.Select(d => d.Id).ToList();
        }

        var chunks = await _vectorSearch.SearchAsync(req.Question, ct: ct, relevantDocumentIds: relevantDocIds);

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



        // Chunk'lardan image path'leri topla — LlmService ile AYNI sırada
        var seenPaths = new HashSet<string>();
        var imagePaths = chunks
            .Where(c => c.ImagePath != null)
            .SelectMany(c =>
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(c.ImagePath!);
                    return parsed ?? new List<string> { c.ImagePath! };
                }
                catch { return new List<string> { c.ImagePath! }; }
            })
            .Where(p => seenPaths.Add(p)) // Distinct ama sırayı koru
            .ToList();

        await _uow.Messages.AddAsync(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = answer,
            ImagesJson = imagePaths.Count > 0 ? JsonSerializer.Serialize(imagePaths) : null,
        }, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<AskResponseDto>.Success(new AskResponseDto(session.Id, answer, chunks));
    }

    public async Task<Result<IReadOnlyList<ChatSessionResponseDto>>> GetMySessionsAsync(CancellationToken ct)
    {
        var sessions = await _uow.Sessions.GetByUserIdAsync(_currentUser.UserId, ct);
        var dtos = sessions.Select(s => s.Adapt<ChatSessionResponseDto>()).ToList();
        return Result<IReadOnlyList<ChatSessionResponseDto>>.Success(dtos);
    }

    private static bool IsAmbiguousQuestion(string question)
    {
        var q = question.Trim().ToLowerInvariant();

        // Çok kısa sorular (3 kelimeden az)
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return true;

        // Sadece zamir + fiil içeren sorular (history gerektiren)
        var ambiguousStarters = new[]
        {
            "bunlar", "bunların", "bunlarda", "bunu", "bunun",
            "şunlar", "şunu", "şunun", "şunların",
            "onlar", "onların", "onu", "onun",
            "bu ne", "bu nedir", "bunlar ne", "bunlar nedir",
            "peki bunlar", "peki bu", "peki şu",
            "ya bunlar", "ya bu", "o ne", "o nedir"
        };

        // Belirsiz zamirle başlayan kısa sorular
        foreach (var starter in ambiguousStarters)
            if (q.StartsWith(starter) && words.Length <= 5)
                return true;

        return false;
    }

    public async Task<Result<IReadOnlyList<ChatMessageResponseDto>>> GetMessagesAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetWithMessagesAsync(sessionId, ct);
        if (session is null)
            return Result<IReadOnlyList<ChatMessageResponseDto>>.Failure(Error.NotFound("Oturum bulunamadı."));

        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<IReadOnlyList<ChatMessageResponseDto>>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));

        var dtos = session.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Adapt<ChatMessageResponseDto>())
            .ToList();

        return Result<IReadOnlyList<ChatMessageResponseDto>>.Success(dtos);
    }

    public async Task<Result<bool>> RenameSessionAsync(Guid sessionId, string title, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId, ct);
        if (session is null) return Result<bool>.Failure(Error.NotFound("Oturum bulunamadı."));
        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<bool>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));
        session.Title = title[..Math.Min(60, title.Length)];
        await _uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<IReadOnlyList<string>>> GetPopularQuestionsAsync(int limit, CancellationToken ct)
    {
        var allMessages = await _uow.Messages.FindAsync(m => m.Role == MessageRole.User, ct);
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

    private static string NormalizeQuestion(string question) =>
        new string(question.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray()).Trim();

    public async Task<Result<bool>> DeleteSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId, ct);
        if (session is null) return Result<bool>.Failure(Error.NotFound("Oturum bulunamadı."));
        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<bool>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));
        _uow.Sessions.Delete(session);
        await _uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}