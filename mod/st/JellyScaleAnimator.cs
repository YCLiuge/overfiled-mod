using UnityEngine;
using MelonLoader;
using System.Collections;

namespace mod.st
{
    /// <summary>
    /// 果冻缩放动画：挤压 → 拉伸 → 平滑恢复（无振荡）
    /// 支持一组物体同时播放，支持循环播放
    /// </summary>
    public class JellyScaleAnimator
    {
        public static JellyScaleAnimator Instance { get; private set; } = new JellyScaleAnimator();

        // ---------- 状态 ----------
        public bool IsPlaying { get; private set; } = false;
        private List<Transform> _targets = new List<Transform>();
        private List<Vector3> _baseScales = new List<Vector3>();  // 每个物体的初始缩放
        private object _coroutine;

        // ---------- 动画参数 ----------
        public float Duration = 0.5f;            // 单次动画时长（秒）
        public float SquashAmount = 0.4f;        // 挤压强度
        public float StretchAmount = 0.4f;       // 拉伸强度
        public float SquashRatio = 0.4f;         // 挤压时其他轴的扩张比例
        public float StretchRatio = 0.4f;        // 拉伸时其他轴的收缩比例
        public Vector3 StretchAxis = Vector3.up; // 主拉伸轴

        // ---------- 循环播放 ----------
        public bool Loop = false;                // 是否循环播放
        public float LoopInterval = 0f;        // 循环间隔（秒）
        public int LoopCount = -1;               // 循环次数（-1 = 无限）

        // ---------- 阶段时间点 ----------
        public float SquashPhaseEnd = 0.33f;
        public float StretchPhaseEnd = 0.66f;

        // ---------- 设置目标（单个） ----------
        public void SetTarget(Transform target)
        {
            _targets.Clear();
            _baseScales.Clear();
            if (target != null)
            {
                _targets.Add(target);
                _baseScales.Add(target.localScale);
            }
            MelonLogger.Msg($"果冻动画目标: {target?.name}");
        }
        public int GetCount() => _targets.Count;
        // ---------- 设置目标（一组） ----------
        public void SetTargets(IEnumerable<Transform> targets)
        {
            _targets.Clear();
            _baseScales.Clear();

            if (targets == null) return;

            foreach (var t in targets)
            {
                if (t == null) continue;
                _targets.Add(t);
                _baseScales.Add(t.localScale);
            }
            MelonLogger.Msg($"果冻动画目标数量: {_targets.Count}");
        }

        public bool HasTarget(Transform transform)
        {
            foreach (var t in _targets)
            {
                if (t == transform) return true;
            }
            return false;
        }
        // ---------- 添加单个目标 ----------
        public void AddTarget(Transform target)
        {
            if (target == null) return;
            _targets.Add(target);
            _baseScales.Add(target.localScale);
        }

        // ---------- 清空目标 ----------
        public void ClearTargets()
        {
            RestoreAllScales();
            _targets.Clear();
            _baseScales.Clear();
        }

        // ---------- 播放一次 ----------
        public void PlayOnce()
        {
            if (_targets.Count == 0) { MelonLogger.Warning("未设置目标"); return; }
            if (IsPlaying) { MelonLogger.Msg("动画正在播放"); return; }
            Loop = false;
            _coroutine = MelonCoroutines.Start(PlayCoroutine());
        }

        // ---------- 循环播放 ----------
        public void PlayLoop(int count = -1)
        {
            if (_targets.Count == 0) { MelonLogger.Warning("未设置目标"); return; }
            if (IsPlaying) { MelonLogger.Msg("动画正在播放"); return; }
            Loop = true;
            LoopCount = count;
            _coroutine = MelonCoroutines.Start(PlayCoroutine());
        }

        // ---------- 停止 ----------
        public void Stop()
        {
            if (_coroutine != null) { MelonCoroutines.Stop(_coroutine); _coroutine = null; }
            RestoreAllScales();
            IsPlaying = false;
            Loop = false;
            MelonLogger.Msg("果冻动画已停止");
        }

        // ---------- 恢复所有物体缩放 ----------
        private void RestoreAllScales()
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i] != null)
                    _targets[i].localScale = _baseScales[i];
            }
        }

        // ---------- 核心协程 ----------
        private IEnumerator PlayCoroutine()
        {
            IsPlaying = true;
            int playedCount = 0;
            Vector3 stretchDir = StretchAxis.normalized;

            while (Loop && (LoopCount < 0 || playedCount < LoopCount))
            {
                // ---- 单次动画 ----
                float elapsed = 0f;
                while (elapsed < Duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / Duration);

                    ComputeScaleFactors(t, out float scaleAlongAxis, out float scaleOtherAxis);

                    // 应用到所有目标
                    for (int i = 0; i < _targets.Count; i++)
                    {
                        if (_targets[i] == null) continue;

                        Vector3 baseScale = _baseScales[i];
                        Vector3 scale = baseScale;

                        scale.x *= 1f + (scaleAlongAxis - 1f) * Mathf.Abs(stretchDir.x)
                                      + (scaleOtherAxis - 1f) * (1f - Mathf.Abs(stretchDir.x));
                        scale.y *= 1f + (scaleAlongAxis - 1f) * Mathf.Abs(stretchDir.y)
                                      + (scaleOtherAxis - 1f) * (1f - Mathf.Abs(stretchDir.y));
                        scale.z *= 1f + (scaleAlongAxis - 1f) * Mathf.Abs(stretchDir.z)
                                      + (scaleOtherAxis - 1f) * (1f - Mathf.Abs(stretchDir.z));

                        _targets[i].localScale = scale;
                    }

                    yield return null;
                }

                // 单次结束后恢复
                RestoreAllScales();
                playedCount++;

                // 循环间隔
                if (Loop && (LoopCount < 0 || playedCount < LoopCount))
                    yield return new WaitForSeconds(LoopInterval);
            }

            // 全部结束
            RestoreAllScales();
            IsPlaying = false;
            _coroutine = null;
            MelonLogger.Msg("果冻动画播放完毕");
        }

        // ---------- 计算单帧缩放系数 ----------
        private void ComputeScaleFactors(float t, out float scaleAlongAxis, out float scaleOtherAxis)
        {
            if (t < SquashPhaseEnd)
            {
                // 阶段 1：挤压
                float p = t / SquashPhaseEnd;
                float eased = EaseOutCubic(p);
                scaleAlongAxis = Mathf.Lerp(1f, 1f - SquashAmount, eased);
                scaleOtherAxis = Mathf.Lerp(1f, 1f + SquashAmount * SquashRatio, eased);
            }
            else if (t < StretchPhaseEnd)
            {
                // 阶段 2：拉伸
                float p = (t - SquashPhaseEnd) / (StretchPhaseEnd - SquashPhaseEnd);
                float eased = EaseInOutCubic(p);
                float startSquash = 1f - SquashAmount;
                float startOther = 1f + SquashAmount * SquashRatio;
                float endStretch = 1f + StretchAmount;
                float endOther = 1f - StretchAmount * StretchRatio;

                scaleAlongAxis = Mathf.Lerp(startSquash, endStretch, eased);
                scaleOtherAxis = Mathf.Lerp(startOther, endOther, eased);
            }
            else
            {
                // 阶段 3：恢复
                float p = (t - StretchPhaseEnd) / (1f - StretchPhaseEnd);
                float eased = EaseInOutCubic(p);
                float startStretch = 1f + StretchAmount;
                float startOther = 1f - StretchAmount * StretchRatio;

                scaleAlongAxis = Mathf.Lerp(startStretch, 1f, eased);
                scaleOtherAxis = Mathf.Lerp(startOther, 1f, eased);
            }
        }

        // ---------- 缓动函数 ----------
        private float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private float EaseInOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
}