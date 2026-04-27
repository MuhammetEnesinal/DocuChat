using Mapster;
using DocuChat.Domain.Entities;
using DocuChat.Application.DTOs.Document;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Mappings;

public static class MappingConfig
{
    public static void Register()
    {
        TypeAdapterConfig<Document, DocumentResponseDto>
            .NewConfig()
            .Map(dest => dest.Status, src => src.Status.ToString())
            .Map(dest => dest.FileType, src => src.FileType.ToString());

        TypeAdapterConfig<ChatMessage, ChatMessageResponseDto>
            .NewConfig()
            .Map(dest => dest.Role, src => src.Role.ToString())
            .Map(dest => dest.ImagesJson, src => src.ImagesJson);

        TypeAdapterConfig<ChatSession, ChatSessionResponseDto>
            .NewConfig();
    }
}