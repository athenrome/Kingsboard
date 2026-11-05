using System;

namespace Kingsboard.Players
{
    /// <summary>
    /// Tracks what the local player is doing right now: whether we're joined to the game, whether we're controlling the
    /// avatar or in the game at the table (<see cref="Mode"/>), and which actions (such as typing in chat) are in progress. Systems report their actions with <see cref="Begin"/> and <see cref="End"/>,
    /// and ask it whether something else should hold back, e.g. <see cref="BlocksCameraMovement"/>.
    /// Owned by <see cref="PlayerController"/> and reached through <see cref="PlayerController.State"/>.
    /// </summary>
    public class PlayerStateTracker
    {
        /// <summary>
        /// The actions that take the keyboard for text, so the game's own keys must be ignored while any is going on
        /// </summary>
        public const PlayerAction TextEntryActions = PlayerAction.TypingInChat;

        /// <summary>
        /// Where we are in joining the game
        /// </summary>
        public PlayerConnectionState Connection { get; private set; } = PlayerConnectionState.Connecting;

        /// <summary>
        /// Every action in progress right now (<see cref="PlayerAction.None"/> when idle)
        /// </summary>
        public PlayerAction Actions { get; private set; } = PlayerAction.None;

        /// <summary>
        /// True while an action that takes the keyboard for text (see <see cref="TextEntryActions"/>) is in progress
        /// </summary>
        public bool IsEnteringText => (Actions & TextEntryActions) != 0;

        /// <summary>
        /// True while the camera's keyboard controls should be ignored, because the keys are going to text entry
        /// </summary>
        public bool BlocksCameraMovement => IsEnteringText;

        /// <summary>
        /// What we're controlling: the avatar in the room (<see cref="PlayerMode.Avatar"/>, the default) or the game at the
        /// table (<see cref="PlayerMode.InGame"/>). Changed with <see cref="SetMode"/>.
        /// </summary>
        public PlayerMode Mode { get; private set; } = PlayerMode.Avatar;

        /// <summary>
        /// True while we're seated at the table interacting with the board (<see cref="PlayerMode.InGame"/>), the only
        /// time the board and our pawns can be used
        /// </summary>
        public bool IsInGame => Mode == PlayerMode.InGame;

        /// <summary>
        /// True while we're walking around the room as the avatar (<see cref="PlayerMode.Avatar"/>)
        /// </summary>
        public bool IsControllingAvatar => Mode == PlayerMode.Avatar;

        /// <summary>
        /// True while the avatar mustn't walk around the room: the keys are going to text entry, or we're in the game
        /// </summary>
        public bool BlocksRoomMovement => IsEnteringText || IsInGame;

        /// <summary>
        /// Raised after <see cref="Connection"/>, <see cref="Mode"/> or <see cref="Actions"/> changes, with the tracker itself
        /// </summary>
        public event Action<PlayerStateTracker> Changed;

        /// <summary>
        /// Raised after <see cref="Mode"/> changes, with the new mode (also raises <see cref="Changed"/>)
        /// </summary>
        public event Action<PlayerMode> ModeChanged;

        /// <summary>
        /// Switches between controlling the avatar and being in the game. Setting the mode we're already in does nothing.
        /// </summary>
        public void SetMode(PlayerMode mode)
        {
            if (Mode == mode)
            {
                return;
            }
            Mode = mode;
            ModeChanged?.Invoke(mode);
            Changed?.Invoke(this);
        }

        /// <summary>
        /// True if every one of the given actions is in progress
        /// </summary>
        public bool IsDoing(PlayerAction action)
        {
            return action != PlayerAction.None && (Actions & action) == action;
        }

        /// <summary>
        /// Marks an action as started. Starting one that's already in progress does nothing.
        /// </summary>
        public void Begin(PlayerAction action)
        {
            SetActions(Actions | action);
        }

        /// <summary>
        /// Marks an action as finished. Ending one that isn't in progress does nothing.
        /// </summary>
        public void End(PlayerAction action)
        {
            SetActions(Actions & ~action);
        }

        /// <summary>
        /// Records where we are in joining the game
        /// </summary>
        public void SetConnection(PlayerConnectionState connection)
        {
            if (Connection == connection)
            {
                return;
            }
            Connection = connection;
            Changed?.Invoke(this);
        }

        private void SetActions(PlayerAction actions)
        {
            if (Actions == actions)
            {
                return;
            }
            Actions = actions;
            Changed?.Invoke(this);
        }
    }
}
