namespace LocalHomeModule;

internal sealed partial class HomeRuntime
{
    // Starts identify playback epochs. Explicit restarts also remove the cursor so two
    // starts in the same millisecond still create distinct event epochs.
    private readonly Dictionary<string,(long Start,double Cycle)> mechanismEventProgress=new(StringComparer.Ordinal),debugEventProgress=new(StringComparer.Ordinal);
    private Dictionary<string,(long Start,double Cycle)> ActiveEventProgress => mechanismDebug?debugEventProgress:mechanismEventProgress;
    private void ClearMechanismEvents() { mechanismEventProgress.Clear(); debugEventProgress.Clear(); }

    private void StartMechanismTargets(IEnumerable<string> targets,long start,bool restart)
        => StartMechanismTargets(targets,start,restart,ActiveStarts,ActiveEventProgress,!mechanismDebug);

    private void StartMechanismTargets(IEnumerable<string> targets,long start,bool restart,
        Dictionary<string,long> timeline,Dictionary<string,(long Start,double Cycle)> progress,bool publish)
    {
        foreach(string target in targets)
            if(restart || !timeline.ContainsKey(target))
            {
                timeline[target]=start;
                progress.Remove(target);
            }
        if(publish) MarkMechanisms();
    }

    private void TickMechanismEvents(long now)
    {
        // The normal timeline remains authoritative for peers while local debug is open.
        TickMechanismEvents(now,mechanismStarts,mechanismEventProgress,mechanismFrozen,true);
        if(mechanismDebug) TickMechanismEvents(now,debugStarts,debugEventProgress,debugFrozen,false);
    }

    private void TickMechanismEvents(long now,Dictionary<string,long> timeline,
        Dictionary<string,(long Start,double Cycle)> progress,long frozen,bool publish)
    {
        if(frozen>0) return; // Manual end never emits completion/cycle events.
        var starts=new Dictionary<string,long>(timeline,StringComparer.Ordinal);
        var queued=new HashSet<string>(StringComparer.Ordinal);
        foreach(var node in mechanismConfig.Nodes)
        {
            if(!starts.TryGetValue(node.Id,out long start) || start<=0 || now<start) continue;
            double elapsed=((double)now-start)/1000-node.Delay;
            if(elapsed<0) continue;
            double duration=MechanismEngine.CycleDuration(node);
            // Zero-length once tracks complete after delay; repeating stationary tracks
            // have no finite cycle boundary and therefore emit no cycle events.
            double cycle=node.Mode=="once"?(elapsed>=duration?1:0):(duration>0?Math.Floor(elapsed/duration):0);
            double previous=progress.TryGetValue(node.Id,out var cursor)&&cursor.Start==start?cursor.Cycle:0;
            if(cycle<=previous) continue;
            progress[node.Id]=(start,cycle);
            var targets=node.Mode=="once"?node.OnCompleteTargets:node.OnCycleTargets;
            if(targets!=null) queued.UnionWith(targets);
        }
        // Snapshot first, apply second: event chains cannot recurse during a tick.
        // Start at the observation tick (+ the normal network lead), never replay old
        // boundaries after an offline/slow frame. Every target starts at most once.
        if(queued.Count>0) StartMechanismTargets(MechanismSubtree(queued),now+300,true,timeline,progress,publish);
    }
}
