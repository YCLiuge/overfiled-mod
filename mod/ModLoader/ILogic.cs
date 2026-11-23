namespace ModLoader
{
    /// <summary>
    /// 业务逻辑契约。所有 logic.dll 的入口类必须实现这个接口。
    /// </summary>
    public interface ILogic
    {
        string Name { get; }
        string Version { get; }

        /// <summary>加载时调用（只调用一次）</summary>
        void OnLoad();

        /// <summary>每帧调用（主 mod 的 OnUpdate 里驱动）</summary>
        void OnUpdate();

        /// <summary>卸载时调用（切换新版本前）</summary>
        void OnUnload();
    }
}
