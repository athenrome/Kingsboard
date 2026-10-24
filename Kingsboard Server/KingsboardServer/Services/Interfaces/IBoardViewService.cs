using Kingsboard.Shared.Models.Board;
using KingsboardServer.Subscriptions.Board;
using KingsboardServer.Models.Board;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Tracks which section of the board each client is viewing, and sends each one its section
    /// and the changes inside it. The board is too big to send whole, so clients see one section at a time.
    /// </summary>
    public interface IBoardViewService
    {
        /// <summary>
        /// Largest width or depth of a section a client may view at once
        /// </summary>
        int MaxSectionSize { get; }

        /// <summary>
        /// Width and depth of the section a client views when it doesn't ask for a particular one
        /// </summary>
        int DefaultSectionSize { get; }

        /// <summary>
        /// The section a client views when it doesn't ask for a particular one: the board's front-left corner
        /// </summary>
        BoardRegion DefaultRegion { get; }

        /// <summary>
        /// Adds a client viewing <paramref name="region"/>. The subscription's events start with the section
        /// (once a board exists), followed by changes inside it.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The region is smaller than 1 x 1 or bigger than <see cref="MaxSectionSize"/></exception>
        BoardViewSubscription Join(BoardRegion region);

        /// <summary>
        /// Moves a client to a different section; the new section is queued on its subscription.
        /// Returns false if the session isn't joined.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The region is smaller than 1 x 1 or bigger than <see cref="MaxSectionSize"/></exception>
        bool SetRegion(Guid sessionId, BoardRegion region);

        /// <summary>
        /// Removes a client and completes its events. Safe to call more than once.
        /// </summary>
        void Leave(Guid sessionId);

        /// <summary>
        /// Gets a one-off snapshot of a section, or null if no board has been created yet
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The region is smaller than 1 x 1 or bigger than <see cref="MaxSectionSize"/></exception>
        BoardSection? GetSection(BoardRegion region);
    }
}
