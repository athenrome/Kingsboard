using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kingsboard.Board
{
    /// <summary>
    /// Handles the client-side game board in Unity: it keeps the latest <see cref="GameBoard"/> from the server
    /// (through <see cref="BoardGrpcHandler"/>) and draws one block per tile, stacked into columns.
    /// Board x maps to world x, board y to world z, and board z (the level) to world y:
    /// tile (x, y, z) is centred at local position (x, (z + 0.5) * <see cref="LevelHeight"/>, y).
    /// It creates itself when the game starts, so nothing needs adding to a scene.
    /// To change its settings in the Inspector, add this component to a GameObject in the scene instead.
    /// </summary>
    public class BoardController : MonoBehaviour
    {
        /// <summary>
        /// Height of one level of a stack, in world units (each tile is 1 x 1 across)
        /// </summary>
        public const float LevelHeight = 0.5f;

        /// <summary>
        /// Gap left between neighbouring blocks so the tiles and levels stay readable, in world units
        /// </summary>
        public const float TileGap = 0.04f;

        [Tooltip("Move the main camera to look at the board whenever a new board arrives")]
        [SerializeField] private bool frameCamera = true;

        private BoardGrpcHandler _handler;
        private readonly Dictionary<TileType, Material> _materials = new Dictionary<TileType, Material>();
        private GameObject[] _tileObjects = Array.Empty<GameObject>();
        private Transform _tileRoot;

        /// <summary>
        /// The latest board from the server, or null before one has arrived
        /// </summary>
        public GameBoard Board { get; private set; }

        /// <summary>
        /// Raised when a whole new board arrives (on connect, or when the server recreates the board)
        /// </summary>
        public event Action<GameBoard> BoardLoaded;

        /// <summary>
        /// Raised after tiles on the current board change, with the tiles placed or replaced and the positions removed
        /// </summary>
        public event Action<IReadOnlyList<MapTile>, IReadOnlyList<BoardPosition>> TilesChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfMissing()
        {
            if (FindAnyObjectByType<BoardController>() == null)
            {
                GameObject board = new GameObject("Board");
                DontDestroyOnLoad(board);
                board.AddComponent<BoardController>();
            }
        }

        /// <summary>
        /// World position of the centre of a board position's block. Works for empty space as well as tiles.
        /// </summary>
        public Vector3 PositionToWorld(BoardPosition position)
        {
            return transform.TransformPoint(PositionToLocal(position));
        }

        /// <summary>
        /// World position of the centre of the top surface of the stack in the position's column (its Z is ignored),
        /// where a pawn would stand
        /// </summary>
        public Vector3 StackTopToWorld(BoardPosition column)
        {
            int stackHeight = Board != null ? Board.GetStackHeight(column) : 0;
            return transform.TransformPoint(new Vector3(column.X, stackHeight * LevelHeight, column.Y));
        }

        /// <summary>
        /// The board position whose block contains a world position. It may be a tile, empty space, or off the board
        /// (check with <see cref="BoardExtensions.Contains"/>). For a raycast hit, nudge the point into the block you
        /// want first: hit.point - hit.normal * 0.01f for the tile hit, or + hit.normal * 0.01f for the empty space in front of it.
        /// </summary>
        public BoardPosition WorldToPosition(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return new BoardPosition()
            {
                X = Mathf.RoundToInt(local.x),
                Y = Mathf.RoundToInt(local.z),
                Z = Mathf.FloorToInt(local.y / LevelHeight)
            };
        }

        private void Start()
        {
            _tileRoot = new GameObject("Tiles").transform;
            _tileRoot.SetParent(transform, false);

            _handler = new BoardGrpcHandler();
            _handler.BoardReceived += OnBoardReceived;
            _handler.TilesChanged += OnTilesChanged;
            _handler.Disconnected += OnDisconnected;
            _handler.Connect();
        }

        private void OnDestroy()
        {
            if (_handler != null)
            {
                _handler.BoardReceived -= OnBoardReceived;
                _handler.TilesChanged -= OnTilesChanged;
                _handler.Disconnected -= OnDisconnected;
                _handler.Dispose();
                _handler = null;
            }

            foreach (Material material in _materials.Values)
            {
                Destroy(material);
            }
            _materials.Clear();
        }

        private void OnBoardReceived(GameBoard board)
        {
            Board = board;
            RebuildTiles();

            if (frameCamera)
            {
                FrameCamera();
            }

            Debug.Log($"[Board] Received a {board.Width} x {board.Depth} x {board.Height} board");
            BoardLoaded?.Invoke(board);
        }

        private void OnTilesChanged(string boardId, long version, IReadOnlyList<MapTile> placed, IReadOnlyList<BoardPosition> removed)
        {
            // Ignore changes for a board we don't have, or older than what we already have
            if (Board == null || boardId != Board.Id || version <= Board.Version)
            {
                return;
            }

            Board.ApplyChanges(version, placed, removed);

            foreach (BoardPosition position in removed)
            {
                if (Board.Contains(position))
                {
                    SetTileObject(Board.IndexOf(position), null);
                }
            }
            foreach (MapTile tile in placed)
            {
                if (Board.Contains(tile.Position))
                {
                    SetTileObject(Board.IndexOf(tile.Position), tile);
                }
            }

            TilesChanged?.Invoke(placed, removed);
        }

        private void OnDisconnected()
        {
            // Keep showing the last board; the server sends it again in full when we reconnect
            Debug.LogWarning("[Board] Disconnected from the board server. Reconnecting...");
        }

        private void RebuildTiles()
        {
            foreach (GameObject tileObject in _tileObjects)
            {
                if (tileObject != null)
                {
                    Destroy(tileObject);
                }
            }

            _tileObjects = new GameObject[Board.Tiles.Length];
            for (int i = 0; i < Board.Tiles.Length; i++)
            {
                SetTileObject(i, Board.Tiles[i]);
            }
        }

        /// <summary>
        /// Makes the block at a board index match <paramref name="tile"/>: creates, updates or (for null) destroys it
        /// </summary>
        private void SetTileObject(int index, MapTile tile)
        {
            GameObject tileObject = _tileObjects[index];

            if (tile == null)
            {
                if (tileObject != null)
                {
                    Destroy(tileObject);
                    _tileObjects[index] = null;
                }
                return;
            }

            if (tileObject == null)
            {
                tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tileObject.transform.SetParent(_tileRoot, false);
                _tileObjects[index] = tileObject;
            }

            BoardPosition position = tile.Position;
            tileObject.name = $"Tile ({position.X}, {position.Y}, {position.Z})";
            tileObject.transform.localPosition = PositionToLocal(position);
            tileObject.transform.localScale = new Vector3(1f - TileGap, LevelHeight - TileGap, 1f - TileGap);

            Renderer tileRenderer = tileObject.GetComponent<Renderer>();
            tileRenderer.sharedMaterial = GetMaterial(tile.Type, tileRenderer.sharedMaterial);
        }

        /// <summary>
        /// Centre of a board position's block, relative to this board's transform
        /// </summary>
        private static Vector3 PositionToLocal(BoardPosition position)
        {
            return new Vector3(position.X, (position.Z + 0.5f) * LevelHeight, position.Y);
        }

        /// <summary>
        /// One shared material per tile type, copied from the render pipeline's default material
        /// </summary>
        private Material GetMaterial(TileType type, Material template)
        {
            if (!_materials.TryGetValue(type, out Material material))
            {
                material = new Material(template);
                material.name = $"Tile ({type})";
                material.color = GetColor(type);
                _materials[type] = material;
            }
            return material;
        }

        private void FrameCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            float halfHeight = Board.Height * LevelHeight / 2f;
            Vector3 centre = transform.TransformPoint(new Vector3((Board.Width - 1) / 2f, halfHeight, (Board.Depth - 1) / 2f));
            float size = Mathf.Max(Board.Width, Board.Depth);
            camera.transform.position = centre + new Vector3(0f, size * 0.8f + halfHeight, -size * 0.8f);
            camera.transform.LookAt(centre);
        }

        private static Color GetColor(TileType type)
        {
            switch (type)
            {
                case TileType.Water: return new Color(0.2f, 0.45f, 0.8f);
                case TileType.Plains: return new Color(0.55f, 0.75f, 0.35f);
                case TileType.Forest: return new Color(0.15f, 0.45f, 0.2f);
                case TileType.Hills: return new Color(0.6f, 0.5f, 0.3f);
                case TileType.Mountain: return new Color(0.85f, 0.85f, 0.9f);
                case TileType.Stone: return new Color(0.45f, 0.45f, 0.48f);
                default: return Color.magenta;
            }
        }
    }
}
