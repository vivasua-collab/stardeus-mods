using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Game.UI {
    public sealed class UISpaceObjectChoice {
        private List<UISpaceObject> objects;
        private UISpaceObject highlighted;
        private Dictionary<UISpaceObject, int> objectIdxs = new();
        public void SetObjects(List<UISpaceObject> objs) {
            objects = objs;
        }

        //public Entity Entity => null;

        public void OnCloseContext() {
            if (highlighted != null) {
                highlighted.transform.SetSiblingIndex(objectIdxs[highlighted]);
                highlighted = null;
            }
        }

        #if false
        public void LoadActions() {
            if (Arrays.IsEmpty(objects)) { return; }
            for (int i = objects.Count - 1; i >= 0; i--) {
                UISpaceObject o = objects[i];
                if (o.SpaceObject?.S == null) {
                    objects.RemoveAt(i);
                }
            }
            if (Arrays.IsEmpty(objects)) { return; }
            OnCloseContext();
            objectIdxs.Clear();
            foreach (var o in objects) {
                objectIdxs.Add(o, o.transform.GetSiblingIndex());
            }
            var s = The.R.State;
            header ??= UDB.Create("so_choice", UDBT.DTextRBHeader,
                IconId.WStarmap, T.Select).AsHeader()
                .WithRBFunction(s.Sig.HideContextMenu.Send);
            res.Add(header);
            foreach (var obj in objects) {
                var name = obj.SpaceObject.Ship?.NameFull ?? obj.SpaceObject.DisplayName;
                res.Add(UDB.Create("obj", UDBT.DBtn, obj.SpaceObject.Icon, name)
                    .WithClickFunction(() => {
                        s.Sig.ShowContextMenu.Send(obj);
                    })
                    .WithIconHoverFunction((h) => {
                        s.UISig.OnHoverUISpaceObject.Send(obj, h);
                        if (h) {
                            // bring to front
                            highlighted = obj;
                            obj.transform.SetAsLastSibling();
                        } else {
                            //restore
                            obj.transform.SetSiblingIndex(objectIdxs[obj]);
                            highlighted = null;
                        }
                    }));
            }
        }
        #endif
    }
}
