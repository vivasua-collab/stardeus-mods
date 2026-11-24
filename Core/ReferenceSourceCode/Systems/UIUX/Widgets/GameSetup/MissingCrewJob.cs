using Game.Constants;
using Game.Systems.Mental;

namespace Game.Systems.UIUX {
    // One row of the crew setup screen's Missing Essential Jobs warning. Solar panel cleaning
    // gets its own name and tip: the gap is a job only a flyer can do there, and a plain
    // "Cleaning" row would read as false next to a robot whose hand cleans.
    public readonly struct MissingCrewJob {
        public readonly JobType Job;
        public readonly bool NeedsFlyer;

        public MissingCrewJob(JobType job, bool needsFlyer) {
            Job = job;
            NeedsFlyer = needsFlyer;
        }

        public string NameT => NeedsFlyer ? T.CrewMissingPanelCleaning : Job.NameT;
        public string TipT => NeedsFlyer ? T.CrewMissingPanelCleaningDesc : T.CrewMissingJobsDesc;
    }
}
