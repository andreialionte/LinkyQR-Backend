using Linky.IRepository;
using Microsoft.AspNetCore.SignalR;
using TickerQ.Utilities.Base;

namespace Linky.Jobs
{
    public class ActiveVisitorJob
    {
        private readonly IActiveVisitorRepository _activeVisitorRepo;
        private readonly IHubContext<ActiveVisitorsHub> _hubContext;

        public ActiveVisitorJob(
            IActiveVisitorRepository activeVisitorRepo,
            IHubContext<ActiveVisitorsHub> hubContext)
        {
            _activeVisitorRepo = activeVisitorRepo;
            _hubContext = hubContext;
        }

        [TickerFunction("ActiveVisitorJob")]
        public async Task Execute(TickerFunctionContext context)
        {
            var cancellationToken = context.CancellationToken;

            // Șterge vizitatorii inactivi
            await _activeVisitorRepo.DeleteInactiveVisitors(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);

            // Obține vizitatorii activi
            var visitors = await _activeVisitorRepo.GetActiveVisitors(cancellationToken).ConfigureAwait(false);

            Console.WriteLine("Job just runned " + DateTime.UtcNow);

            // Trimite count-ul către clienți
            await _hubContext.Clients.All.SendAsync("ReceiveVisitorCount", visitors.Count, cancellationToken).ConfigureAwait(false);
        }
    }
}
