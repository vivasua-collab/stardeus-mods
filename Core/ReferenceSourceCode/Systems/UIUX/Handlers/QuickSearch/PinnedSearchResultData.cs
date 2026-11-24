using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using MessagePack;

namespace Game.Systems.UIUX {
    [MessagePackObject]
    public class PinnedSearchResultData {
        [Key("AdapterId")] public string AdapterId;
        [Key("ResultId")] public string ResultId;
        public static PinnedSearchResultData[] Serialize(List<BaseSearchResult> pinned) {
            if (pinned.Count == 0) {
                return null;
            }
            var res = new PinnedSearchResultData[pinned.Count];
            for (int i = 0; i < pinned.Count; i++) {
                var p = pinned[i];
                res[i] = Serialize(p);
            }
            return res;
        }

        public static void Deserialize(PinnedSearchResultData[] saved, List<BaseSearchResult> pinned) {
            foreach (var pd in saved) {
                var adapter = Registries.SearchAdapters.Get(pd.AdapterId);
                // order is 0 because it doesn't matter for pinned items, they are
                // saved in sorted order
                var res = adapter.LoadResult(pd.ResultId, 0);
                if (res != null) {
                    pinned.Add(res);
                }
            }
        }

        private static PinnedSearchResultData Serialize(BaseSearchResult res) {
            var data = new PinnedSearchResultData();
            data.AdapterId = res.Adapter.Id;
            data.ResultId = res.Id;
            return data;
        }
    }
}
