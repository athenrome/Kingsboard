using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Enumerations;
using UnityEngine;
using UnityEngine.Serialization;

namespace Kingsboard.Board
{
    /// <summary>
    /// Handles the client-side game board in Unity. The board is too big to load whole, so this shows one section of it
    /// at a time (through <see cref="BoardGrpcHandler"/>) and loads another with <see cref="RequestSection"/> or
    /// <see cref="PanSection"/>. It draws one block per tile, stacked into columns.
    /// Tiles are drawn at their position on the whole board: board x maps to world x, board y to world z, and board z
    /// (the level) to world y, so tile (x, y, z) is centred at local position (x, (z + 0.5) * <see cref="LevelHeight"/>, y).
    /// Lives on the Board prefab (Assets/Prefabs/Board/Board.prefab), placed in the scene. Every tile block is an
    /// instance of the BoardTile prefab, created under the prefab's Tiles child.
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

        [Tooltip("Width and depth of the first section to load. 0 uses the server's default size.")]
        [SerializeField, FormerlySerializedAs("sectionSize")] private int _sectionSize = 0;

        [Tooltip("Prefab drawn for every tile (Assets/Prefabs/Board/BoardTile.prefab). Needs a Renderer; it is coloured and scaled per tile.")]
        [SerializeField, FormerlySerializedAs("tilePrefab")] private GameObject _tilePrefab;

        [Tooltip("Where tile blocks are created (the Tiles child of the Board prefab)")]
        [SerializeField, FormerlySerializedAs("tileRoot")] private Transform _tileRoot;

        private BoardGrpcHandler _handler;
        private readonly Dictionary<TileType, Material> _materials = new Dictionary<TileType, Material>();
        private GameObject[] _tileObjects = Array.Empty<GameObject>();

        /// <summary>
        /// The section of the board currently loaded, or null before one has arrived
        /// </summary>
        public BoardSection Section { get; private set; }

        /// <summary>
        /// Largest width or depth of a section the server allows (0 until connected)
        /// </summary>
        public int MaxSectionSize => _handler != null ? _handler.MaxSectionSize : 0;

        /// <summary>
        /// Raised when a whole new section arrives (on connect, after <see cref="RequestSection"/>, or when the server
        /// recreates the board)
        /// </summary>
        public event Action<BoardSection> SectionLoaded;

        /// <summary>
        /// Raised after tiles in the current section change, with the tiles placed or replaced and the positions removed
        /// </summary>
        public event Action<IReadOnlyList<MapTile>, IReadOnlyList<BoardPosition>> TilesChanged;

        /// <summary>
        /// Asks the server for a different section of the board; it replaces the current one when it arrives.
        /// A region hanging over the board's edge is moved back onto it.
        /// Returns null when the request was accepted, otherwise the reason it wasn't.
        /// </summary>
        public Task<string> RequestSection(BoardRegion region)
        {
            if (_handler == null)
            {
                return Task.FromResult("The board isn't connected");
            }
            return _handler.RequestSectionAsync(region);
        }

        /// <summary>
        /// Loads the section next to the current one: moved by <paramref name="dx"/> columns and <paramref name="dy"/> rows,
        /// keeping its size. Returns null when the request was accepted, otherwise the reason it wasn't.
        /// </summary>
        public Task<string> PanSection(int dx, int dy)
        {
            if (Section == null)
            {
                return Task.FromResult("No section has loaded yet");
            }
            return RequestSection(BoardService.Move(Section.Region, dx, dy));
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
        /// where a pawn would stand. Columns outside the loaded section count as empty.
        /// </summary>
        public Vector3 StackTopToWorld(BoardPosition column)
        {
            int stackHeight = Section != null ? BoardService.GetStackHeight(Section, column) : 0;
            return transform.TransformPoint(new Vector3(column.X, stackHeight * LevelHeight, column.Y));
        }

        /// <summary>
        /// The board position whose block contains a world position. It may be a tile, empty space, or outside the loaded
        /// section (check with <see cref="BoardService.Contains(BoardSection, BoardPosition)"/>). For a raycast hit, nudge
        /// the point into the block you want first: hit.point - hit.normal * 0.01f for the tile hit, or + hit.normal * 0.01f
        /// for the empty space in front of it.
        /// </summary>
        public BoardPosition WorldToPosition(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return new BoardPosition(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.z), Mathf.FloorToInt(local.y / LevelHeight));
        }

        private void Start()
        {
            if (_tilePrefab == null || _tileRoot == null)
            {
                Debug.LogError("[Board] The BoardController needs its tile prefab and Tiles root assigned, so the board can't be drawn. Use the Board prefab.", this);
                enabled = false;
                return;
            }

            _handler = new BoardGrpcHandler();
            _handler.SectionReceived += OnSectionReceived;
            _handler.TilesChanged += OnTilesChanged;
            _handler.Disconnected += OnDisconnected;

            BoardRegion firstRegion = null;
            if (_sectionSize > 0)
            {
                firstRegion = new BoardRegion(new BoardPosition(0, 0, 0), _sectionSize, _sectionSize);
            }
            _handler.Connect(firstRegion);
        }

        private void OnDestroy()
        {
            if (_handler != null)
            {
                _handler.SectionReceived -= OnSectionReceived;
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

        private void OnSectionReceived(BoardSection section)
        {
            Section = section;
            RebuildTiles();

            BoardRegion region = section.Region;
            Debug.Log($"[Board] Loaded a {region.Width} x {region.Depth} section at ({region.Origin.X}, {region.Origin.Y}) of the {section.BoardWidth} x {section.BoardDepth} x {section.BoardHeight} board");
            SectionLoaded?.Invoke(section);
        }

        private void OnTilesChanged(string boardId, long version, IReadOnlyList<MapTile> placed, IReadOnlyList<BoardPosition> removed)
        {
            // Ignore changes for a board we don't have, or older than what we already have
            if (Section == null || boardId != Section.Id || version <= Section.Version)
            {
                return;
            }

            BoardService.ApplyChanges(Section, version, placed, removed);

            foreach (BoardPosition position in removed)
            {
                if (BoardService.Contains(Section, position))
                {
                    SetTileObject(BoardService.IndexOf(Section, position), null);
                }
            }
            foreach (MapTile tile in placed)
            {
                if (BoardService.Contains(Section, tile.Position))
                {
                    SetTileObject(BoardService.IndexOf(Section, tile.Position), tile);
                }
            }

            TilesChanged?.Invoke(placed, removed);
        }

        private void OnDisconnected()
        {
            // Keep showing the last section; the server sends it again in full when we reconnect
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

            _tileObjects = new GameObject[Section.Tiles.Length];
            for (int i = 0; i < Section.Tiles.Length; i++)
            {
                SetTileObject(i, Section.Tiles[i]);
            }
        }

        /// <summary>
        /// Makes the block at a section index match <paramref name="tile"/>: creates, updates or (for null) destroys it
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
                tileObject = Instantiate(_tilePrefab, _tileRoot, false);
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
