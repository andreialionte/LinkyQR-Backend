//using Linky.IService;
//using NATS.Client.Core;
//using NATS.Client.JetStream;
//using NATS.Client.JetStream.Models;


//// NATS.Net = wrapper simplu(high - level)
//// NATS.Client.Core = (low - level, more stronger)

//public sealed class NatsService : INatsService, IAsyncDisposable
//{
//    private readonly INatsConnection _connection;
//    private readonly INatsJSContext _jetStream;
//    private readonly ILogger<NatsService> _logger;

//    public NatsService(INatsConnection connection, ILogger<NatsService> logger)
//    {
//        _connection = connection;
//        _jetStream = new NatsJSContext(connection);
//        _logger = logger;

//        _logger.LogInformation("NATS Service initialized");
//    }

//    // === CORE NATS ===

//    public async Task PublishAsync<T>(string subject, T data, CancellationToken ct = default)
//    {
//        try
//        {
//            await _connection.PublishAsync(subject, data, cancellationToken: ct);
//            _logger.LogDebug("Published to {Subject}", subject);
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Failed to publish to {Subject}", subject);
//            throw;
//        }
//    }

//    public IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = null, CancellationToken ct = default)
//    {
//        _logger.LogInformation("Subscribing to {Subject}", subject);
//        return _connection.SubscribeAsync<T>(subject, queueGroup: queueGroup, cancellationToken: ct);
//    }

//    // === REQUEST-REPLY ===

//    public async Task<NatsMsg<TReply>?> RequestAsync<TRequest, TReply>(string subject, TRequest data, CancellationToken ct = default)
//    {
//        try
//        {
//            var reply = await _connection.RequestAsync<TRequest, TReply>(subject, data, cancellationToken: ct);
//            _logger.LogDebug("Request-reply completed for {Subject}", subject);
//            return reply;
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Request failed for {Subject}", subject);
//            return null;
//        }
//    }

//    // === JETSTREAM ===

//    public async Task<PubAckResponse> PublishJetStreamAsync<T>(string subject, T data, CancellationToken ct = default)
//    {
//        try
//        {
//            var ack = await _jetStream.PublishAsync(subject, data, cancellationToken: ct);
//            _logger.LogDebug("JetStream published to {Subject}", subject);
//            return ack;
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "JetStream publish failed for {Subject}", subject);
//            throw;
//        }
//    }

//    // === DIRECT ACCESS ===

//    public INatsConnection GetConnection() => _connection;
//    public INatsJSContext GetJetStream() => _jetStream;

//    public async ValueTask DisposeAsync()
//    {
//        await _connection.DisposeAsync();
//    }
//}