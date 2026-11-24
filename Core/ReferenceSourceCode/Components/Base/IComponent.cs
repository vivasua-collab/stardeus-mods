using Game.Constants;
using Game.Data;
using Game.Data.Space;
using UnityEngine;

namespace Game.Components {
    public interface IComponent : IWithEntity {
        void CreateComponentClassCache();
        string GetName();
        int TypeHash { get; }
        int NameHash { get; }
        bool SkipTypeLookupCache { get; }
        // This can be set to zero on removal, and I hate it
        int EntityId { get; set; }
        int LayerId { get; set; }
        bool IsUsable { get; }
        bool IsConstructed { get; }
        Map Map { get; }
        void ShowInfoIcon(string iconId, string infoText);
        void ShowThoughtBubble(string iconId, string infoText, int timeoutSeconds = 0);
        void ShowInfoText(string iconId, string infoKey);
        InfoBar ShowInfoBar(Color color, float pct, float value, int max, string infoText);
        void HideAllInfo();
        void HideThoughtBubble();
        void HideInfoIcon();
        void HideInfoText();
        void HideInfoBar();
        // This is called when winch is used. Any component that has
        // cached posIdx or so, should refresh
        void UpdatePosIdx(int pos, int dx, int dy);
        ComponentConfig Config { get; set; }
        ComponentData Data { get; set; }
        // Only called once ?
        bool Initialize(Entity entity);
        // Returns false if component failed to load (should be removed)
        bool Load(Entity entity, ComponentData data);
        /// <summary>Called when we remove the component from an entity</summary>
        void OnRemove();
        /// <summary>Used before save to write data</summary>
        void OnSave();
        /// <summary>Everything is setup</summary>
        void OnReady(bool wasLoaded);
        /// <summary>After OnReady was called for each component</summary>
        void OnLateReady(bool wasLoaded);
        /// <summary>After LateReady called for each component</summary>
        void PostLateReady(bool wasLoaded);
        void Transmit(IComponent sender, int message);
        void Receive(IComponent sender, int message);
        IComponent Copy();
        void Validate();
    }
}
