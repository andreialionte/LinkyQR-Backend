using Linky.Models;
using Linky.DTOs;
using Riok.Mapperly.Abstractions;

namespace Linky.Mappers;

[Mapper]
public partial class VisitorStatsMapper
{
    [MapperIgnoreSource(nameof(VisitorStats.Id))]
    [MapProperty(nameof(VisitorStats.TopPages), nameof(VisitorStatsDto.TopPages), Use = nameof(CopyDictionary))]
    [MapProperty(nameof(VisitorStats.TopCountries), nameof(VisitorStatsDto.TopCountries), Use = nameof(CopyDictionary))]
    public partial VisitorStatsDto ToDto(VisitorStats stats);

    [MapperIgnoreTarget(nameof(VisitorStats.Id))]
    [MapProperty(nameof(VisitorStatsDto.TopPages), nameof(VisitorStats.TopPages), Use = nameof(CopyDictionary))]
    [MapProperty(nameof(VisitorStatsDto.TopCountries), nameof(VisitorStats.TopCountries), Use = nameof(CopyDictionary))]
    public partial VisitorStats ToModel(VisitorStatsDto dto);

    [UserMapping(Default = false)]
    private Dictionary<string, int> CopyDictionary(Dictionary<string, int> source) => new Dictionary<string, int>(source);

    private Guid CreateNewId() => Guid.NewGuid();
}
