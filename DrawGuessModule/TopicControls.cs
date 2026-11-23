using UnityEngine;
using UnityEngine.UI;

namespace DrawGuessModule;

public static partial class PaintingSystem
{
    private static Button? _answerButton;
    private static bool _choosingTopic, _topicDrawer, _canGuess;

    // The runtime supplies role and phase from the authenticated room snapshot.
    public static void SetTopicState(string phase, bool isDrawer)
    {
        bool choosing = phase == "choosing";
        if ((_choosingTopic != choosing || _topicDrawer != isDrawer) && _guess != null)
            _guess.text = "";
        _choosingTopic = choosing;
        _topicDrawer = isDrawer;
        _canGuess = phase == "playing" && !isDrawer;
        RefreshTopicControls();
    }

    private static void RefreshTopicControls()
    {
        bool submitTopic = _choosingTopic && _topicDrawer;
        bool enabled = !_freeMode && (submitTopic || _canGuess);
        if (_guess != null)
        {
            _guess.interactable = enabled;
            _guess.characterLimit = submitTopic ? 16 : 120;
        }
        if (_answerButton != null)
        {
            _answerButton.interactable = enabled;
            Text label = _answerButton.GetComponentInChildren<Text>();
            if (label != null) label.text = submitTopic ? "提交题目，开始作画" : _choosingTopic ? "等待画手出题" : "提交答案";
        }
        if (!_freeMode && _choosingTopic && _secretText != null)
            _secretText.text = submitTopic
                ? "轮到你出题\n在右侧输入 1–16 字题目\n提交后开始作画和计时"
                : "画手正在出题\n提交后开始作画和猜词\n答案对猜词者隐藏";
    }
}
