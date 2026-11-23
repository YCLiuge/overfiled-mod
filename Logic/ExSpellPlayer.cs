using Il2Cpp;
using mod;
using UnityEngine;
using static Il2CppGCloud.UQM.MiniJSON.Json.Parser;

namespace Logic
{
    enum Exstate
    {
        None,
        Spell,

    }
    internal class ExSpellPlayer
    {
        public static ExSpellPlayer i = new();
        ExSpellPlayer() { if (i == null) i = this; }

        static int chooseid = 0;
        Exstate exstate = Exstate.None;
        public void start()
        {
            stc.Log("ExSpellPlayer");

            
        }
        public void update()
        {

            if (exstate == Exstate.Spell && Input.GetKeyDown(KeyCode.O))
            {
                if (chooseid == 0) return;
                var u = TipMsgCore.i.mainplayer.GetComponent<Unit>();

                SpellSystem.Instance.TryCastActiveSpell(u, chooseid, 1, u, Vector3.zero, null, false, false, out var spell);
            }
            //IExcelSystem.Spell.GetSpellInfo(0).ID;
        }
        public void main_message(string message)
        {
            var msg = message.Split(' ');
            switch (msg[0])
            {
                case "~stop":
                    exstate = Exstate.None;
                    break;
                case "sp":
                    exstate = Exstate.Spell;
                    if (msg.Length == 2)
                    {
                        if (int.TryParse(msg[1], out var id))
                        {
                            //if (IExcelSystem.Spell.GetSpellInfo(chooseid) == null) return;
                            var u = TipMsgCore.i.mainplayer.GetComponent<Unit>();
                            if (SpellSystem.Instance.TryCastActiveSpell(u, id, 1, u, u.transform.forward, null, false, false, out var spell))
                            {
                                stc.Log("技能播放成功" + id);
                                chooseid = id;
                            }
                        }
                    }
                    break;
            }
        }
    }
}
