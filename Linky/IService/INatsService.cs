//using NATS.Client.Core;
//using NATS.Client.JetStream;
//using NATS.Client.JetStream.Models;

//namespace Linky.IService
//{
//    public interface INatsService
//    {
//        // Core NATS
//        Task PublishAsync<T>(string subject, T data, CancellationToken ct = default);
//        IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = null, CancellationToken ct = default);

//        // Request-Reply
//        Task<NatsMsg<TReply>?> RequestAsync<TRequest, TReply>(string subject, TRequest data, CancellationToken ct = default);

//        // JetStream
//        Task<PubAckResponse> PublishJetStreamAsync<T>(string subject, T data, CancellationToken ct = default);

//        // Direct access
//        INatsConnection GetConnection();
//        INatsJSContext GetJetStream();
//    }
//}
