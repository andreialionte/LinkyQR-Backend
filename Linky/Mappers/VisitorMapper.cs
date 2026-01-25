using Linky.DTOs;
using Linky.Models;
using Riok.Mapperly.Abstractions;

namespace Linky.Mappers;

[Mapper]
public partial class VisitorMapper
{
    public partial VisitorDto ToDto(Visitor visitor);

    [MapProperty(nameof(VisitorDto.Id), nameof(Visitor.Id), Use = nameof(MapId))]
    public partial Visitor ToModel(VisitorDto dto);

    [UserMapping(Default = false)]
    private Guid MapId(Guid id) => id == Guid.Empty ? Guid.NewGuid() : id;
}
