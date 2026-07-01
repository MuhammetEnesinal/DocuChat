namespace DocuChat.Application.ServiceContracts;

public class RerankedItem
{
    public int OriginalIndex { get; set; }
    public double Score { get; set; }

    public RerankedItem(int OriginalIndex, double Score)
    {
        this.OriginalIndex = OriginalIndex;
        this.Score = Score;
    }
}
