using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [Serializable]
    public class BeltCopy
    {
        [System.NonSerialized]
        public ItemProto itemProto;

        public int protoId;
        public int originalId = 0;
        public Vector3 originalPos;
        public Quaternion originalRot;
        public int beltId;

        public Vector3 cursorRelativePos = Vector3.zero;
        public Vector3[] movesFromReference = new Vector3[0];

        public int backInputId;
        public int leftInputId;
        public int rightInputId;
        public int outputId;

        public string toJSON()
        {
            return "{" +
                $"\"protoId\" : {protoId}," +
                $"\"originalId\" : {originalId}," +
                $"\"originalPos\" : {BlueprintData.Vector3ToJson(originalPos)}," +
                $"\"originalRot\" : {JsonUtility.ToJson(originalRot)}," +
                $"\"beltId\" : {beltId}," +
                $"\"backInputId\" : {backInputId}," +
                $"\"leftInputId\" : {leftInputId}," +
                $"\"rightInputId\" : {rightInputId}," +
                $"\"outputId\" : {outputId}," +
                $"\"cursorRelativePos\" : {BlueprintData.Vector3ToJson(cursorRelativePos)}," +
                $"\"movesFromReference\" : [{movesFromReference.Select(i => BlueprintData.Vector3ToJson(i)).Join(null, ",")}]" +
            "}";
        }
    }

    [Serializable]
    public class BuildingCopy
    {
        [System.NonSerialized]
        public ItemProto itemProto;

        public int protoId;
        public int originalId = 0;
        public Vector3 originalPos;
        public Quaternion originalRot;

        public Vector3 cursorRelativePos = Vector3.zero;
        public Vector3[] movesFromReference = new Vector3[0];
        public float cursorRelativeYaw = 0f;

        public int recipeId;

        public string toJSON()
        {
            return "{" +
                $"\"protoId\" : {protoId}," +
                $"\"originalId\" : {originalId}," +
                $"\"originalPos\" : {BlueprintData.Vector3ToJson(originalPos)}," +
                $"\"originalRot\" : {JsonUtility.ToJson(originalRot)}," +
                $"\"cursorRelativePos\" : {BlueprintData.Vector3ToJson(cursorRelativePos)}," +
                $"\"movesFromReference\" : [{movesFromReference.Select(i => BlueprintData.Vector3ToJson(i)).Join(null, ",")}]," +

                $"\"cursorRelativeYaw\" : {cursorRelativeYaw.ToString("F2")}," +
                $"\"recipeId\" : {recipeId}" +
            "}";
        }
    }

    [Serializable]
    public class InserterCopy
    {
        [System.NonSerialized]
        public ItemProto itemProto;

        public int protoId;
        public int originalId = 0;

        public int pickTarget;
        public int insertTarget;

        public int referenceBuildingId = 0;

        public bool incoming;
        public int startSlot;
        public int endSlot;
        public Vector3 posDelta;
        public Vector3 pos2Delta;
        public Quaternion rot;
        public Quaternion rot2;
        public Vector3[] movesFromReference = new Vector3[0];
        public short pickOffset;
        public short insertOffset;
        public short t1;
        public short t2;
        public int filterId;
        public int refCount;
        public bool otherIsBelt;

        public string toJSON()
        {
            return "{" +
                $"\"protoId\" : {protoId}," +
                $"\"originalId\" : {originalId}," +
                $"\"pickTarget\" : {pickTarget}," +
                $"\"insertTarget\" : {insertTarget}," +
                $"\"referenceBuildingId\" : {referenceBuildingId}," +
                $"\"incoming\" : {(incoming ? "true" : "false")}," +
                $"\"startSlot\" : {startSlot}," +
                $"\"endSlot\" : {endSlot}," +
                $"\"posDelta\" : {BlueprintData.Vector3ToJson(posDelta)}," +
                $"\"pos2Delta\" : {BlueprintData.Vector3ToJson(pos2Delta)}," +
                $"\"rot\" : {JsonUtility.ToJson(rot)}," +
                $"\"rot2\" : {JsonUtility.ToJson(rot2)}," +
                $"\"movesFromReference\" : [{movesFromReference.Select(i => BlueprintData.Vector3ToJson(i)).Join(null, ",")}]," +
                $"\"pickOffset\" : {pickOffset}," +
                $"\"insertOffset\" : {insertOffset}," +
                $"\"t1\" : {t1}," +
                $"\"t2\" : {t2}," +
                $"\"filterId\" : {filterId}," +
                $"\"refCount\" : {refCount}," +
                $"\"otherIsBelt\" : {(otherIsBelt ? "true" : "false")}" +
            "}";
        }
    }

    public class BlueprintData
    {
        public Vector3 referencePos = Vector3.zero;
        public Quaternion inverseReferenceRot = Quaternion.identity;
        public float referenceYaw = 0f;

        public Dictionary<int, BuildingCopy> copiedBuildings = new Dictionary<int, BuildingCopy>();
        public Dictionary<int, InserterCopy> copiedInserters = new Dictionary<int, InserterCopy>();
        public Dictionary<int, BeltCopy> copiedBelts = new Dictionary<int, BeltCopy>();

        public const double JSON_PRECISION = 100;

        public string export()
        {
            var buildings = "{" + copiedBuildings.Select(x => $"\"{x.Key}\": {x.Value.toJSON()}").Join(null, ",\n") + "}";
            var inserters = "{" + copiedInserters.Select(x => $"\"{x.Key}\": {x.Value.toJSON()}").Join(null, ",\n") + "}";
            var belts = "{" + copiedBelts.Select(x => $"\"{x.Key}\": {x.Value.toJSON()}").Join(null, ",\n") + "}";

            var json = "{" +
                $"\"version\": 1," +
                $"\"referencePos\": {Vector3ToJson(referencePos)}," +
                $"\"inverseReferenceRot\": {JsonUtility.ToJson(inverseReferenceRot)}," +
                $"\"buildings\": {buildings}," +
                $"\"inserters\": {inserters}," +
                $"\"belts\": {belts}" +
                "}";

            return Convert.ToBase64String(Zip(json));
        }

        internal static string Vector3ToJson(Vector3 input)
        {
            return "{" +
                $"\"x\": {Math.Round(input.x * JSON_PRECISION) / JSON_PRECISION}," +
                $"\"y\": {Math.Round(input.y * JSON_PRECISION) / JSON_PRECISION}," +
                $"\"z\": {Math.Round(input.z * JSON_PRECISION) / JSON_PRECISION}" +
                "}";
        }

        private void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }

        private byte[] Zip(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    CopyTo(msi, gs);
                }

                return mso.ToArray();
            }
        }

        private string Unzip(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    CopyTo(gs, mso);
                }

                return Encoding.UTF8.GetString(mso.ToArray());
            }
        }
    }
}
