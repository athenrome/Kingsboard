using System;
using UnityEngine;

namespace Kingsboard.Players
{
    /// <summary>
    /// Joins the game as this client's player (through <see cref="PlayerGrpcHandler"/>) and keeps track of who we are,
    /// so other systems can tell which pawns we control and act as us. The server gives each player some pawns to control.
    /// It also tracks what we're doing right now (joined or not, typing in chat, aiming a card) through <see cref="State"/>.
    /// Lives on the Player prefab (Assets/Prefabs/Players/Player.prefab), placed in the scene; set the name there, or
    /// start the client with --name=YourName to override it.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        private const string NameArgument = "--name=";

        [Tooltip("Name to join the game with. The server may change it to keep names unique. --name= on the command line overrides it.")]
        [SerializeField] private string _playerName = "Player";

        private PlayerGrpcHandler _handler;

        /// <summary>
        /// Who we are, or null while we aren't joined
        /// </summary>
        public PlayerInfo Player { get; private set; }

        /// <summary>
        /// What the local player is doing right now. Systems report their actions to it (e.g. chat reports
        /// <see cref="PlayerAction.TypingInChat"/>) and check it before acting (e.g. the camera ignores keys while
        /// <see cref="PlayerStateTracker.BlocksCameraMovement"/>). Available from the start, before we've joined.
        /// </summary>
        public PlayerStateTracker State { get; } = new PlayerStateTracker();

        /// <summary>
        /// True while we are joined to the game
        /// </summary>
        public bool IsJoined => Player != null;

        /// <summary>
        /// Raised when we join (or rejoin after a disconnect, with new ids)
        /// </summary>
        public event Action<PlayerInfo> Joined;

        /// <summary>
        /// Raised when we stop being joined (the connection dropped); we rejoin by ourselves
        /// </summary>
        public event Action Left;

        /// <summary>
        /// True if <paramref name="ownerId"/> is our player id, i.e. a pawn with that owner is ours to control
        /// </summary>
        public bool IsMe(Guid? ownerId)
        {
            return Player != null && ownerId.HasValue && ownerId.Value == Player.PlayerId;
        }

        private void Start()
        {
            _handler = new PlayerGrpcHandler();
            _handler.Joined += OnJoined;
            _handler.Disconnected += OnDisconnected;
            _handler.Connect(ReadName());
        }

        private void OnDestroy()
        {
            if (_handler != null)
            {
                _handler.Joined -= OnJoined;
                _handler.Disconnected -= OnDisconnected;
                _handler.Dispose();
                _handler = null;
            }
        }

        private void OnJoined(PlayerInfo player)
        {
            Player = player;
            State.SetConnection(PlayerConnectionState.Joined);
            Debug.Log($"[Players] Joined the game as {player.Name} ({player.Colour})");
            Joined?.Invoke(player);
        }

        private void OnDisconnected()
        {
            Player = null;
            State.SetConnection(PlayerConnectionState.Rejoining);
            Debug.LogWarning("[Players] Disconnected from the game. Rejoining...");
            Left?.Invoke();
        }

        /// <summary>
        /// The name to join with: --name= from the command line if given, otherwise the one set on the prefab
        /// </summary>
        private string ReadName()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith(NameArgument, StringComparison.OrdinalIgnoreCase) && arg.Length > NameArgument.Length)
                {
                    return arg.Substring(NameArgument.Length);
                }
            }
            return _playerName;
        }
    }
}
