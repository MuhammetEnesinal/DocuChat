namespace DocuChat.Application.Interfaces.Services.Ai.Llm;

public interface ITokenCounter
{
    int Count(string text);
}
