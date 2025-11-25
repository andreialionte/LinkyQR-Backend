using Linky.DTOs;
using Linky.Models;

namespace Linky.Mappers
{
    public static class QRCodeMapper
    {
        public static QRCode FromDto(QRCodeDto dto)
        {
            return new QRCode
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                ExpirationDate = dto.ExpirationDate,
                IsActive = true,
                LastScannedAt = DateTime.MinValue,
                ScanCount = 0
            };
        }

        public static QRCodeDto ToDto(QRCode model) =>
            new(
                model.Content ?? string.Empty,
                model.ExpirationDate
            );
    }
}
