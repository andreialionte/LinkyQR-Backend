using Linky.DTOs;
using Linky.Models;

namespace Linky.Mappers
{ // https://vitorafgomes.medium.com/automapper-vs-manual-mapping-in-net-c7b2e81a199c
    public static class VisitorMapper
    {
        public static VisitorDto ToDto(Visitor visitor) =>
            new(
                visitor.Id,
                visitor.Path,
                visitor.Timestamp,
                visitor.Ip,
                visitor.Country,
                visitor.City,
                visitor.Referer,
                visitor.IsUnique,
                visitor.UserAgent,
                visitor.SessionId
            );

        public static Visitor ToModel(VisitorDto dto) =>
            new Visitor
            {
                Id = Guid.NewGuid(),
                Path = dto.Path,
                Timestamp = dto.Timestamp,
                Ip = dto.Ip,
                Country = dto.Country,
                City = dto.City,
                Referer = dto.Referer,
                IsUnique = dto.IsUnique,
                UserAgent = dto.UserAgent
            };
    }
}
