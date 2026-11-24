namespace Game.Input {
    // Immutable view of a single default (pre-override) binding entry, used by
    // the codex site exporter to render machine-independent key glyphs.
    public struct DefaultBindingInfo {
        public string Path;
        public bool IsComposite;
        public bool IsPartOfComposite;
        public string HumanName;
    }
}
