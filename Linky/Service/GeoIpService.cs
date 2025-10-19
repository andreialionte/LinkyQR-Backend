using Linky.IService;
using Linky.Utils;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using System.Net;

namespace Linky.Service
{
    public class GeoIpService : IGeoIPService
    {
        private readonly DatabaseReader _reader;
        public GeoIpService(ILogger<GeoIpService> logger)
        {
            _reader = new DatabaseReader("Data/GeoIP/GeoLite2-City.mmdb");

        }

        public GeoLocation GetLocationByIp(string ipAddress)
        {
            if (_reader == null || string.IsNullOrEmpty(ipAddress) || ipAddress.ToLower() == "unknown")
                return new GeoLocation { Country = null, City = null };

            var ip = IPAddress.Parse(ipAddress);

            // Skip local/private addresses
            if (IPAddress.IsLoopback(ip) || IsPrivateIp(ip))
                return new GeoLocation { Country = null, City = null };

            try
            {
                var response = _reader.City(ip);
                return new GeoLocation
                {
                    Country = response.Country?.IsoCode,
                    City = response.City?.Name
                };
            }
            catch (AddressNotFoundException)
            {
                // IP not in database
                return new GeoLocation { Country = null, City = null };
            }
        }

        // Helper to check RFC1918 private ranges
        private bool IsPrivateIp(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();

            if (bytes.Length == 4)
            {
                // IPv4 private ranges
                return
                    (bytes[0] == 10) ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168);
            }

            // Optional: handle IPv6 private ranges if needed
            return false;
        }
    }
}