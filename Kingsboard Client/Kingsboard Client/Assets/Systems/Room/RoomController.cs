using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kingsboard.Room
{
    /// <summary>
    /// Owns the room the local player walks around in, and its grid (<see cref="RoomGrid"/>). The grid is the client's
    /// own: it isn't sent to the server and has nothing to do with the Kingsboard board. Every object placed in the room
    /// (a <see cref="RoomObjectController"/>) registers here and sits on whole cells; objects that block movement mark
    /// their cells as taken, and the avatar (<see cref="RoomAvatarController"/>) can only step onto free cells.
    /// The grid's size and cell size can be changed at any time (<see cref="SetGrid"/>, or in the Inspector during Play
    /// mode); objects and the avatar then snap to the new grid.
    /// Lives on the Room prefab (Assets/Prefabs/Room/Room.prefab), placed in the scene.
    /// </summary>
    public class RoomController : MonoBehaviour
    {
        [Tooltip("The length of one side of a grid cell, in room units. The avatar moves one cell per step.")]
        [SerializeField, Min(0.01f)] private float _cellSize = 8f;

        [Tooltip("How many cells the grid has along the room's x axis (east-west)")]
        [SerializeField, Min(1)] private int _width = 26;

        [Tooltip("How many cells the grid has along the room's z axis (north-south)")]
        [SerializeField, Min(1)] private int _depth = 26;

        [Tooltip("The middle of the grid: a point on the floor, in the room's local space")]
        [SerializeField] private Vector3 _centre = new Vector3(0f, -14f, 0f);

        [Tooltip("How high above the floor the camera may go before it would hit the beams, in room units")]
        [SerializeField, Min(0f)] private float _headroom = 100f;

        [Tooltip("Draw the grid in the Scene view while the Room is selected")]
        [SerializeField] private bool _drawGrid = true;

        private readonly List<RoomObjectController> _objects = new List<RoomObjectController>();
        private readonly Dictionary<Vector2Int, RoomObjectController> _blockers = new Dictionary<Vector2Int, RoomObjectController>();
        private RoomGrid _grid;

        /// <summary>
        /// The room's grid as it is now. Replaced, never changed in place, when the grid changes (see <see cref="GridChanged"/>).
        /// Outside Play mode it always reflects the Inspector values.
        /// </summary>
        public RoomGrid Grid => Application.isPlaying ? (_grid ??= BuildGrid()) : BuildGrid();

        /// <summary>
        /// Every object registered in the room
        /// </summary>
        public IReadOnlyList<RoomObjectController> Objects => _objects;

        /// <summary>
        /// How high above the floor the camera may go, in room units
        /// </summary>
        public float Headroom => _headroom;

        /// <summary>
        /// Raised after the grid's size or cell size changes, once every object has snapped to the new grid
        /// </summary>
        public event Action<RoomGrid> GridChanged;

        /// <summary>
        /// Raised after any object is placed, moved, or removed, so cells may have become free or taken
        /// </summary>
        public event Action LayoutChanged;

        /// <summary>
        /// Changes the grid's cell size and number of cells, keeping it centred where it was. Every object snaps to the
        /// nearest place on the new grid (objects that no longer fit stay where they snap to, even if off the grid), then
        /// <see cref="GridChanged"/> is raised.
        /// </summary>
        /// <param name="cellSize">The new cell size in room units (at least 0.01)</param>
        /// <param name="width">The new number of cells along x (at least 1)</param>
        /// <param name="depth">The new number of cells along z (at least 1)</param>
        public void SetGrid(float cellSize, int width, int depth)
        {
            _cellSize = Mathf.Max(0.01f, cellSize);
            _width = Mathf.Max(1, width);
            _depth = Mathf.Max(1, depth);
            ApplyGrid();
        }

        /// <summary>
        /// The world position of the middle of <paramref name="cell"/>, on the floor
        /// </summary>
        public Vector3 CellToWorld(Vector2Int cell)
        {
            return transform.TransformPoint(RoomGridService.CellToLocal(Grid, cell));
        }

        /// <summary>
        /// The cell under a world point, which may be off the grid (check with <see cref="IsOnGrid"/>)
        /// </summary>
        public Vector2Int WorldToCell(Vector3 worldPoint)
        {
            return RoomGridService.LocalToCell(Grid, transform.InverseTransformPoint(worldPoint));
        }

        /// <summary>
        /// True if <paramref name="cell"/> is on the grid
        /// </summary>
        public bool IsOnGrid(Vector2Int cell)
        {
            return RoomGridService.Contains(Grid, cell);
        }

        /// <summary>
        /// True if the avatar can stand on <paramref name="cell"/>: it's on the grid and no blocking object covers it
        /// </summary>
        public bool IsWalkable(Vector2Int cell)
        {
            return IsOnGrid(cell) && !_blockers.ContainsKey(cell);
        }

        /// <summary>
        /// The blocking object covering <paramref name="cell"/>, or null if none does
        /// </summary>
        public RoomObjectController GetBlocker(Vector2Int cell)
        {
            return _blockers.TryGetValue(cell, out RoomObjectController blocker) ? blocker : null;
        }

        /// <summary>
        /// The free cell nearest to <paramref name="cell"/> (searching outwards ring by ring), or null if every cell is taken
        /// </summary>
        public Vector2Int? FindNearestWalkable(Vector2Int cell)
        {
            cell = RoomGridService.ClampToGrid(Grid, cell);
            int maxRadius = Mathf.Max(Grid.Width, Grid.Depth);
            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                        {
                            continue;
                        }
                        Vector2Int candidate = new Vector2Int(cell.x + dx, cell.y + dy);
                        if (IsWalkable(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// True if <paramref name="roomObject"/> could be placed with its lowest cell at <paramref name="anchor"/>, turned by
        /// <paramref name="quarterTurns"/>: the whole footprint must be on the grid, and if the object blocks movement no
        /// other blocking object may cover any of its cells. The object's own cells don't count against it.
        /// </summary>
        public bool CanPlace(RoomObjectController roomObject, Vector2Int anchor, int quarterTurns)
        {
            Vector2Int size = RoomGridService.RotateSize(roomObject.UnturnedSize, quarterTurns);
            if (!RoomGridService.ContainsFootprint(Grid, anchor, size))
            {
                return false;
            }
            if (!roomObject.BlocksMovement)
            {
                return true;
            }
            foreach (Vector2Int cell in RoomGridService.FootprintCells(anchor, size))
            {
                RoomObjectController blocker = GetBlocker(cell);
                if (blocker != null && blocker != roomObject)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Adds an object to the room where it currently stands. Called by <see cref="RoomObjectController"/> itself;
        /// registering one that's already registered just refreshes its cells.
        /// </summary>
        public void Register(RoomObjectController roomObject)
        {
            if (!_objects.Contains(roomObject))
            {
                _objects.Add(roomObject);
            }
            RefreshBlockers();
            LayoutChanged?.Invoke();
        }

        /// <summary>
        /// Removes an object from the room, freeing its cells. Called by <see cref="RoomObjectController"/> itself.
        /// </summary>
        public void Unregister(RoomObjectController roomObject)
        {
            if (_objects.Remove(roomObject))
            {
                RefreshBlockers();
                LayoutChanged?.Invoke();
            }
        }

        /// <summary>
        /// Recounts which cells are taken after an object moved. Called by <see cref="RoomObjectController"/> itself.
        /// </summary>
        public void NotifyMoved(RoomObjectController roomObject)
        {
            RefreshBlockers();
            LayoutChanged?.Invoke();
        }

        private void OnValidate()
        {
            // Lets the grid be adjusted from the Inspector while playing
            if (Application.isPlaying && _grid != null)
            {
                ApplyGrid();
            }
        }

        private void ApplyGrid()
        {
            _grid = BuildGrid();
            foreach (RoomObjectController roomObject in _objects)
            {
                roomObject.SnapToGrid();
            }
            RefreshBlockers();
            GridChanged?.Invoke(_grid);
            LayoutChanged?.Invoke();
        }

        private RoomGrid BuildGrid()
        {
            return RoomGridService.Create(_cellSize, _width, _depth, _centre);
        }

        private void RefreshBlockers()
        {
            _blockers.Clear();
            foreach (RoomObjectController roomObject in _objects)
            {
                if (roomObject == null || !roomObject.BlocksMovement)
                {
                    continue;
                }
                foreach (Vector2Int cell in roomObject.Cells)
                {
                    if (_blockers.TryGetValue(cell, out RoomObjectController other) && IsOnGrid(cell))
                    {
                        Debug.LogWarning($"[Room] {roomObject.name} and {other.name} both cover cell {cell}.", roomObject);
                        continue;
                    }
                    _blockers[cell] = roomObject;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawGrid)
            {
                return;
            }

            RoomGrid grid = Application.isPlaying ? Grid : BuildGrid();
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
            Vector3 lift = Vector3.up * 0.05f;
            for (int x = 0; x <= grid.Width; x++)
            {
                Vector3 from = grid.Origin + new Vector3(x * grid.CellSize, 0f, 0f) + lift;
                Gizmos.DrawLine(from, from + new Vector3(0f, 0f, grid.Depth * grid.CellSize));
            }
            for (int y = 0; y <= grid.Depth; y++)
            {
                Vector3 from = grid.Origin + new Vector3(0f, 0f, y * grid.CellSize) + lift;
                Gizmos.DrawLine(from, from + new Vector3(grid.Width * grid.CellSize, 0f, 0f));
            }

            if (Application.isPlaying)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.35f);
                foreach (Vector2Int cell in _blockers.Keys)
                {
                    Gizmos.DrawCube(RoomGridService.CellToLocal(grid, cell) + lift, new Vector3(grid.CellSize * 0.9f, 0.1f, grid.CellSize * 0.9f));
                }
            }
        }
    }
}
