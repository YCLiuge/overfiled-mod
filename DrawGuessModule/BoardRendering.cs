using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using DrawGuessModule.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrawGuessModule
{
    public static partial class PaintingSystem
    {
        private sealed class ChannelBoard
        {
            public string Id = "", Label = "";
            public SceneBoardPlacement Placement = new SceneBoardPlacement();
            public Color32[] Pixels = new Color32[CanvasSize * CanvasSize];
            public readonly List<Color32[]> Undo = new List<Color32[]>();
            public Texture2D? Texture;
            public Il2CppStructArray<Color32>? Upload;
            public GameObject? Model;
            public TextMesh? Caption;
            public Material? Material, Frame;
            public bool Dirty = true;
            public bool UsesGameModel;
            public float RetryAt;
            public ChannelBoard() { Array.Fill(Pixels, White); }
        }
        private static readonly Dictionary<string, ChannelBoard> ChannelBoards = new Dictionary<string, ChannelBoard>(StringComparer.Ordinal);

        /// <summary>Main thread only. Operations are an accepted delta, or a full replay when reset is true.</summary>
        public static void SetChannelBoard(string boardId, SceneBoardPlacement placement, string label, List<DrawingOperation> operations, bool reset)
        {
            if (string.IsNullOrWhiteSpace(boardId) || boardId.Length > 128 || placement == null || !placement.IsValid) return;
            foreach (char c in boardId) if (char.IsControl(c)) return;
            if (!ChannelBoards.TryGetValue(boardId, out var board))
            {
                board = new ChannelBoard { Id = boardId };
                ChannelBoards.Add(boardId, board);
            }
            board.Placement = placement.Copy();
            board.Label = string.IsNullOrEmpty(label) ? boardId : label.Length <= 180 ? label : label.Substring(0, 180);
            // Rendering remote boards must never reset/cancel the local drawing pointer or history.
            var pixels = _pixels;
            bool dirty = _dirty;
            var history = UndoBuffers.ToArray();
            try
            {
                _pixels = board.Pixels;
                _dirty = board.Dirty;
                UndoBuffers.Clear();
                UndoBuffers.AddRange(board.Undo);
                if (reset) { Array.Fill(_pixels, White); UndoBuffers.Clear(); _dirty = true; }
                if (operations != null)
                    foreach (var op in operations)
                        ApplyOperation(op.Tool, op.X1, op.Y1, op.X2, op.Y2, op.Color, op.Size, op.NewStroke);
            }
            finally
            {
                board.Pixels = _pixels!; board.Dirty = _dirty;
                board.Undo.Clear(); board.Undo.AddRange(UndoBuffers);
                _pixels = pixels; _dirty = dirty;
                UndoBuffers.Clear(); UndoBuffers.AddRange(history);
            }
            EnsureChannelBoard(board);
            if (board.Model != null) SetBoardTransform(board.Model, board.Placement);
            if (board.Caption != null) board.Caption.text = board.Label;
            UploadChannelBoard(board);
            if (_sceneEasel != null) _sceneEasel.SetActive(false);
        }

        public static void RemoveChannelBoard(string id)
        {
            if (id == null || !ChannelBoards.TryGetValue(id, out var board)) return;
            DestroyChannelModel(board);
            if (board.Texture != null) Object.Destroy(board.Texture);
            board.Texture = null; board.Upload = null; board.Undo.Clear();
            ChannelBoards.Remove(id);
            if (ChannelBoards.Count == 0 && _sceneEasel != null) _sceneEasel.SetActive(true);
        }

        public static void ClearChannelBoards()
        {
            foreach (string id in new List<string>(ChannelBoards.Keys)) RemoveChannelBoard(id);
        }

        private static void UploadChannelBoard(ChannelBoard board)
        {
            if (!board.Dirty || board.Texture == null) return;
            board.Upload ??= new Il2CppStructArray<Color32>(CanvasSize * CanvasSize);
            for (int i = 0; i < board.Pixels.Length; i++) board.Upload[i] = board.Pixels[i];
            board.Texture.SetPixels32(board.Upload);
            board.Texture.Apply(false, false);
            board.Dirty = false;
        }

        private static void EnsureChannelBoard(ChannelBoard board)
        {
            if (Time.unscaledTime < board.RetryAt) return;
            if (board.Model != null && (board.UsesGameModel || FindGameEasel() == null)) return;
            board.RetryAt = Time.unscaledTime + 2;
            DestroyChannelModel(board);
            try
            {
                Shader shader = BoardShader();
                if (board.Texture == null)
                {
                    board.Texture = new Texture2D(CanvasSize, CanvasSize, TextureFormat.RGBA32, false)
                    { name = "DrawGuess_ChannelCanvas_" + board.Id, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    board.Dirty = true;
                }
                board.Model = new GameObject("DrawGuess_ChannelEasel_" + board.Id);
                SetBoardTransform(board.Model, board.Placement);
                board.UsesGameModel = CreateGameEasel(board.Model);
                if (!board.UsesGameModel) board.Frame = CreateFrame(board.Model, shader);
                board.Material = CreateBoardQuad(board.Model, shader, board.Texture);
                var label = new GameObject("OwnerRoomLabel");
                label.transform.SetParent(board.Model.transform, false);
                label.transform.localPosition = new Vector3(0, 2.7f, 0.35f);
                label.transform.localRotation = Quaternion.Euler(0, 180, 0);
                board.Caption = label.AddComponent<TextMesh>();
                _font ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
                board.Caption.font = _font;
                label.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
                board.Caption.fontSize = 48; board.Caption.characterSize = 0.035f;
                board.Caption.anchor = TextAnchor.MiddleCenter; board.Caption.alignment = TextAlignment.Center;
                board.Caption.richText = false; board.Caption.color = Color.white;
                board.Caption.text = board.Label;
            }
            catch (Exception e)
            {
                DestroyChannelModel(board);
                board.RetryAt = Time.unscaledTime + 5;
                MelonLoader.MelonLogger.Msg("[DrawGuess] 频道画架生成失败，将重试：" + e.Message);
            }
        }

        private static void DestroyChannelModel(ChannelBoard board)
        {
            if (board.Model != null) { board.Model.SetActive(false); Object.Destroy(board.Model); }
            if (board.Material != null) Object.Destroy(board.Material);
            if (board.Frame != null) Object.Destroy(board.Frame);
            board.Model = null; board.Caption = null; board.Material = board.Frame = null;
        }
    }
}
