using Linky.DTOs;
using Linky.Models;

namespace Linky.IRepository
{
    public interface IQRCodeRepository
    {
        Task<QRCode> CreateAsync(QRCodeDto dto);
        Task<QRCode?> GetByIdAsync(Guid id);
        Task<IEnumerable<QRCode>> GetAllAsync();
        //Total QRCode Created
        Task IncrementScanAsync(Guid id, string clientIp, string? country, string? city);
        Task<byte[]> GenerateQrCodeImageAsync(string text, IFormFile? logoFile = null, int pixelsPerModule = 20);
        Task UpdateContentAsync(Guid id, string content);
    }
}
