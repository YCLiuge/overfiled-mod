using System;
using System.Collections.Generic;
using System.Globalization;
using DrawGuessModule.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DrawGuessModule
{
    public static partial class PaintingSystem
    {
        private static bool _freeMode;
        private static readonly List<GameObject> GuessOnly = new List<GameObject>();
        private static Button? _guessModeButton, _freeModeButton, _saveButton;
        private static Text? _scoreHeading, _helpText, _canvasHint, _publicServerText, _serverPortLabel;

        public static void SetPlayerName(string name)
        {
            _defaultNickname = string.IsNullOrEmpty(name) ? "读取角色名称…" : BoundText(name, 32);
            if (_nickname != null) _nickname.text = _defaultNickname;
        }

        public static void SetMode(string mode)
        {
            bool free = mode == PaintingDefaults.FreeMode;
            if (_freeMode == free) return;
            CancelPointer();
            _freeMode = free;
            _secretWord = "";
            _scoreboard = "";
            RefreshLabels();
        }

        private static void ChooseServer(bool local)
        {
            _defaultHost = local ? PaintingDefaults.LocalHost : PaintingDefaults.PublicHost;
            _defaultPort = (local ? PaintingDefaults.LocalPort : PaintingDefaults.PublicPort).ToString(CultureInfo.InvariantCulture);
            RefreshServerFields();
        }

        private static bool PublicServerSelected => _defaultHost == PaintingDefaults.PublicHost &&
            _defaultPort == PaintingDefaults.PublicPort.ToString(CultureInfo.InvariantCulture);

        private static void RefreshServerFields()
        {
            bool showAddress = !PublicServerSelected;
            if (_host != null)
            {
                _host.text = showAddress ? _defaultHost : "";
                _host.gameObject.SetActive(showAddress);
            }
            if (_port != null)
            {
                _port.text = showAddress ? _defaultPort : "";
                _port.gameObject.SetActive(showAddress);
            }
            if (_serverPortLabel != null) _serverPortLabel.gameObject.SetActive(showAddress);
            if (_publicServerText != null) _publicServerText.gameObject.SetActive(!showAddress);
        }

        private static string HidePublicEndpoint(string text) => text
            .Replace(PaintingDefaults.PublicHost + ":" + PaintingDefaults.PublicPort.ToString(CultureInfo.InvariantCulture), "公网服务器")
            .Replace(PaintingDefaults.PublicHost, "公网服务器");

        private static void AddModeControls(Transform panel)
        {
            _guessModeButton = Button(panel, "你画我猜", new Vector2(65, 345), new Vector2(180, 40), () => Dispatch("dg mode guess"));
            _freeModeButton = Button(panel, "自由绘画同步", new Vector2(260, 345), new Vector2(180, 40), () => Dispatch("dg mode free"));
            _saveButton = Button(panel, "保存 PNG 到本地", new Vector2(465, 345), new Vector2(200, 40), () => Dispatch("dg save"));
        }

        private static void RefreshModeLabels()
        {
            foreach (GameObject obj in GuessOnly)
                if (obj != null && obj.activeSelf == _freeMode) obj.SetActive(!_freeMode);
            if (_guessModeButton != null) _guessModeButton.interactable = _freeMode;
            if (_freeModeButton != null) _freeModeButton.interactable = !_freeMode;
            if (_saveButton != null) _saveButton.interactable = _freeMode && _canDraw;
            if (_scoreHeading != null) _scoreHeading.text = _freeMode ? "自由画室成员" : "房间与积分榜";
            if (_canvasHint != null) _canvasHint.text = _freeMode ? "512 × 512  ·  自由绘画 / 本地 PNG 保存" : "512 × 512  ·  笔迹经房主确认后显示";
            if (_freeMode)
            {
                if (_drawerText != null) _drawerText.text = _canDraw ? "我的画架 · 仅自己可画" : "他人的画架 · 只读观看";
                if (_secretText != null) _secretText.text = "无题目 · 无计时 · 无积分\n普通画架仅创建者可以修改\n同频道玩家同步观看";
            }
            if (_helpText != null)
                _helpText.text = _freeMode
                    ? "创建画架后同频道均可观看。\n普通画架只有创建者可画。\nAlt＋点击别人的画架只读观看。\n无需联网也可个人绘画。\n\nF8 / Esc：显示或隐藏\n放置画架：创建 / 移动自己的\n隐藏后画架持续同步。"
                    : "先创建自己的画架，再建房。\nAlt＋点击房主的游戏画架加入。\n轮到你时先在右侧输入题目，\n提交后开始作画和计时。\n游戏画架仅当前画手可画。\n其他玩家在右侧猜词。\n\nF8 / Esc：显示或隐藏";
        }
    }
}
