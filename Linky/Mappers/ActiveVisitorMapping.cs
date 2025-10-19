using Linky.DTOs;
using Linky.Models;

namespace Linky.Mappers
{
    public static class ActiveVisitorMapping
    {
        public static ActiveVisitorDto ToDto(ActiveVisitor model) =>
            new(
                model.SessionId,
                model.Ip,
                model.CurrentPath,
                //model.PathHistory.AsReadOnly(),
                model.LastSeenUtc,
                model.Country,
                model.City,
                model.UserAgent
            );

        public static ActiveVisitor ToModel(ActiveVisitorDto dto) =>
            new ActiveVisitor
            {
                SessionId = dto.SessionId,
                Ip = dto.Ip,
                CurrentPath = dto.CurrentPath,
                //PathHistory = dto.PathHistory.ToList(),
                LastSeenUtc = dto.LastSeenUtc,
                Country = dto.Country,
                City = dto.City,
                UserAgent = dto.UserAgent
            };
    }
}
