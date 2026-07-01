namespace DocuChat.Application.DTOs.Document;

public class DocumentChunkResponseDto
{
    public Guid Id { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; }
    // ImagePath JSON kaldırıldı — yeni mimaride ChunkImages join'inden gelir.
    // Frontend belge inceleme sayfasında chunk başına resimleri ayrı endpoint'ten alabilir.
    public IReadOnlyList<string>? ImagePaths { get; set; }
    public int? PageNumber { get; set; }
    public string? Header { get; set; }

    public DocumentChunkResponseDto(
        Guid Id, int ChunkIndex, string Content, IReadOnlyList<string>? ImagePaths,
        int? PageNumber = null, string? Header = null)
    {
        this.Id = Id;
        this.ChunkIndex = ChunkIndex;
        this.Content = Content;
        this.ImagePaths = ImagePaths;
        this.PageNumber = PageNumber;
        this.Header = Header;
    }
}
