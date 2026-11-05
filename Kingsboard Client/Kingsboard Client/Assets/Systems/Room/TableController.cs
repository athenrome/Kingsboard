using System;
using Kingsboard.Players;
using UnityEngine;

namespace Kingsboard.Room
{
    /// <summary>
    /// The table the game is played at. The board and pawns can only be used while the local player is seated here:
    /// the avatar walks up to the table, then sits (<see cref="Sit"/>), which starts <see cref="PlayerAction.AtTable"/>
    /// in the player's state. The camera then looks at the board and pawns can be selected; standing up
    /// (<see cref="Stand"/>) ends it and hands the camera back to the avatar.
    /// Lives on the Table prefab (Assets/Prefabs/Room/Table.prefab) beside its <see cref="RoomObjectController"/>.
    /// </summary>
    public class TableController : MonoBehaviour
    {
        [Tooltip("The table's place on the room grid (the RoomObjectController on this prefab)")]
        [SerializeField] private RoomObjectController _roomObject;

        [Tooltip("How many cells away from the table the avatar can be and still sit at it (1 means right beside it, corners included)")]
        [SerializeField, Min(1)] private int _reach = 1;

        private PlayerController _player;
        private bool _wasSeated;

        /// <summary>
        /// The table's place on the room grid
        /// </summary>
        public RoomObjectController RoomObject => _roomObject;

        /// <summary>
        /// True while the local player is seated at the table
        /// </summary>
        public bool IsSeated => _player != null && _player.State.IsAtTable;

        /// <summary>
        /// Raised when the local player sits down (true) or stands up (false)
        /// </summary>
        public event Action<bool> SeatedChanged;

        /// <summary>
        /// True if an avatar standing on <paramref name="cell"/> is close enough to sit at the table
        /// </summary>
        public bool IsInReach(Vector2Int cell)
        {
            if (_roomObject == null)
            {
                return false;
            }
            int distance = _roomObject.DistanceTo(cell);
            return distance > 0 && distance <= _reach;
        }

        /// <summary>
        /// The world point an avatar should face to look at the table: the middle of the table at floor height
        /// </summary>
        public Vector3 GetFacingPoint()
        {
            return transform.position;
        }

        /// <summary>
        /// Seats the local player at the table, so the board and pawns can be used. Does nothing if already seated or
        /// there's no player in the scene; it's up to the caller to check the avatar is in reach.
        /// </summary>
        public void Sit()
        {
            if (_player == null || _player.State.IsAtTable)
            {
                return;
            }
            _player.State.Begin(PlayerAction.AtTable);
        }

        /// <summary>
        /// Stands the local player up from the table. Does nothing if not seated.
        /// </summary>
        public void Stand()
        {
            if (_player == null || !_player.State.IsAtTable)
            {
                return;
            }
            _player.State.End(PlayerAction.AtTable);
        }

        private void Awake()
        {
            if (_roomObject == null)
            {
                Debug.LogError("[Room] The TableController needs its RoomObjectController assigned. Use the Table prefab.", this);
            }
        }

        private void Start()
        {
            _player = FindAnyObjectByType<PlayerController>();
            if (_player == null)
            {
                Debug.LogWarning("[Room] No PlayerController in the scene, so no one can sit at the table. Place the Player prefab.", this);
                return;
            }
            _player.State.Changed += OnStateChanged;
            _wasSeated = _player.State.IsAtTable;
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.State.Changed -= OnStateChanged;
                _player.State.End(PlayerAction.AtTable);
            }
        }

        private void OnStateChanged(PlayerStateTracker state)
        {
            if (state.IsAtTable == _wasSeated)
            {
                return;
            }
            _wasSeated = state.IsAtTable;
            SeatedChanged?.Invoke(_wasSeated);
        }
    }
}
