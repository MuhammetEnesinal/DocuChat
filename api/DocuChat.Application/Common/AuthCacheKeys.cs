namespace DocuChat.Application.Common;

/// <summary>
/// Auth ile ilgili IMemoryCache anahtarları — API (OnTokenValidated) ve Infrastructure
/// (mutasyon sonrası evict) tek kaynaktan üretsin, drift olmasın.
/// </summary>
public static class AuthCacheKeys
{
    // Kullanıcının (SecurityStamp, ClaimsStamp) çiftini 60 sn cache'ler.
    public static string Stamps(string userId) => $"auth-stamps:{userId}";
}
