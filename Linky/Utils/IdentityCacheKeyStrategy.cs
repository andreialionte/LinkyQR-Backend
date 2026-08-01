using UiPath.Caching;

namespace Linky.Utils;

public sealed class IdentityCacheKeyStrategy : ICacheKeyStrategy
{
    public CacheKey GetCacheKey<T>(CacheKey key) => key;
}