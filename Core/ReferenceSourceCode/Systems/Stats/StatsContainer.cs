using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Game.Data;
using KL.Utils;

namespace Game.Systems.Stats {
    public abstract class StatsContainer {
        public Stat[] AllStats;
        protected abstract void LinkDependencies();
        [Conditional("UNITY_EDITOR")]
        protected void Validate() {
            #if UNITY_EDITOR
            foreach (var f in GetType().GetFields(
                    BindingFlags.Public | BindingFlags.Instance)) {
                if (f.FieldType == typeof(Stat)) {
                    var stat = f.GetValue(this) as Stat;
                    if (!AllStats.Contains(stat)) {
                        D.Err("Add {0} to {1}.AllStats you idiot!", f.Name, GetType().Name);
                        UnityEditor.EditorApplication.ExitPlaymode();
                        throw new System.Exception("Fix this shit");
                    }
                }
            }
            #endif
        }

        public void FinalizeStats() {
            LinkDependencies();
            Validate();
        }

        public bool Find(int id, out Stat stat) {
            for (int i = AllStats.Length - 1; i >= 0; i--) {
                Stat st = AllStats[i];
                if (st.Id == id) {
                    stat = st;
                    return true;
                }
            }
            stat = null;
            return false;
        }
        public Stat AddStatSource(int id, IStatSource src) {
            for (int i = AllStats.Length - 1; i >= 0; i--) {
                Stat st = AllStats[i];
                if (st.Id == id) {
                    if (st.Sources == null) {
                        st.Sources = new List<IStatSource>() { src };
                    } else {
                        st.Sources.Add(src);
                    }
                    return st;
                }
            }
            return null;
        }
    }
}
