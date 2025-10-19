using Linky.Utils;

namespace Linky.IService
{
    public interface IGeoIPService
    {
        GeoLocation GetLocationByIp(string ipAddress);
    }
}
