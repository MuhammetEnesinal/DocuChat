namespace DocuChat.Application.DTOs.Document;

public record DocumentChunkResponseDto(
    Guid Id,
    int ChunkIndex,
    string Content,
    // ImagePath JSON kaldırıldı — yeni mimaride ChunkImages join'inden gelir.
    // Frontend belge inceleme sayfasında chunk başına resimleri ayrı endpoint'ten alabilir.
    IReadOnlyList<string>? ImagePaths);
