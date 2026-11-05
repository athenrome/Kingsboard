using System.Collections.Generic;
using UnityEngine;

namespace Kingsboard.Room
{
    /// <summary>
    /// Something placed in the room (furniture, pillars, the fireplace) that sits on the room's grid. It covers a
    /// rectangle of whole cells (its footprint), faces one of four directions, and may block the avatar from walking
    /// through it. Its footprint is set in room units and turned into the nearest whole number of cells for the grid
    /// in use, so it stays the right size if the cell size changes.
    /// When the game starts it remembers where it was placed (its home), snaps to the nearest cells and registers with
    /// the <see cref="RoomController"/>. When the grid changes it snaps again from its home, so changing the grid and
    /// changing it back puts everything where it was. Move it with <see cref="TryPlace"/>, which is what room
    /// customisation will use; that also moves its home.
    /// Put it on any object that should line up with the grid, e.g. the Table and Bench prefabs. The object's height is
    /// left alone; only where it sits on the floor and which way it faces are snapped.
    /// </summary>
    public class RoomObjectController : MonoBehaviour
    {
        [Tooltip("How much floor the object covers when unturned, in room units: x along its own x axis, y along its own z axis. Rounded to whole cells.")]
        [SerializeField] private Vector2 _footprint = new Vector2(8f, 8f);

        [Tooltip("Where the object's pivot sits relative to the middle of its footprint when unturned, in room units (height ignored). Leave at zero when the pivot is in the middle.")]
        [SerializeField] private Vector3 _pivotOffset = Vector3.zero;

        [Tooltip("True if the avatar can't walk through the cells this covers")]
        [SerializeField] private bool _blocksMovement = true;

        [Tooltip("The room this belongs to. Leave empty to use the Room it's inside, or the one in the scene.")]
        [SerializeField] private RoomController _room;

        private bool _registered;
        private Vector3 _homeCentre;
        private int _homeTurns;

        /// <summary>
        /// The lowest cell the object covers (its smallest x and y)
        /// </summary>
        public Vector2Int Anchor { get; private set; }

        /// <summary>
        /// Which way the object faces, in quarter turns clockwise seen from above (0 to 3)
        /// </summary>
        public int QuarterTurns { get; private set; }

        /// <summary>
        /// The footprint's size in cells as turned now (width and depth swap on an odd number of quarter turns)
        /// </summary>
        public Vector2Int Size => RoomGridService.RotateSize(UnturnedSize, QuarterTurns);

        /// <summary>
        /// The footprint's size in cells on the current grid when the object is unturned (at least 1 × 1)
        /// </summary>
        public Vector2Int UnturnedSize => _room != null ? RoomGridService.SizeToCells(_room.Grid, _footprint) : Vector2Int.one;

        /// <summary>
        /// True if the avatar can't walk through the cells this covers
        /// </summary>
        public bool BlocksMovement => _blocksMovement;

        /// <summary>
        /// Every cell the object covers, including any that fall off the grid
        /// </summary>
        public IEnumerable<Vector2Int> Cells => RoomGridService.FootprintCells(Anchor, Size);

        /// <summary>
        /// The room this object is in, or null if there's no Room in the scene
        /// </summary>
        public RoomController Room => _room;

        /// <summary>
        /// How many steps a cell is from this object's footprint, counting diagonal steps as one (0 means inside it)
        /// </summary>
        public int DistanceTo(Vector2Int cell)
        {
            return RoomGridService.DistanceToFootprint(cell, Anchor, Size);
        }

        /// <summary>
        /// Moves the object so its lowest cell is <paramref name="anchor"/>, facing <paramref name="quarterTurns"/>, if
        /// <see cref="RoomController.CanPlace"/> allows it, and makes that its new home. Returns false and leaves it where
        /// it was if not.
        /// </summary>
        public bool TryPlace(Vector2Int anchor, int quarterTurns)
        {
            if (_room == null || !_room.CanPlace(this, anchor, quarterTurns))
            {
                return false;
            }

            Anchor = anchor;
            QuarterTurns = RoomGridService.NormaliseTurns(quarterTurns);
            _homeTurns = QuarterTurns;
            _homeCentre = RoomGridService.FootprintCentre(_room.Grid, Anchor, Size);
            ApplyTransform();
            _room.NotifyMoved(this);
            return true;
        }

        /// <summary>
        /// Snaps the object to the cells nearest its home on the current grid. Called when it registers and whenever the
        /// grid changes; it doesn't check for overlaps.
        /// </summary>
        public void SnapToGrid()
        {
            if (_room == null)
            {
                return;
            }

            QuarterTurns = _homeTurns;
            Anchor = RoomGridService.SnapFootprint(_room.Grid, _homeCentre, Size);
            ApplyTransform();
        }

        private void Start()
        {
            if (_room == null)
            {
                _room = FindRoom();
            }
            if (_room == null)
            {
                Debug.LogWarning($"[Room] {name} has a RoomObjectController but there's no Room in the scene to sit in. Place the Room prefab.", this);
                return;
            }

            RememberHome();
            SnapToGrid();
            _room.Register(this);
            _registered = true;
        }

        private void OnDestroy()
        {
            if (_registered && _room != null)
            {
                _room.Unregister(this);
            }
        }

        /// <summary>
        /// Takes where the object stands now as its home: the middle of its footprint and the quarter turn nearest the way
        /// it faces
        /// </summary>
        private void RememberHome()
        {
            Quaternion localRotation = Quaternion.Inverse(_room.transform.rotation) * transform.rotation;
            _homeTurns = RoomGridService.YawToTurns(localRotation.eulerAngles.y);
            _homeCentre = _room.transform.InverseTransformPoint(transform.position) - TurnedPivotOffset(_homeTurns);
        }

        /// <summary>
        /// Puts the object's pivot where <see cref="Anchor"/> and <see cref="QuarterTurns"/> say, keeping its height
        /// </summary>
        private void ApplyTransform()
        {
            Vector3 local = RoomGridService.FootprintCentre(_room.Grid, Anchor, Size) + TurnedPivotOffset(QuarterTurns);
            Vector3 current = _room.transform.InverseTransformPoint(transform.position);
            local.y = current.y;
            transform.SetPositionAndRotation(
                _room.transform.TransformPoint(local),
                _room.transform.rotation * Quaternion.Euler(0f, QuarterTurns * 90f, 0f));
        }

        private Vector3 TurnedPivotOffset(int quarterTurns)
        {
            Vector3 offset = Quaternion.Euler(0f, quarterTurns * 90f, 0f) * _pivotOffset;
            offset.y = 0f;
            return offset;
        }

        private RoomController FindRoom()
        {
            RoomController room = GetComponentInParent<RoomController>();
            return room != null ? room : FindAnyObjectByType<RoomController>();
        }

        /// <summary>
        /// Snaps the object to the room's grid in the editor, the same way it snaps when the game starts
        /// </summary>
        [ContextMenu("Snap to room grid")]
        private void SnapInEditor()
        {
            RoomController previous = _room;
            if (_room == null)
            {
                _room = FindRoom();
            }
            if (_room == null)
            {
                Debug.LogWarning("[Room] No Room in the scene to snap to.", this);
                return;
            }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(transform, "Snap to room grid");
#endif
            RememberHome();
            SnapToGrid();
            _room = previous;
        }

        private void OnDrawGizmosSelected()
        {
            RoomController room = _room != null ? _room : FindRoom();
            if (room == null)
            {
                return;
            }

            // Shows the footprint this would snap to from where it stands now
            RoomGrid grid = room.Grid;
            Quaternion localRotation = Quaternion.Inverse(room.transform.rotation) * transform.rotation;
            int turns = RoomGridService.YawToTurns(localRotation.eulerAngles.y);
            Vector2Int size = RoomGridService.RotateSize(RoomGridService.SizeToCells(grid, _footprint), turns);
            Vector3 centre = room.transform.InverseTransformPoint(transform.position) - TurnedPivotOffset(turns);
            Vector2Int anchor = RoomGridService.SnapFootprint(grid, centre, size);

            Gizmos.matrix = room.transform.localToWorldMatrix;
            Gizmos.color = _blocksMovement ? new Color(1f, 0.5f, 0.2f, 0.9f) : new Color(0.4f, 1f, 0.4f, 0.9f);
            Vector3 footprintCentre = RoomGridService.FootprintCentre(grid, anchor, size) + Vector3.up * 0.1f;
            Gizmos.DrawWireCube(footprintCentre, new Vector3(size.x * grid.CellSize, 0.2f, size.y * grid.CellSize));
        }
    }
}
