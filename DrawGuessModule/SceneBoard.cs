using System;
using DrawGuessModule.Core;
using MelonLoader;
using mod;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrawGuessModule
{
    public static partial class PaintingSystem
    {
        private static GameObject? _sceneEasel;
        private static Material? _boardMaterial, _frameMaterial;
        private static SceneBoardPlacement? _boardPlacement;
        private static bool _boardWanted, _automaticBoard = true;
        private static float _boardRetryAt;

        public static void PlaceSceneBoard()
        {
            var placement = PlacementInFrontOfPlayer();
            if (placement == null) throw new InvalidOperationException("玩家尚未进入场景，暂时无法放置画架");
            SetSceneBoardPlacement(placement);
        }
        public static void SetSceneBoardAutomaticPlacement(bool enabled)
        {
            _automaticBoard = enabled;
            if (!enabled) RemoveSceneBoard();
        }
        public static SceneBoardPlacement? GetSceneBoardPlacement()
        {
            _boardWanted = true;
            EnsureSceneBoard();
            return _boardPlacement?.Copy();
        }
        public static void SetSceneBoardPlacement(SceneBoardPlacement placement)
        {
            if (placement == null || !placement.IsValid) return;
            _boardWanted = true; _boardPlacement = placement.Copy();
            EnsureSceneBoard();
            if (_sceneEasel != null) SetBoardTransform(_sceneEasel, _boardPlacement);
        }
        // Legacy/current-panel easel only. Channel boards have independent lifecycle APIs.
        public static void RemoveSceneBoard()
        {
            if (_sceneEasel != null) { _sceneEasel.SetActive(false); Object.Destroy(_sceneEasel); }
            if (_boardMaterial != null) Object.Destroy(_boardMaterial);
            if (_frameMaterial != null) Object.Destroy(_frameMaterial);
            _sceneEasel = null; _boardMaterial = _frameMaterial = null;
            _boardPlacement = null; _boardWanted = false; _boardRetryAt = 0;
        }
        public static string DescribeSceneBoard()
        {
            if (ChannelBoards.Count > 0) return "频道画架：" + ChannelBoards.Count + " 架（独立画布；隐藏面板后 Alt＋点击画架选择）";
            if (_sceneEasel == null) return "场景画架：尚未生成（打开面板或 dg board 放置）";
            var p = _sceneEasel.transform.position;
            Color32 sample = _texture != null ? _texture.GetPixel(CanvasSize / 4, CanvasSize / 2) : White;
            return FormattableString.Invariant($"场景画架：({p.x:F2}, {p.y:F2}, {p.z:F2}) | 共享画布 {_boardMaterial != null && _boardMaterial.mainTexture == _texture} | 面板 {(_visible ? "显示" : "隐藏")} | 上传完成 {!_dirty} | 纹理采样 #{sample.r:X2}{sample.g:X2}{sample.b:X2}");
        }
        private static SceneBoardPlacement? PlacementInFrontOfPlayer()
        {
            var player = TipMsgCore.i?.mainplayer;
            if (player == null) return null;
            var origin = player.transform;
            Vector3 forward = origin.forward; forward.y = 0;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            forward.Normalize();
            var position = origin.position + forward * 2;
            return new SceneBoardPlacement { X = position.x, Y = position.y, Z = position.z, Yaw = Quaternion.LookRotation(-forward).eulerAngles.y };
        }
        private static void EnsureTexture()
        {
            EnsureBuffer();
            if (_texture != null) return;
            _texture = new Texture2D(CanvasSize, CanvasSize, TextureFormat.RGBA32, false)
            { name = "DrawGuess_PanelCanvas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _dirty = true;
        }
        private static Shader BoardShader() => Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard") ?? throw new InvalidOperationException("找不到画板材质着色器");
        private static void EnsureSceneBoard()
        {
            // Runtime channel boards own scene presentation. Legacy placement metadata may
            // still be set/read while its preview is disabled, without spawning a private easel.
            if (!_boardWanted || !_automaticBoard) return;
            if (_boardPlacement == null && _automaticBoard) _boardPlacement = PlacementInFrontOfPlayer();
            if (ChannelBoards.Count > 0) { if (_sceneEasel != null) _sceneEasel.SetActive(false); return; }
            if (_sceneEasel != null || _boardPlacement == null || Time.unscaledTime < _boardRetryAt) return;
            _boardRetryAt = Time.unscaledTime + 2;
            if (_boardMaterial != null) Object.Destroy(_boardMaterial);
            if (_frameMaterial != null) Object.Destroy(_frameMaterial);
            _boardMaterial = _frameMaterial = null;
            try
            {
                EnsureTexture();
                var shader = BoardShader();
                _sceneEasel = new GameObject("DrawGuess_SceneEasel");
                SetBoardTransform(_sceneEasel, _boardPlacement);
                if (!CreateGameEasel(_sceneEasel)) _frameMaterial = CreateFrame(_sceneEasel, shader);
                _boardMaterial = CreateBoardQuad(_sceneEasel, shader, _texture!);
                UploadIfDirty();
            }
            catch (Exception e)
            {
                var placement = _boardPlacement;
                RemoveSceneBoard(); _boardPlacement = placement; _boardWanted = true;
                _boardRetryAt = Time.unscaledTime + 5;
                MelonLogger.Msg("[DrawGuess] 场景画架生成失败，将重试：" + e.Message);
            }
        }
        private static void SetBoardTransform(GameObject model, SceneBoardPlacement placement)
        {
            model.transform.position = new Vector3(placement.X, placement.Y, placement.Z);
            model.transform.rotation = Quaternion.Euler(0, placement.Yaw, 0);
        }
        private static Material CreateBoardQuad(GameObject model, Shader shader, Texture2D texture)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Painting_Quad";
            // The original canvas was a direct child of the cloned furniture. Its local
            // coordinates must inherit that model's scale, rather than the unscaled channel root.
            var furniture = model.transform.Find("GameEaselVisual/FurnitureEaselVisual");
            quad.transform.SetParent(furniture != null ? furniture : model.transform, false);
            quad.transform.localPosition = new Vector3(0, 1.48f, .33f);
            quad.transform.localRotation = Quaternion.Euler(9, 180, 0);
            quad.transform.localScale = new Vector3(1.2f, .9f, 1);
            var material = new Material(shader) { name = "DrawGuess_BoardMaterial", mainTexture = texture };
            try { quad.GetComponent<MeshRenderer>().sharedMaterial = material; return material; }
            catch { Object.Destroy(material); throw; }
        }
        private static Material CreateFrame(GameObject model, Shader shader)
        {
            var material = new Material(shader) { name = "DrawGuess_FrameMaterial", color = new Color(.35f, .19f, .08f, 1) };
            try
            {
                AddFramePart(model, material, "LeftLeg", new Vector3(-.48f, .9f, .12f), new Vector3(.08f, 1.8f, .1f));
                AddFramePart(model, material, "RightLeg", new Vector3(.48f, .9f, .12f), new Vector3(.08f, 1.8f, .1f));
                AddFramePart(model, material, "Top", new Vector3(0, 1.97f, .29f), new Vector3(1.36f, .08f, .12f));
                AddFramePart(model, material, "Bottom", new Vector3(0, .99f, .29f), new Vector3(1.36f, .08f, .12f));
                AddFramePart(model, material, "Left", new Vector3(-.64f, 1.48f, .29f), new Vector3(.08f, 1, .12f));
                AddFramePart(model, material, "Right", new Vector3(.64f, 1.48f, .29f), new Vector3(.08f, 1, .12f));
                return material;
            }
            catch { Object.Destroy(material); throw; }
        }
        private static void AddFramePart(GameObject root, Material material, string name, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name; part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        private static void TickSceneBoard()
        {
            EnsureSceneBoard(); UploadIfDirty();
            foreach (var board in ChannelBoards.Values) { EnsureChannelBoard(board); UploadChannelBoard(board); }
            if (_visible || !Input.GetMouseButtonDown(0) ||
                !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) return;
            var camera = Camera.main;
            if (camera == null) return;
            if (!Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore) || hit.transform == null) return;
            string? selected = null;
            foreach (var pair in ChannelBoards)
                if (pair.Value.Model != null && (hit.transform == pair.Value.Model.transform || hit.transform.IsChildOf(pair.Value.Model.transform)))
                { selected = pair.Key; break; }
            // Dispatch outside dictionary iteration: runtime selection may remove/rebuild boards synchronously.
            if (selected != null) { Dispatch("dg boardselect " + selected); return; }
            if (ChannelBoards.Count == 0 && _sceneEasel != null &&
                (hit.transform == _sceneEasel.transform || hit.transform.IsChildOf(_sceneEasel.transform))) Show();
        }
    }
}
