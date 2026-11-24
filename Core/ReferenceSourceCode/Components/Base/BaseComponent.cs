using System;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.CodeGen;
using KL.Signals;
using KL.Utils;
using UnityEngine;
using Game.Data.Space;
using Game.Systems.UIUX;

namespace Game.Components {
    /*
    Life cycle:

    => Place tile:
      1. Initialize()
      2. OnConfig() <- use to load defaults from Config
      3. OnReady(false) <- everything is loaded (entity id, all other comps)

    => Remove tile:
      1. OnDestroy() <- use to cleanup before destruction

    => Save:
      1. OnSave() <- use to store state in data (via GetOrCreateData())

    => Load:
      1. Load()
      2. Initialize()
      3. OnConfig() <- use to load defaults from config
      4. OnLoad() <- use to load public state
      5. OnReady(true) <- everything is loaded (entity id, all other comps)
    */
    public abstract class BaseComponent<T> : IComponent , ITaggedSignalListener
            where T : IComponent, new() {
        protected static void AddComponentPrototype(IComponent proto) {
            if (proto is not T) {
                D.Err("Prototype for {0} is extending BaseComponent<{1}>. It should be BaseComponent<{0}>. Rejecting the component prototype.",
                    typeof(T), proto);
                return;
            }
            Definitions.AddComponentPrototype(proto);
        }

        // Static because it's per class that extends the BaseComponent
        private static string componentName = null;
        private static int typeHash;
        private static int nameHash;
        private static bool isCacheCreated;
        public readonly BCallback OnChange = new();

        public void CreateComponentClassCache() {
            if (isCacheCreated) {
                #if !UNITY_EDITOR
                // if domain reload is disabled, this will happen, so we ignore this
                // in Unity editor.
                D.Err("Component trying to create cache twice! {0}", this);
                return;
                #endif
            }
            isCacheCreated = true;
            // Very expensive and GC heavy, especially during generation
            var name = GetType().Name;
            if (name.Length > 4) { // XComp -> X
                componentName = name[..^4]; // cut Comp
            } else {
                componentName = name;
            }
            typeHash = GetType().GetHashCode();
            nameHash = Hashes.S(componentName);
            OnCreateComponentClassCache();
        }

        // Add any extra cache here per component class if necessary
        protected virtual void OnCreateComponentClassCache() {
        }

        [NonSerialized]
        public GameState S;
        public string GetName() {
            return componentName;
        }

        public int TypeHash => typeHash;
        public int NameHash => nameHash;

        public bool SkipTypeLookupCache { get; set; }

        public int EntityId { get; set; }
        protected Entity entity;
        public Entity Entity => entity;
        public int LayerId { get; set; }
        public ComponentConfig Config { get; set; }
        public ComponentData Data { get; set; }
        public Tile Tile;
        public Being Being;
        public Obj Obj { get; private set; }
        public bool IsConstructed => Tile.Constructable.IsFinished;
        public bool IsUsable => EntityUtils.IsDeviceUsable(Tile);
        public object SignalTag => string.Format("G: {0}, E: {1}", S.GameId, EntityId);

        private InfoIcon infoIcon;
        private InfoIcon infoBubble;
        private InfoBar infoBar;
        private InfoText infoText;
        public Map Map => entity.Map;
        public virtual void UpdatePosIdx(int pos, int dx, int dy) { }
        protected InfoIcon GetInfoIcon(string iconId, string infoKey) {
            (infoIcon ??= InfoIcon.For(this)).UpdateIcon(iconId, infoKey);
            return infoIcon;
        }
        protected InfoIcon GetInfoIconWithTint(string iconId, string infoKey, Color tint) {
            (infoIcon ??= InfoIcon.For(this)).UpdateIcon(iconId, infoKey, tint);
            return infoIcon;
        }
        protected InfoIcon GetInfoBubble(string iconId, string infoKey) {
            (infoBubble ??= InfoBubble.For(this)).UpdateIcon(iconId, infoKey);
            return infoBubble;
        }
        protected InfoIcon GetInfoBubbleWithTint(string iconId, string infoKey, Color tint) {
            (infoBubble ??= InfoBubble.For(this)).UpdateIcon(iconId, infoKey, tint);
            return infoBubble;
        }
        protected InfoBar GetInfoBar(Color color, float pct, int value, int max, string infoKey) {
            (infoBar ??= InfoBar.For(this)).UpdateBar(color, pct, value, max, infoKey);
            return infoBar;
        }
        protected InfoText GetInfoText(string text, string hoverText) {
            (infoText ??= InfoText.For(this)).UpdateText(text, hoverText);
            return infoText;
        }
        protected InfoText GetInfoText(string text, string infoKey, Vector2 offset) {
            (infoText ??= InfoText.For(this)).UpdateText(text, infoKey, offset);
            return infoText;
        }
        public void ShowInfoText(string text, string infoKey, Vector2 offset) {
            var item = GetInfoText(text, infoKey, offset);
            if (item.IsShowing) {
                return;
            }
            S.Sys.Info.Show(item);
        }
        public void ShowInfoText(string text, string hoverText) {
            var item = GetInfoText(text, hoverText);
            if (item.IsShowing) {
                return;
            }
            S.Sys.Info.Show(item);
        }

        public void ShowInfoIconWithTint(string iconId, string infoText, Color tint) {
            var item = GetInfoIconWithTint(iconId, infoText, tint);
            if (item.IsShowing) {
                return;
            }
            S.Sys.Info.Show(item);
        }
        public void ShowInfoIcon(string iconId, string infoText) {
            var item = GetInfoIcon(iconId, infoText);
            if (item.IsShowing) {
                return;
            }
            S.Sys.Info.Show(item);
        }
        public void ShowThoughtBubble(string iconId, string infoText, int timeoutSeconds = 0) {
            ShowThoughtBubble(iconId, infoText, Consts.ColorWhiteV4, timeoutSeconds);
        }
        public void ShowThoughtBubble(string iconId, string infoText, Color tint, int timeoutSeconds = 0) {
            // D.Warn("Showing TB: {0}/{1}", iconId, tint);
            // D.Warn("Showing info bubble with text: {0} for {1}", infoText, Entity);
            var item = GetInfoBubbleWithTint(iconId, infoText, tint);
            item.Timeout = timeoutSeconds;
            if (item.IsShowing) {
                return;
            }
            S.Sys.Info.Show(item);
        }

        public InfoBar ShowInfoBar(Color color, float pct,
                string infoTextPretranslated) {
            var item = GetInfoBar(color, pct, Mathf.RoundToInt(pct * 100f), 100,
                infoTextPretranslated);
            if (!item.IsShowing) {
                item.IsPretranslated = true;
                S.Sys.Info.Show(item);
            }
            return item;
        }
        public InfoBar ShowInfoBar(Color color, float pct, float value, int max, string infoText) {
            var item = GetInfoBar(color, pct, Mathf.RoundToInt(value), max, infoText);
            if (!item.IsShowing) {
                item.IsPretranslated = false;
                S.Sys.Info.Show(item);
            }
            return item;
        }

        public void HideAllInfo() {
            // D.Warn("Hiding all info for {0}", Being);
            HideInfoIcon();
            HideThoughtBubble();
            HideInfoBar();
            HideInfoText();
        }

        public void HideInfoIcon() {
            if (infoIcon == null) { return; }
            // D.Warn("Hiding info icon for {0} -> {1}", infoIcon, Being);
            if (infoIcon.UpdateIcon(null, null)) {
                S.Sys.Info.UpdateNow(infoIcon);
            }
        }
        public void HideThoughtBubble() {
            if (infoBubble == null) { return; }
            // D.Warn("Hiding thought bubble");
            if (infoBubble.UpdateIcon(null, null)) {
                S.Sys.Info.UpdateNow(infoBubble);
            }
        }
        public void HideInfoBar() {
            if (infoBar == null) { return; }
            if (infoBar.UpdateBar(Consts.ColorHealthBar, 0, 0, 0, null)) {
                S.Sys.Info.UpdateNow(infoBar);
            }
        }

        public void HideInfoText() {
            if (infoText == null) { return; }
            if (infoText.UpdateText(null, null)) {
                S.Sys.Info.UpdateNow(infoText);
            }
        }

        /// <summary>Use in Serialize() to get the ComponentData</summary>
        protected ComponentData GetOrCreateData() {
            if (Data == null) {
                /*
                // this logging is SLOW!
                // D.Log("Giving new component data instance for {0} -> {1}",
                    GetName(), EntityId);
                */
                Data = new ComponentData(EntityId, GetName());
            }
            Data.Context = entity;
            Data.WillSave();
            return Data;
        }

        /// <summary>Override OnInitialize instead. Returns false if component is not created properly</summary>
        public bool Initialize(Entity entity) {
            S = entity.S;
            D.Ass(S != null, "State is null on comp.initialize!");
            this.entity = entity;
            if (entity is Being b) {
                Being = b;
            } else if (entity is Obj o) {
                Obj = o;
            } else if (entity is Tile t) {
                Tile = t;
            }
            Config = entity.Definition.ComponentConfigFor(NameHash);
            if (Config != null) {
                OnConfig();
                return true;
            }
            return false;
        }

        /// <summary>Use to load defaults from Config, and wire other components.
        /// When overriding this, never use S.Rng inside without checking
        /// if it is not null! (Or in game menu will crash)</summary>
        protected virtual void OnConfig() { }
        /// <summary>Use data.Set to persist state</summary>
        public virtual void OnSave() { }

        public virtual void OnReady(bool wasLoaded) { }
        public virtual void OnLateReady(bool wasLoaded) { }
        public virtual void PostLateReady(bool wasLoaded) { }

        /// <summary>Override OnLoad instead</summary>
        public bool Load(Entity entity, ComponentData data) {
            try {
                if (data == null) {
                    data = GetOrCreateData();
                } else {
                    Data = data;
                    data.Context = entity;
                }
                if (Initialize(entity)) {
                    data.WillLoad();
                    OnLoad(data);
                    data.UnsetMode();
                    return true;
                } else {
                    try {
                        #if UNITY_EDITOR
                        if (!The.TestsAreRunning) {
                            D.Warn("Removing a broken component from entity: {0} -> {1}",
                                entity.DefinitionId, GetName());
                        }
                        #else
                        D.Warn("Removing a broken component from entity: {0} -> {1}",
                            entity.DefinitionId, GetName());
                        #endif
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed logging warning about removing broken component: {0}",
                            data?.Component);
                    }
                    entity.RemoveComponent(this);
                    data.UnsetMode();
                    return false;
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed loading component {0} for entity {1}",
                    GetName(), entity.Definition.Id);
                return false;
            }
        }

        /// <summary>Use data.Get to load S. Other components may not have been initialized yet</summary>
        protected virtual void OnLoad(ComponentData data) {}

        /// <summary>Cleanup before removing the entity</summary>
        public virtual void OnRemove() { }

        ///<summary>If you extent any component with another, you have to override this!</summary>
        public virtual IComponent Copy() {
            var t = new T();
            return t;
        }

        public void Transmit(IComponent sender, int message) {
            if (entity == null) {
                D.Err("Trying to transmit a message while entity is null! {1} -> {0}",
                    sender, MsgIdH.ResolveName(message));
                return;
            }
            entity.Transmit(sender, message);
        }

        public virtual void Receive(IComponent sender, int message) { }

        public bool HasGoal => entity.Goal?.Target == this;
        public bool HasAnyGoal => entity.Goal != null;

        public bool TryGetGoal(out AIGoal goal) {
            goal = entity.Goal;
            return goal?.Target == this;
        }

        public bool TryClearGoal(string reason = null) {
            if (entity.Goal?.Target == this) {
                entity.ClearGoal(reason);
                return true;
            }
            return false;
        }

        protected AIGoal CreateGoal(int jobTypeH, string iconId,
                string desc, IAIGoalProvider target) {
            return AIGoal.Create(jobTypeH, desc, target, G.Ticks).WithIcon(iconId);
        }

        public void Validate() {
            Config.CheckUsage(entity.Definition);
        }
    }
}
