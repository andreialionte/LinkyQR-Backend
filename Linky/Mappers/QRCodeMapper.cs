using Linky.DTOs;
using Linky.Models;
using Riok.Mapperly.Abstractions;

namespace Linky.Mappers;

[Mapper]
public partial class QRCodeMapper
{
    public QRCode FromDto(QRCodeDto dto)
    {
        return new QRCode
        {
            Id = Guid.NewGuid(),
            Content = dto.Content,
            ExpirationDate = dto.ExpirationDate,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            LastScannedAt = DateTime.MinValue,
            ScanCount = 0
        };
    }

    [MapperIgnoreSource(nameof(QRCode.Id))]
    [MapperIgnoreSource(nameof(QRCode.CreatedAt))]
    [MapperIgnoreSource(nameof(QRCode.LastUsedAt))]
    [MapperIgnoreSource(nameof(QRCode.IsActive))]
    [MapperIgnoreSource(nameof(QRCode.LastScannedAt))]
    [MapperIgnoreSource(nameof(QRCode.ScanCount))]
    [MapperIgnoreSource(nameof(QRCode.LastIp))]
    [MapperIgnoreSource(nameof(QRCode.LastCountry))]
    [MapperIgnoreSource(nameof(QRCode.LastCity))]
    public partial QRCodeDto ToDto(QRCode model);
}
