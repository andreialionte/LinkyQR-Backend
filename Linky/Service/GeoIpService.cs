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
        private readonly ILogger<GeoIpService> _logger;

        public GeoIpService(ILogger<GeoIpService> logger)
        {
            _logger = logger;
            _reader = null;

            try
            {
                // Use AppContext.BaseDirectory to get the application root directory
                // This works both in local development and in Docker containers
                var geoIpPath = Path.Combine(AppContext.BaseDirectory, "Data/GeoIP/GeoLite2-City.mmdb");

                _logger.LogInformation("Looking for GeoIP database at: {path}", geoIpPath);

                if (!File.Exists(geoIpPath))
                {
                    _logger.LogWarning("GeoIP database not found at {path}. GeoIP lookups will return null.", geoIpPath);
                    return;
                }

                _reader = new DatabaseReader(geoIpPath);
                _logger.LogInformation("GeoIP database loaded successfully from {path}", geoIpPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load GeoIP database. GeoIP lookups will return null.");
            }
        }

        public GeoLocation GetLocationByIp(string ipAddress)
        {
            // If reader is null, GeoIP is not available
            if (_reader == null || string.IsNullOrEmpty(ipAddress) || ipAddress.ToLower() == "unknown")
                return new GeoLocation { Country = null, City = null };

            try
            {
                var ip = IPAddress.Parse(ipAddress);

                // Skip local/private addresses
                if (IPAddress.IsLoopback(ip) || IsPrivateIp(ip))
                    return new GeoLocation { Country = null, City = null };

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error looking up IP {ip}", ipAddress);
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