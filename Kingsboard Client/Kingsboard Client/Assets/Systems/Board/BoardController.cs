using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kingsboard.Board
{
    /// <summary>
    /// Handles the client-side game board in Unity: it keeps the latest <see cref="GameBoard"/> from the server
    /// (through <see cref="BoardGrpcHandler"/>) and draws one block per tile. The board lies on the XZ plane:
    /// tile (x, y) is centred at world position (x, 0, y), one unit per tile.
    /// It creates itself when the game starts, so nothing needs adding to a scene.
    /// To change its settings in the Inspector, add this component to a GameObject in the scene instead.
    /// </summary>
    public class BoardController : MonoBehaviour
    {
        /// <summary>
        /// Gap between neighbouring tiles, in world units
        /// </summary>
        public const float TileGap = 0.05f;

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
        /// Raised after tiles on the current board change
        /// </summary>
        public event Action<IReadOnlyList<MapTile>> TilesChanged;

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
        /// World position of the centre of the top of tile (x, y)
        /// </summary>
        public Vector3 TileToWorld(int x, int y)
        {
            MapTile tile = Board?.GetTile(x, y);
            float height = tile != null ? GetHeight(tile.Type) : 0f;
            return transform.TransformPoint(new Vector3(x, height, y));
        }

        /// <summary>
        /// The tile under a world position, or null if it's off the board
        /// </summary>
        public MapTile WorldToTile(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return Board?.GetTile(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.z));
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

            Debug.Log($"[Board] Received a {board.Width} x {board.Height} board");
            BoardLoaded?.Invoke(board);
        }

        private void OnTilesChanged(string boardId, long version, IReadOnlyList<MapTile> tiles)
        {
            // Ignore changes for a board we don't have, or older than what we already have
            if (Board == null || boardId != Board.Id || version <= Board.Version)
            {
                return;
            }

            Board.ApplyChanges(version, tiles);
            foreach (MapTile tile in tiles)
            {
                if (Board.Contains(tile.X, tile.Y))
                {
                    UpdateTileObject(_tileObjects[tile.Y * Board.Width + tile.X], tile);
                }
            }

            TilesChanged?.Invoke(tiles);
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
                Destroy(tileObject);
            }

            _tileObjects = new GameObject[Board.Width * Board.Height];
            for (int y = 0; y < Board.Height; y++)
            {
                for (int x = 0; x < Board.Width; x++)
                {
                    GameObject tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tileObject.name = $"Tile ({x}, {y})";
                    tileObject.transform.SetParent(_tileRoot, false);
                    _tileObjects[y * Board.Width + x] = tileObject;

                    UpdateTileObject(tileObject, Board.GetTile(x, y));
                }
            }
        }

        private void UpdateTileObject(GameObject tileObject, MapTile tile)
        {
            if (tileObject == null)
            {
                return;
            }

            TileType type = tile != null ? tile.Type : TileType.Unknown;
            float height = GetHeight(type);
            Vector2Int position = tile != null ? new Vector2Int(tile.X, tile.Y) : Vector2Int.zero;

            // A block standing on the board plane, so higher terrain is taller
            tileObject.transform.localPosition = new Vector3(position.x, height / 2f, position.y);
            tileObject.transform.localScale = new Vector3(1f - TileGap, height, 1f - TileGap);
            tileObject.GetComponent<Renderer>().sharedMaterial = GetMaterial(type, tileObject.GetComponent<Renderer>().sharedMaterial);
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

            Vector3 centre = transform.TransformPoint(new Vector3((Board.Width - 1) / 2f, 0f, (Board.Height - 1) / 2f));
            float size = Mathf.Max(Board.Width, Board.Height);
            camera.transform.position = centre + new Vector3(0f, size * 0.9f, -size * 0.75f);
            camera.transform.LookAt(centre);
        }

        private static float GetHeight(TileType type)
        {
            switch (type)
            {
                case TileType.Water: return 0.1f;
                case TileType.Plains: return 0.2f;
                case TileType.Forest: return 0.3f;
                case TileType.Hills: return 0.5f;
                case TileType.Mountain: return 0.9f;
                default: return 0.2f;
            }
        }

        private static Color GetColor(TileType type)
        {
            switch (type)
            {
                case TileType.Water: return new Color(0.2f, 0.45f, 0.8f);
                case TileType.Plains: return new Color(0.55f, 0.75f, 0.35f);
                case TileType.Forest: return new Color(0.15f, 0.45f, 0.2f);
                case TileType.Hills: return new Color(0.6f, 0.5f, 0.3f);
                case TileType.Mountain: return new Color(0.55f, 0.55f, 0.58f);
                default: return Color.magenta;
            }
        }
    }
}
