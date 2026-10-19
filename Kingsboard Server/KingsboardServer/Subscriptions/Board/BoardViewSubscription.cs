using System.Threading.Channels;
using KingsboardServer.Events.Board;

namespace KingsboardServer.Subscriptions.Board
{
    /// <summary>
    /// A client's view of the board, returned when they join
    /// </summary>
    /// <param name="SessionId">Secret id the client uses to change its section</param>
    /// <param name="Events">What to send the client: its section, then changes inside it and any new sections. Completes when they leave.</param>
    public record BoardViewSubscription(Guid SessionId, ChannelReader<BoardViewEvent> Events);
}
