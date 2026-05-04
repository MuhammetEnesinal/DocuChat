// DocuChat.Application/Mappings/MappingConfig.cs
using Mapster;
using DocuChat.Domain.Entities;
using DocuChat.Application.DTOs.Document;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Mappings;

public static class MappingConfig
{
    public static void Register()
    {
        // Enum → string dönüşümleri özel mapping gerektirir
        TypeAdapterConfig<Document, DocumentResponseDto>
            .NewConfig()
            .Map(dest => dest.Status, src => src.Status.ToString())
            .Map(dest => dest.FileType, src => src.FileType.ToString());

        // Role enum → string
        TypeAdapterConfig<ChatMessage, ChatMessageResponseDto>
            .NewConfig()
            .Map(dest => dest.Role, src => src.Role.ToString());

        // ChatSession ve DocumentChunk 
       
        TypeAdapterConfig<ChatSession, ChatSessionResponseDto>.NewConfig();
        TypeAdapterConfig<DocumentChunk, DocumentChunkDto>.NewConfig();
    }
}