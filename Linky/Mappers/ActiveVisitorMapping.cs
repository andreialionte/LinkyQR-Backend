using Linky.DTOs;
using Linky.Models;
using Riok.Mapperly.Abstractions;

namespace Linky.Mappers;

[Mapper]
public partial class ActiveVisitorMapper
{
    public partial ActiveVisitorDto ToDto(ActiveVisitor model);

    [MapProperty(nameof(ActiveVisitorDto.SessionId), nameof(ActiveVisitor.SessionId), Use = nameof(MapSessionId))]
    public partial ActiveVisitor ToModel(ActiveVisitorDto dto);

    [UserMapping(Default = false)]
    private Guid MapSessionId(Guid sessionId) => sessionId == Guid.Empty ? Guid.NewGuid() : sessionId;
}
