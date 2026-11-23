using ModLoader;

public sealed class Fixture : ILogic
{
    private readonly int _id = Probe.Construct();
    public string Name => "Verification fixture";
    public string Version => "1.0.0";
    public void OnLoad() => Probe.Load(_id);
    public void OnUnload() => Probe.Unload(_id);
    public void OnUpdate() => Probe.Update(_id);
}
