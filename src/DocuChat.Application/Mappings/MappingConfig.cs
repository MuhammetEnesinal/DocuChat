// DocuChat.Application/Mappings/MappingConfig.cs
using Mapster;
using DocuChat.Domain.Entities;
using DocuChat.Application.DTOs.Document;
using DocuChat.Application.DTOs.Chat;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Mappings;

public static class MappingConfig
{
    public static void Register()
    {
        // ─── Document → DocumentResponseDto ───────────────────────────────
        // Enum → string dönüşümleri özel mapping gerektirir
        TypeAdapterConfig<Document, DocumentResponseDto>
            .NewConfig()
            .Map(dest => dest.Status, src => src.Status.ToString())
            .Map(dest => dest.FileType, src => src.FileType.ToString());

        // ─── ChatMessage → ChatMessageResponseDto ─────────────────────────
        // Role enum → string
        TypeAdapterConfig<ChatMessage, ChatMessageResponseDto>
            .NewConfig()
            .Map(dest => dest.Role, src => src.Role.ToString());

        // ─── ChatSession → ChatSessionResponseDto ─────────────────────────
        // Explicit mapping for clarity (direct property mapping)
        TypeAdapterConfig<ChatSession, ChatSessionResponseDto>
            .NewConfig()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.Title, src => src.Title)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt);

        // ─── DocumentChunk → DocumentChunkDto ──────────────────────────────
        // Explicit mapping excluding Embedding (should not expose raw vectors to client)
        TypeAdapterConfig<DocumentChunk, DocumentChunkDto>
            .NewConfig()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.ChunkIndex, src => src.ChunkIndex)
            .Map(dest => dest.Content, src => src.Content)
            .Map(dest => dest.ImagePath, src => src.ImagePath);
    }
}