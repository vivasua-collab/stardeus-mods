using Game.Constants;
using Game.Commands;
using KL.Signals;
using KL.Utils;
using System.Collections;
using System.Threading.Tasks;
using System;
using Game.Data;
using UnityEngine;
using MessagePack;
using System.Text;
using Game.Utils;
using Game.CodeGen;
using UnityEngine.SceneManagement;
using Game.UI;

namespace Game.Commands {
    public class CmdImportShipFromSave : AsyncCommand {
        public override bool TrySameFrame => false;
        public CmdImportShipFromSave() : base("import_ship_from_save") {
        }
        public override IEnumerator ExecuteAsync() {
            D.FIXME_LATER("TODO");
            // 1. read the save file
            // 2. find the offset where we will start placing the ship
            // 3. find the area of the ship
            // 4. remap the coordinates to start where we have to
            // 5. go through every single tile in the save we're importing
            // 6. place the copy of the tile in the map with adjusted position
            // 7. same for beings and stuff
            yield break;
        }
    }
}
