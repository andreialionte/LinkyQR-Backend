using Linky.DTOs;
using Linky.Models;

namespace Linky.IRepository
{
    public interface IQRCodeRepository
    {
        Task<QRCode> CreateAsync(QRCodeDto dto, CancellationToken cancellationToken = default);
        Task<QRCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IEnumerable<QRCode>> GetAllAsync(CancellationToken cancellationToken = default);
        //Total QRCode Created
        Task IncrementScanAsync(Guid id, string clientIp, string? country, string? city, CancellationToken cancellationToken = default);
        Task<byte[]> GenerateQrCodeImageAsync(string text, IFormFile? logoFile = null, int pixelsPerModule = 20, CancellationToken cancellationToken = default);
        Task UpdateContentAsync(Guid id, string content, CancellationToken cancellationToken = default);
    }
}
