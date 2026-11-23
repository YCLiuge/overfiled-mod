
using UnityEngine;
using System.IO;
namespace mod.st
{
    public class Net_Vector3
    {
        public float x, y, z;
        public Net_Vector3()
        {
            x = 0;
            y = 0;
            z = 0;
        }
       public Net_Vector3(float x, float y, float z)
        {
            this.x = (float)Math.Round(x, 2);
            this.y = (float)Math.Round(y, 2);
            this.z = (float)Math.Round(z, 2);
        }
       public static implicit operator Net_Vector3(UnityEngine.Vector3 v) 
        {
            return new Net_Vector3(v.x,v.y,v.z);
        }
        public static implicit operator UnityEngine.Vector3(Net_Vector3 v)
        {
            return new UnityEngine.Vector3(v.x, v.y, v.z);
        }
    }
    public class Net_Quaternion
    {
        public float x,y,z,w;
        public Net_Quaternion() { x = 0;y = 0;z = 0;w = 0; }
        public Net_Quaternion(float w, float x, float y,float z)
        {
            this.x = (float)Math.Round(x, 2);
            this.y = (float)Math.Round(y, 2);
            this.z = (float)Math.Round(z, 2);
            this.w = (float)Math.Round(w, 2);
        }

        public static implicit operator Net_Quaternion(UnityEngine.Quaternion q)
        {
            return new Net_Quaternion(q.w, q.x, q.y, q.z);
        }

    }

    public class Net_BoatData
    {
        public Net_Vector3 pos;
        public Net_Vector3 rot;
        public int id;
        public Net_BoatData(Net_Vector3 pos, Net_Vector3 rot,int id)
        {
            this.pos = pos;
            this.rot = rot;
            this.id = id;
        }
        public static implicit operator Net_BoatData(Itempot itempot)
        {
            return new Net_BoatData(itempot.Position,itempot.Rotation.ToEuler(),itempot.id);
        }
        public static implicit operator Itempot(Net_BoatData BoatData)
        {
            return new Itempot(BoatData.pos, Quaternion.Euler(BoatData.rot), BoatData.id, "0");
        }
    }
}
