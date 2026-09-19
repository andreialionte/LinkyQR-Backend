namespace Linky.Config
{
    public class MqttConfig
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 1883;
        public string ClientId { get; set; } = "LinkyBackendService";
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool UseTls { get; set; } = false;
        public int KeepAliveIntervalSeconds { get; set; } = 15;
        public int ReconnectDelaySeconds { get; set; } = 5;
    }
}
