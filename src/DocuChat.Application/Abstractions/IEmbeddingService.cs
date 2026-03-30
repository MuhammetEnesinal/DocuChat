namespace DocuChat.Application.Abstractions;

public interface IEmbeddingService
{
   
    /// Metni vektöre dönüştürür (text-embedding-3-small → float[1536]).
    /// pgvector cosine similarity aramasında kullanılır.
    
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}