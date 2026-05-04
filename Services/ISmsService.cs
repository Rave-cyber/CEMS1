namespace CEMS.Services
{
    public interface ISmsService
    {
        bool IsConfigured { get; }
        Task<bool> SendAsync(string toNumber, string message);
    }
}
