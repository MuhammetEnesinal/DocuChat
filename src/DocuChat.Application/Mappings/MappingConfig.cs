using DocuChat.Application.DTOs.Chat;
using DocuChat.Application.DTOs.Document;
using DocuChat.Domain.Entities;
using Mapster;

namespace DocuChat.Application.Mappings;

public static class MappingConfig
{
    public static void Register()
    {
        // Document → DocumentDto
        // ChunkCount: Chunks navigation load edilmişse sayar, yoksa 0 döner
        TypeAdapterConfig<Document, DocumentDto>
            .NewConfig()
            .Map(dest => dest.Status, src => src.Status.ToString())
            .Map(dest => dest.FileType, src => src.FileType.ToString())
            .Map(dest => dest.ChunkCount, src => src.ChunkCount);   // DB'deki sayıyı kullan

        // ChatMessage → ChatMessageDto
        TypeAdapterConfig<ChatMessage, ChatMessageDto>
            .NewConfig()
            .Map(dest => dest.Role, src => src.Role.ToString());

        // ChatSession → ChatSessionDto
        // DocumentName: Document navigation load edilmişse FileName'i alır
        TypeAdapterConfig<ChatSession, ChatSessionDto>
            .NewConfig()
            .Map(dest => dest.DocumentName, src =>
                src.Document == null ? string.Empty : src.Document.FileName);
    }
}